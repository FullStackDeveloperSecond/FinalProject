import { apiClient } from '../../api/client'
import type { CreateSkuRequest, SkuDto, UpdateSkuRequest } from './types'

export async function createSku(productPublicId: string, request: CreateSkuRequest): Promise<SkuDto> {
  const { data } = await apiClient.POST('/api/v1/admin/products/{productId}/skus', {
    params: { path: { productId: productPublicId } },
    body: request,
  })
  return data!
}

export async function updateSku(skuPublicId: string, request: UpdateSkuRequest): Promise<SkuDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/skus/{id}', {
    params: { path: { id: skuPublicId } },
    body: request,
  })
  return data!
}

export async function deleteSku(skuPublicId: string, rowVersion: string): Promise<void> {
  await apiClient.DELETE('/api/v1/admin/skus/{id}', {
    params: { path: { id: skuPublicId } },
    body: { rowVersion },
  })
}
