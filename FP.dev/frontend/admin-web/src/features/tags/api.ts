import { apiClient } from '../../api/client'
import type { CatalogLookupDto, CreateTagRequest, UpdateTagRequest } from './types'

export interface TagListParams {
  q?: string
  isActive?: boolean
  pageNumber?: number
  pageSize?: number
}

export async function listTags(params: TagListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/tags', {
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

export async function createTag(request: CreateTagRequest): Promise<CatalogLookupDto> {
  const { data } = await apiClient.POST('/api/v1/admin/tags', {
    body: request,
  })
  return data!
}

export async function updateTag(publicId: string, request: UpdateTagRequest): Promise<CatalogLookupDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/tags/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}
