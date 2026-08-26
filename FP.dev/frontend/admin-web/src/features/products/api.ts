import { apiClient } from '../../api/client'
import type { AdminProductDetailDto, CreateProductRequest, UpdateProductRequest } from './types'

export interface AdminProductListParams {
  q?: string
  brandCodes?: string[]
  categoryCodes?: string[]
  statuses?: string[]
  stockState?: string
  sort?: string
  pageNumber?: number
  pageSize?: number
}

export async function listAdminProducts(params: AdminProductListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/products', {
    params: {
      query: {
        Q: params.q || undefined,
        BrandCodes: params.brandCodes,
        CategoryCodes: params.categoryCodes,
        Statuses: params.statuses,
        StockState: params.stockState || undefined,
        Sort: params.sort || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function getAdminProduct(publicId: string): Promise<AdminProductDetailDto> {
  const { data } = await apiClient.GET('/api/v1/admin/products/{id}', {
    params: { path: { id: publicId } },
  })
  return data!
}

export async function createProduct(request: CreateProductRequest): Promise<AdminProductDetailDto> {
  const { data } = await apiClient.POST('/api/v1/admin/products', {
    body: request,
  })
  return data!
}

export async function updateProduct(
  publicId: string,
  request: UpdateProductRequest,
): Promise<AdminProductDetailDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/products/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}
