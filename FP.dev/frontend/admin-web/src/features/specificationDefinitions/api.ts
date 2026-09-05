import { apiClient } from '../../api/client'
import type {
  CreateSpecificationDefinitionRequest,
  DisableSpecificationDefinitionRequest,
  SpecificationDefinitionDto,
  UpdateSpecificationDefinitionRequest,
} from './types'

export interface SpecificationDefinitionListParams {
  categoryPublicId?: string
  q?: string
  isActive?: boolean
  pageNumber?: number
  pageSize?: number
}

export async function listSpecificationDefinitions(params: SpecificationDefinitionListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/specification-definitions', {
    params: {
      query: {
        CategoryPublicId: params.categoryPublicId || undefined,
        Q: params.q || undefined,
        IsActive: params.isActive,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function createSpecificationDefinition(
  request: CreateSpecificationDefinitionRequest,
): Promise<SpecificationDefinitionDto> {
  const { data } = await apiClient.POST('/api/v1/admin/specification-definitions', { body: request })
  return data!
}

export async function updateSpecificationDefinition(
  publicId: string,
  request: UpdateSpecificationDefinitionRequest,
): Promise<SpecificationDefinitionDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/specification-definitions/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}

export async function disableSpecificationDefinition(
  publicId: string,
  request: DisableSpecificationDefinitionRequest,
): Promise<SpecificationDefinitionDto> {
  const { data } = await apiClient.POST('/api/v1/admin/specification-definitions/{id}/actions/disable', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}
