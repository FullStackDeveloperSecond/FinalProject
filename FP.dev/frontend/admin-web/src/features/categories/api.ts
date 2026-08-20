import { apiClient } from '../../api/client'
import type { CategoryDto, CreateCategoryRequest, UpdateCategoryRequest } from './types'

export interface CategoryListParams {
  q?: string
  isActive?: boolean
  pageNumber?: number
  pageSize?: number
}

export async function listCategories(params: CategoryListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/categories', {
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

export async function createCategory(request: CreateCategoryRequest): Promise<CategoryDto> {
  const { data } = await apiClient.POST('/api/v1/admin/categories', {
    body: request,
  })
  return data!
}

export async function updateCategory(publicId: string, request: UpdateCategoryRequest): Promise<CategoryDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/categories/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}
