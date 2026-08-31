import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  createCoupon,
  executeCouponAction,
  getCoupon,
  listCoupons,
  updateCoupon,
  type CouponListParams,
} from './api'
import type {
  CouponAction,
  CouponActionRequest,
  CreateCouponRequest,
  UpdateCouponRequest,
} from './types'

export function useCouponList(params: MaybeRefOrGetter<CouponListParams>) {
  return useQuery({
    queryKey: computed(() => ['coupons', 'list', toValue(params)] as const),
    queryFn: () => listCoupons(toValue(params)),
    placeholderData: (previous) => previous,
  })
}

export function useCoupon(publicId: MaybeRefOrGetter<string | null>) {
  return useQuery({
    queryKey: computed(() => ['coupons', 'detail', toValue(publicId)] as const),
    queryFn: () => getCoupon(toValue(publicId)!),
    enabled: computed(() => Boolean(toValue(publicId))),
  })
}

/**
 * 三個寫入動作都把整個 `coupons` 前綴失效。
 *
 * 只失效 `['coupons','list']` 會讓詳情表單留著舊的 `rowVersion`，使用者接著送出
 * 就會拿到一個其實是自己造成的 `concurrency_conflict`。
 */
export function useCreateCoupon() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateCouponRequest) => createCoupon(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['coupons'] }),
  })
}

export function useUpdateCoupon() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, request }: { publicId: string, request: UpdateCouponRequest }) =>
      updateCoupon(publicId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['coupons'] }),
  })
}

export function useCouponAction() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      publicId,
      action,
      request,
    }: { publicId: string, action: CouponAction, request: CouponActionRequest }) =>
      executeCouponAction(publicId, action, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['coupons'] }),
  })
}
