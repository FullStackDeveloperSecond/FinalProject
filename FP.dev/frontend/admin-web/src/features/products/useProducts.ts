import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  applyBulkProductAction,
  createProduct,
  deleteProductImage,
  exportAdminProducts,
  getAdminProduct,
  listAdminProducts,
  publishProductImage,
  updateProduct,
  updateProductImage,
  uploadProductImage,
  type AdminProductListParams,
} from './api'
import type {
  AdminProductExportFormat,
  BulkProductAction,
  BulkProductActionRequest,
  CreateProductRequest,
  ProductImageUploadInput,
  UpdateProductImageRequest,
  UpdateProductRequest,
} from './types'

export function useAdminProductList(params: MaybeRefOrGetter<AdminProductListParams>) {
  return useQuery({
    queryKey: computed(() => ['admin-products', 'list', toValue(params)] as const),
    queryFn: () => listAdminProducts(toValue(params)),
    // 組長在 PR #78 item 1 與 PR #79 item 1／3 連續指出的同一個反模式：query key 的身分變了
    // （這裡是篩選條件與頁碼），`placeholderData` 卻把上一組結果畫在新的 key 底下。加入批次選取
    // 之後這不只是顯示問題——管理員改了篩選、畫面還留著上一組商品而且勾得動，就會對「根本不在
    // 眼前這份清單裡」的商品批次上下架或調價。寧可閃一下載入中。
  })
}

export function useAdminProductDetail(publicId: MaybeRefOrGetter<string | undefined>) {
  return useQuery({
    queryKey: computed(() => ['admin-products', 'detail', toValue(publicId)] as const),
    queryFn: () => getAdminProduct(toValue(publicId) as string),
    enabled: computed(() => Boolean(toValue(publicId))),
  })
}

export function useCreateProduct() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateProductRequest) => createProduct(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-products', 'list'] }),
  })
}

export function useUpdateProduct() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateProductRequest }) =>
      updateProduct(publicId, request),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['admin-products', 'list'] })
      queryClient.invalidateQueries({ queryKey: ['admin-products', 'detail', variables.publicId] })
    },
  })
}

/**
 * UC-ADM-PROD-02 批次動作。成功後把列表整個失效重取：狀態或價格改了，畫面上的 RowVersion 也全
 * 部過期，不重取的話下一次批次動作會直接撞 409。
 */
export function useBulkProductAction() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ action, request }: { action: BulkProductAction, request: BulkProductActionRequest }) =>
      applyBulkProductAction(action, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-products'] }),
  })
}

/** A-04 匯出：拿到 Blob 後觸發瀏覽器下載，形狀比照 useOperationalReport 的 download。 */
export function useExportProducts() {
  return useMutation({
    mutationFn: async (
      { params, format }: { params: AdminProductListParams, format: AdminProductExportFormat },
    ) => {
      const blob = await exportAdminProducts(params, format)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = `products-${new Date().toISOString().slice(0, 10)}.${format}`
      link.click()
      URL.revokeObjectURL(url)
    },
  })
}

// ---------------------------------------------------------------- M-03 商品圖片（A-06）
//
// 圖片是獨立的 Aggregate：四個動作都只讓「這個商品的詳情」與列表（主圖）失效重取，
// 商品本身的 RowVersion 不會變，頁面上的商品表單不用重抓 token。

function invalidateProductImages(queryClient: ReturnType<typeof useQueryClient>, productPublicId: string) {
  queryClient.invalidateQueries({ queryKey: ['admin-products', 'detail', productPublicId] })
  queryClient.invalidateQueries({ queryKey: ['admin-products', 'list'] })
}

export function useUploadProductImage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ productPublicId, input }: { productPublicId: string, input: ProductImageUploadInput }) =>
      uploadProductImage(productPublicId, input),
    onSuccess: (_data, variables) => invalidateProductImages(queryClient, variables.productPublicId),
  })
}

export function useUpdateProductImage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ imagePublicId, request }: { productPublicId: string, imagePublicId: string, request: UpdateProductImageRequest }) =>
      updateProductImage(imagePublicId, request),
    onSuccess: (_data, variables) => invalidateProductImages(queryClient, variables.productPublicId),
  })
}

export function usePublishProductImage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ imagePublicId, rowVersion }: { productPublicId: string, imagePublicId: string, rowVersion: string }) =>
      publishProductImage(imagePublicId, rowVersion),
    onSuccess: (_data, variables) => invalidateProductImages(queryClient, variables.productPublicId),
  })
}

export function useDeleteProductImage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ imagePublicId, rowVersion }: { productPublicId: string, imagePublicId: string, rowVersion: string }) =>
      deleteProductImage(imagePublicId, rowVersion),
    onSuccess: (_data, variables) => invalidateProductImages(queryClient, variables.productPublicId),
  })
}
