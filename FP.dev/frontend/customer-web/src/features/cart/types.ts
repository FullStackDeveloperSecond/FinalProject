/**
 * Hand-typed Cart contract, mirroring `ShoppingCartContracts.cs` on `feature/cart-api`
 * (not merged to `dev` yet, so there is no live API to export `frontend/shared`'s generated
 * OpenAPI schema from). This is a stand-in for that generated schema, not a parallel
 * hand-written DTO system — the shape matches what codegen would produce and is proven correct
 * by the backend's own HTTP integration tests (CartApiTests). Once Cart merges to `dev` and
 * `frontend/shared`'s `api:generate` is run for real, this file should be deleted and
 * `features/cart/api.ts` switched to import `paths`/`components` from `@doselect/web-shared/api`
 * like `features/catalog` does.
 *
 * PR #29 review round 2 (組長): this drifted from cart-api's real, current shape (flat
 * subtotal/couponDiscountAmount/totalEstimate fields, a couponAllocatedDiscount per item) —
 * cart-api actually nests amounts under `CartAmountsDto` and has no per-item coupon field at
 * all (coupon logic isn't wired into Cart yet, see CouponAppliedDto below). Kept in exact sync
 * with `ShoppingCartContracts.cs` as of that file's current head — re-check against the real
 * file before trusting this again if it's been a while.
 */

export interface CartWarningDto {
  code: string
  message: string
}

export interface CartItemDto {
  publicId: string
  skuPublicId: string
  skuCode: string
  name: string
  quantity: number
  unitPrice: number
  lineTotal: number
  availability: 'available' | 'unavailable' | 'insufficient_stock'
  priceChanged: boolean
  maxPurchasableQuantity: number
  assemblyGroupKey: string | null
  rowVersion: string
}

/** Placeholder shape pending yinyin's coupon integration — always null until then (CartDto.Coupon remarks in ShoppingCartContracts.cs). */
export interface CouponAppliedDto {
  code: string
  discountAmount: number
}

/** Matches API DTO與Schema契約.md's `amounts{...}` object exactly. */
export interface CartAmountsDto {
  subtotal: number
  itemDiscount: number
  couponDiscount: number
  shippingEstimate: number | null
  assemblyFee: number
  totalEstimate: number
  currency: string
}

export interface CartDto {
  publicId: string
  items: CartItemDto[]
  coupon: CouponAppliedDto | null
  amounts: CartAmountsDto
  warnings: CartWarningDto[]
  rowVersion: string
}

export interface CartIssueDto {
  itemPublicId: string | null
  code: string
  severity: 'error' | 'warning'
  availableActions: string[]
}

export interface CartValidationDto {
  cart: CartDto
  isCheckoutReady: boolean
  issues: CartIssueDto[]
  validatedAtUtc: string
}

export interface CartMergeConflictDto {
  guestItemPublicId: string
  skuPublicId: string
  reason: string
  acceptedQuantity: number
}

export interface CartMergeResultDto {
  cart: CartDto
  conflicts: CartMergeConflictDto[]
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

export interface CartApiPaths {
  '/api/v1/cart': {
    get: {
      responses: {
        200: JsonResponse<CartDto>
        400: ProblemResponse
      }
    }
  }
  '/api/v1/cart/items': {
    post: {
      requestBody: {
        content: {
          'application/json': {
            skuPublicId: string
            quantity: number
            cartRowVersion: string | null
          }
        }
      }
      responses: {
        200: JsonResponse<CartDto>
        400: ProblemResponse
        404: ProblemResponse
        409: ProblemResponse
      }
    }
  }
  '/api/v1/cart/items/{id}': {
    patch: {
      parameters: { path: { id: string } }
      requestBody: {
        content: {
          'application/json': {
            quantity: number
            itemRowVersion: string
            cartRowVersion: string
          }
        }
      }
      responses: {
        200: JsonResponse<CartDto>
        400: ProblemResponse
        404: ProblemResponse
        409: ProblemResponse
      }
    }
    delete: {
      parameters: { path: { id: string } }
      requestBody: {
        content: {
          'application/json': {
            itemRowVersion: string
          }
        }
      }
      responses: {
        200: JsonResponse<CartDto>
        400: ProblemResponse
        404: ProblemResponse
        409: ProblemResponse
      }
    }
  }
  '/api/v1/cart/actions/revalidate': {
    post: {
      responses: {
        200: JsonResponse<CartValidationDto>
        400: ProblemResponse
      }
    }
  }
  '/api/v1/cart/actions/merge': {
    post: {
      requestBody: {
        content: {
          'application/json': {
            guestCartKey: string
            strategy: 'mergeAndReportConflicts'
            idempotencyKey: string
          }
        }
      }
      responses: {
        // PR #29 review round 2: a merge rejected for the 100-item cap (PR #28's round-3/4
        // ruling) returns 409 with a real CartMergeResultDto body (Cart unchanged + the
        // blocking conflict), not a ProblemDetails — the shared client's error middleware
        // only recognizes Content-Type: application/problem+json as a "known" error shape, so
        // this 409's body currently still ends up discarded into a generic thrown ApiError
        // once something actually calls this endpoint. Not fixed here: nothing calls
        // mergeCartOnLogin yet (see api.ts's remarks) — whoever wires up the real login flow
        // needs to either teach the shared middleware about this non-ProblemDetails error body,
        // or bypass it for this one call.
        200: JsonResponse<CartMergeResultDto>
        400: ProblemResponse
        401: ProblemResponse
        409: JsonResponse<CartMergeResultDto>
      }
    }
  }
}
