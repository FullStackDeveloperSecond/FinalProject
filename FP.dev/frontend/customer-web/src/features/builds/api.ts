import { createApiClient } from '../../api/client'
import type {
  AddBuildToCartRequest,
  BuildListDto,
  BuildListSummaryDto,
  BuildShareDto,
  BuildsApiPaths,
  CompatibilityCheckDto,
  CompatibilityCheckRequest,
  CreateBuildListRequest,
  PageResultDto,
  SharedBuildDto,
  UpdateBuildListRequest,
} from './types'

const buildsApiClient = createApiClient<BuildsApiPaths>()

/**
 * The shared API client's middleware throws an `ApiError` for any non-OK response (see
 * `frontend/shared/src/api/client.ts`), so `data` is always populated on the success path
 * handled here — callers do not need to additionally check openapi-fetch's own `error` field.
 */
export async function listBuildLists(pageNumber: number, pageSize: number): Promise<PageResultDto<BuildListSummaryDto>> {
  const { data } = await buildsApiClient.GET('/api/v1/build-lists', {
    params: { query: { pageNumber, pageSize } },
  })
  return data!
}

export async function getBuildList(publicId: string): Promise<BuildListDto> {
  const { data } = await buildsApiClient.GET('/api/v1/build-lists/{id}', {
    params: { path: { id: publicId } },
  })
  return data!
}

export async function createBuildList(request: CreateBuildListRequest): Promise<BuildListDto> {
  const { data } = await buildsApiClient.POST('/api/v1/build-lists', { body: request })
  return data!
}

export async function updateBuildList(publicId: string, request: UpdateBuildListRequest): Promise<BuildListDto> {
  const { data } = await buildsApiClient.PUT('/api/v1/build-lists/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}

export async function deleteBuildList(publicId: string, rowVersion: string): Promise<void> {
  await buildsApiClient.DELETE('/api/v1/build-lists/{id}', {
    params: { path: { id: publicId } },
    body: { rowVersion },
  })
}

export async function createBuildShare(publicId: string): Promise<BuildShareDto> {
  const { data } = await buildsApiClient.POST('/api/v1/build-lists/{id}/share', {
    params: { path: { id: publicId } },
  })
  return data!
}

export async function revokeBuildShare(publicId: string): Promise<void> {
  await buildsApiClient.DELETE('/api/v1/build-lists/{id}/share', {
    params: { path: { id: publicId } },
  })
}

export async function getSharedBuild(token: string): Promise<SharedBuildDto> {
  const { data } = await buildsApiClient.GET('/api/v1/build-shares/{token}', {
    params: { path: { token } },
  })
  return data!
}

/**
 * Returns the updated Cart as `unknown` — no `features/cart` exists on this branch to import
 * a `CartDto` from yet (cart-frontend isn't merged here either). Callers only need to know
 * the call succeeded; a future integration can type this properly once cart-frontend merges.
 */
export async function addBuildToCart(
  publicId: string,
  request: AddBuildToCartRequest,
  idempotencyKey: string,
): Promise<unknown> {
  const { data } = await buildsApiClient.POST('/api/v1/build-lists/{id}/actions/add-to-cart', {
    params: { path: { id: publicId } },
    body: request,
    headers: { 'Idempotency-Key': idempotencyKey },
  })
  return data
}

/** Public, unauthenticated — used for the live compatibility preview before a build is saved (UC-COMPAT-01). */
export async function checkCompatibility(request: CompatibilityCheckRequest): Promise<CompatibilityCheckDto> {
  const { data } = await buildsApiClient.POST('/api/v1/compatibility-checks', { body: request })
  return data!
}
