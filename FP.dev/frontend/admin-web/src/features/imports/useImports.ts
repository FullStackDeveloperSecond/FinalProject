import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  confirmInventoryImport,
  confirmProductImport,
  downloadInventoryImportErrors,
  downloadProductImportErrors,
  downloadProductImportTemplate,
  getInventoryImportBatch,
  getInventoryImportRows,
  getProductImportBatch,
  getProductImportRows,
  previewInventoryImport,
  previewProductImport,
  type ProductImportFiles,
} from './api'
import type { AnyImportRowsPage } from './types'

/** 目前的模板版本，與後端的 CurrentTemplateVersion 一致。 */
export const CurrentTemplateVersion = 1

export type ImportKind = 'product' | 'inventory'

/**
 * A-07 與 A-13 的資料存取。兩頁的流程一模一樣（上傳→預覽→修正→確認），差別只在打哪一組端點，
 * 所以用 kind 分流而不是寫兩份幾乎相同的 composable。
 */
export function useImportBatch(kind: ImportKind, batchId: MaybeRefOrGetter<string | null>) {
  return useQuery({
    queryKey: computed(() => ['imports', kind, 'batch', toValue(batchId)] as const),
    queryFn: () => (kind === 'product'
      ? getProductImportBatch(toValue(batchId)!)
      : getInventoryImportBatch(toValue(batchId)!)),
    enabled: computed(() => Boolean(toValue(batchId))),
    // 刻意不用 placeholderData：換一個批次就是換一份資料，畫面上不該短暫顯示上一個批次的統計，
    // 那些數字會被拿來決定要不要按下「確認匯入」。
  })
}

export interface ImportRowsFilter {
  errorsOnly?: boolean
  pageSize?: number
}

/**
 * 組長 PR #89 item 5：「載入更多」是累加，不是換頁。上一版只改 cursor，新的一頁會取代上一頁，
 * 超過 50 列就沒辦法連續檢視、也回不去。改用 infinite query：每一頁的游標由上一頁的 nextCursor
 * 提供，畫面把所有頁攤平。換篩選條件（只看錯誤列）就是換 key，從第一頁重來。
 */
export function useImportRows(
  kind: ImportKind,
  batchId: MaybeRefOrGetter<string | null>,
  filter: MaybeRefOrGetter<ImportRowsFilter>,
) {
  return useInfiniteQuery({
    queryKey: computed(() => ['imports', kind, 'rows', toValue(batchId), toValue(filter)] as const),
    queryFn: ({ pageParam }): Promise<AnyImportRowsPage> => (kind === 'product'
      ? getProductImportRows(toValue(batchId)!, { ...toValue(filter), cursor: pageParam })
      : getInventoryImportRows(toValue(batchId)!, { ...toValue(filter), cursor: pageParam })),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: last => (last.hasMore ? last.nextCursor ?? undefined : undefined),
    enabled: computed(() => Boolean(toValue(batchId))),
  })
}

export function usePreviewImport(kind: ImportKind) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (files: ProductImportFiles & { adjustments?: File }) =>
      (kind === 'product'
        ? previewProductImport(
            {
              products: files.products,
              skus: files.skus,
              specifications: files.specifications,
              workbook: files.workbook,
            },
            CurrentTemplateVersion)
        : previewInventoryImport(files.adjustments!, CurrentTemplateVersion)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['imports', kind] }),
  })
}

export function useConfirmImport(kind: ImportKind) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ batchId, rowVersion }: { batchId: string, rowVersion: string }) =>
      (kind === 'product'
        ? confirmProductImport(batchId, rowVersion)
        : confirmInventoryImport(batchId, rowVersion)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['imports', kind] }),
  })
}

export function useDownloadImportErrors(kind: ImportKind) {
  return useMutation({
    mutationFn: async (batchId: string) => {
      const blob = kind === 'product'
        ? await downloadProductImportErrors(batchId)
        : await downloadInventoryImportErrors(batchId)
      triggerDownload(blob, `${kind}-import-${batchId}-errors.csv`)
    },
  })
}

export function useDownloadProductTemplate() {
  return useMutation({
    mutationFn: async () => {
      const blob = await downloadProductImportTemplate()
      triggerDownload(blob, `doselect-product-import-template-v${CurrentTemplateVersion}.zip`)
    },
  })
}

/** 形狀比照 useOperationalReport 的 download。 */
function triggerDownload(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
