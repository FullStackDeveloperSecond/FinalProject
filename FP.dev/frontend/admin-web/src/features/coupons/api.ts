import { apiClient } from '../../api/client'
import type {
  CouponAction,
  CouponActionRequest,
  CouponDto,
  CouponStatus,
  CreateCouponRequest,
  UpdateCouponRequest,
} from './types'

export interface CouponListParams {
  q?: string
  statuses?: CouponStatus[]
  sort?: string
  pageNumber?: number
  pageSize?: number
}

/**
 * The shared API client's middleware throws an `ApiError` for any non-OK
 * response, so `data` is always populated on the success path handled here.
 */
export async function listCoupons(params: CouponListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/coupons', {
    params: {
      query: {
        Q: params.q || undefined,
        // 空陣列會送出 `Statuses=`，後端會當成「篩選了零個狀態」而回空清單。
        Statuses: params.statuses?.length ? params.statuses : undefined,
        Sort: params.sort || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function getCoupon(publicId: string): Promise<CouponDto> {
  const { data } = await apiClient.GET('/api/v1/admin/coupons/{id}', {
    params: { path: { id: publicId } },
  })
  return data!
}

export async function createCoupon(request: CreateCouponRequest): Promise<CouponDto> {
  const { data } = await apiClient.POST('/api/v1/admin/coupons', { body: request })
  return data!
}

export async function updateCoupon(
  publicId: string,
  request: UpdateCouponRequest,
): Promise<CouponDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/coupons/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}

/**
 * 執行 `activate`／`pause`／`disable`。
 *
 * `rowVersion` 是必填的樂觀鎖：後端以它做條件更新，版本過期時回
 * `concurrency_conflict`，不會覆蓋別人的修改。
 */
export async function executeCouponAction(
  publicId: string,
  action: CouponAction,
  request: CouponActionRequest,
): Promise<CouponDto> {
  const { data } = await apiClient.POST('/api/v1/admin/coupons/{id}/actions/{couponAction}', {
    params: { path: { id: publicId, couponAction: action } },
    body: request,
  })
  return data!
}
