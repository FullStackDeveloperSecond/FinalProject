import { apiClient } from '../../api/client'
import type {
  ConvenienceStoreDto,
  CreateConvenienceStoreRequest,
  CreatePackageLimitVersionRequest,
  PackageLimitVersionDto,
  PublishPackageLimitVersionRequest,
  UpdateConvenienceStoreRequest,
} from './types'

export interface ConvenienceStoreListParams {
  providerCode?: string
  city?: string
  district?: string
  isActive?: boolean
  pageNumber?: number
  pageSize?: number
}

export async function listConvenienceStores(params: ConvenienceStoreListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/convenience-stores', {
    params: {
      query: {
        ProviderCode: params.providerCode || undefined,
        City: params.city || undefined,
        District: params.district || undefined,
        IsActive: params.isActive,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function createConvenienceStore(
  request: CreateConvenienceStoreRequest,
): Promise<ConvenienceStoreDto> {
  const { data } = await apiClient.POST('/api/v1/admin/convenience-stores', { body: request })
  return data!
}

export async function updateConvenienceStore(
  publicId: string,
  request: UpdateConvenienceStoreRequest,
): Promise<ConvenienceStoreDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/convenience-stores/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}

export async function listPackageLimitVersions(providerCode: string): Promise<PackageLimitVersionDto[]> {
  const { data } = await apiClient.GET('/api/v1/admin/shipping-providers/{id}/package-limit-versions', {
    params: { path: { id: providerCode } },
  })
  return data!
}

export async function createPackageLimitVersion(
  providerCode: string,
  request: CreatePackageLimitVersionRequest,
): Promise<PackageLimitVersionDto> {
  const { data } = await apiClient.POST('/api/v1/admin/shipping-providers/{id}/package-limit-versions', {
    params: { path: { id: providerCode } },
    body: request,
  })
  return data!
}

export async function publishPackageLimitVersion(
  providerCode: string,
  versionPublicId: string,
  request: PublishPackageLimitVersionRequest,
): Promise<PackageLimitVersionDto> {
  const { data } = await apiClient.POST(
    '/api/v1/admin/shipping-providers/{id}/package-limit-versions/{versionId}/actions/publish',
    {
      params: { path: { id: providerCode, versionId: versionPublicId } },
      body: request,
    },
  )
  return data!
}
