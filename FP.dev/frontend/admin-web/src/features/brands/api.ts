import { apiClient } from '../../api/client'
import type { BrandDto, CreateBrandRequest, UpdateBrandRequest } from './types'

export interface BrandListParams {
  q?: string
  isActive?: boolean
  pageNumber?: number
  pageSize?: number
}

/**
 * The shared API client's middleware throws an `ApiError` for any non-OK
 * response, so `data` is always populated on the success path handled here.
 */
export async function listBrands(params: BrandListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/brands', {
    params: {
      query: {
        Q: params.q || undefined,
        IsActive: params.isActive,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function createBrand(request: CreateBrandRequest): Promise<BrandDto> {
  const { data } = await apiClient.POST('/api/v1/admin/brands', {
    body: request,
  })
  return data!
}

export async function updateBrand(publicId: string, request: UpdateBrandRequest): Promise<BrandDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/brands/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}
