import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
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
  type ImportRowsParams,
} from './api'

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

export function useImportRows(
  kind: ImportKind,
  batchId: MaybeRefOrGetter<string | null>,
  params: MaybeRefOrGetter<ImportRowsParams>,
) {
  return useQuery({
    queryKey: computed(() => ['imports', kind, 'rows', toValue(batchId), toValue(params)] as const),
    queryFn: () => (kind === 'product'
      ? getProductImportRows(toValue(batchId)!, toValue(params))
      : getInventoryImportRows(toValue(batchId)!, toValue(params))),
    enabled: computed(() => Boolean(toValue(batchId))),
  })
}

export function usePreviewImport(kind: ImportKind) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (files: { products?: File, skus?: File, specifications?: File, adjustments?: File }) =>
      (kind === 'product'
        ? previewProductImport(
            {
              products: files.products!,
              skus: files.skus!,
              specifications: files.specifications!,
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
