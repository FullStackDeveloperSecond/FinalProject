import { apiClient } from '../../api/client'
import type { SpecFilterRequest } from './types'
import { productSearchQuerySerializer } from './querySerializer'

export interface ProductSearchParams {
  q?: string
  category?: string
  brand?: string
  minPrice?: number
  maxPrice?: number
  inStock?: boolean
  specs?: SpecFilterRequest[]
  sort?: string
  pageNumber?: number
  pageSize?: number
}

/**
 * The shared API client's middleware throws an `ApiError` for any non-OK
 * response (see `frontend/shared/src/api/client.ts`), so `data` is always
 * populated on the success path handled here — callers do not need to
 * additionally check openapi-fetch's own `error` field.
 */
export async function searchProducts(params: ProductSearchParams) {
  const { data } = await apiClient.GET('/api/v1/products', {
    params: {
      query: {
        Q: params.q || undefined,
        Category: params.category || undefined,
        Brand: params.brand || undefined,
        MinPrice: params.minPrice,
        MaxPrice: params.maxPrice,
        InStock: params.inStock,
        Specs: params.specs && params.specs.length > 0 ? params.specs : undefined,
        Sort: params.sort || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
    // PR #24 review round 2: Specs is an array of objects, which the default querySerializer
    // can't handle — see querySerializer.ts.
    querySerializer: productSearchQuerySerializer,
  })
  return data!
}

export async function getProductDetail(productPublicId: string) {
  const { data } = await apiClient.GET('/api/v1/products/{id}', {
    params: { path: { id: productPublicId } },
  })
  return data!
}

export async function getCatalogFilterOptions(category?: string) {
  const { data } = await apiClient.GET('/api/v1/catalog/filter-options', {
    params: { query: { Category: category || undefined } },
  })
  return data!
}
