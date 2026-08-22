/**
 * Hand-typed Build List / Compatibility contract, mirroring `BuildListContracts.cs`,
 * `CompatibilityCheckContracts.cs` and their controllers on `feature/build-compat-api`
 * (not merged to `dev` yet, so there is no live API to export `frontend/shared`'s generated
 * OpenAPI schema from). This is a stand-in for that generated schema, not a parallel
 * hand-written DTO system — the shape matches what codegen would produce and is proven
 * correct by the backend's own HTTP integration tests (BuildListsApiTests). Once
 * build-compat-api merges to `dev` and `frontend/shared`'s `api:generate` is run for real,
 * this file should be deleted and `features/builds/api.ts` switched to import `paths`/
 * `components` from `@doselect/web-shared/api` like `features/catalog` does.
 */

export interface BuildItemDto {
  publicId: string
  skuPublicId: string
  skuCode: string
  name: string
  quantity: number
  sortOrder: number
  unitPrice: number
  lineTotal: number
  availability: 'available' | 'unavailable' | 'insufficient_stock'
}

export type CompatibilitySeverity =
  | 'compatible' | 'warning' | 'blocked' | 'insufficientData' | 'ruleDisabled'

export interface CompatibilityFindingDto {
  ruleCode: string
  severity: CompatibilitySeverity
  messageKey: string
  subjectSkuPublicIds: string[]
  facts: Record<string, unknown>
}

export type CompatibilityOverall = 'compatible' | 'warning' | 'blocked' | 'insufficientData'

export interface BuildCompatibilitySummaryDto {
  overall: CompatibilityOverall
  ruleSetVersion: number
  settingsVersion: number
  results: CompatibilityFindingDto[]
}

export interface BuildTotalsDto {
  merchandise: number
  assemblyFee: number
  grandTotal: number
  currency: string
}

export interface BuildListDto {
  publicId: string
  name: string
  items: BuildItemDto[]
  compatibility: BuildCompatibilitySummaryDto
  totals: BuildTotalsDto
  updatedAtUtc: string
  rowVersion: string
}

export interface BuildListSummaryDto {
  publicId: string
  name: string
  itemCount: number
  compatibilityOverall: CompatibilityOverall
  grandTotal: number
  isShared: boolean
  updatedAtUtc: string
  rowVersion: string
}

export interface PageResultDto<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface BuildItemInput {
  skuPublicId: string
  quantity: number
}

export interface CreateBuildListRequest {
  name: string
  items: BuildItemInput[]
}

export interface UpdateBuildListRequest {
  name: string
  items: BuildItemInput[]
  rowVersion: string
}

export interface BuildShareDto {
  sharePublicId: string
  url: string
  expiresAtUtc: string | null
}

export interface SharedBuildDto {
  sharePublicId: string
  name: string
  items: BuildItemDto[]
  compatibility: BuildCompatibilitySummaryDto
  totals: BuildTotalsDto
  canCopy: boolean
  canAddToCart: boolean
}

export interface AddBuildToCartRequest {
  quantity: number
  buildRowVersion: string
}

export interface CompatibilityCheckRequest {
  items: BuildItemInput[]
}

export interface CompatibilityCheckDto {
  overall: CompatibilityOverall
  ruleSetVersion: number
  settingsVersion: number
  results: CompatibilityFindingDto[]
  evaluatedAtUtc: string
}

interface JsonResponse<T> {
  content: {
    'application/json': T
  }
}

interface ProblemResponse {
  content: {
    'application/problem+json': { code: string }
  }
}

export interface BuildsApiPaths {
  '/api/v1/build-lists': {
    get: {
      parameters: { query: { pageNumber: number, pageSize: number } }
      responses: {
        200: JsonResponse<PageResultDto<BuildListSummaryDto>>
        400: ProblemResponse
      }
    }
    post: {
      requestBody: { content: { 'application/json': CreateBuildListRequest } }
      responses: {
        200: JsonResponse<BuildListDto>
        400: ProblemResponse
      }
    }
  }
  '/api/v1/build-lists/{id}': {
    get: {
      parameters: { path: { id: string } }
      responses: {
        200: JsonResponse<BuildListDto>
        404: ProblemResponse
      }
    }
    put: {
      parameters: { path: { id: string } }
      requestBody: { content: { 'application/json': UpdateBuildListRequest } }
      responses: {
        200: JsonResponse<BuildListDto>
        400: ProblemResponse
        404: ProblemResponse
        409: ProblemResponse
      }
    }
    delete: {
      parameters: { path: { id: string } }
      requestBody: { content: { 'application/json': { rowVersion: string } } }
      responses: {
        200: JsonResponse<null>
        404: ProblemResponse
        409: ProblemResponse
      }
    }
  }
  '/api/v1/build-lists/{id}/share': {
    post: {
      parameters: { path: { id: string } }
      responses: {
        200: JsonResponse<BuildShareDto>
        404: ProblemResponse
      }
    }
    delete: {
      parameters: { path: { id: string } }
      responses: {
        200: JsonResponse<null>
        404: ProblemResponse
      }
    }
  }
  '/api/v1/build-shares/{token}': {
    get: {
      parameters: { path: { token: string } }
      responses: {
        200: JsonResponse<SharedBuildDto>
        404: ProblemResponse
      }
    }
  }
  '/api/v1/build-lists/{id}/actions/add-to-cart': {
    post: {
      parameters: { path: { id: string } }
      requestBody: { content: { 'application/json': AddBuildToCartRequest } }
      responses: {
        200: JsonResponse<unknown>
        400: ProblemResponse
        404: ProblemResponse
        409: ProblemResponse
      }
    }
  }
  '/api/v1/compatibility-checks': {
    post: {
      requestBody: { content: { 'application/json': CompatibilityCheckRequest } }
      responses: {
        200: JsonResponse<CompatibilityCheckDto>
        400: ProblemResponse
      }
    }
  }
}
