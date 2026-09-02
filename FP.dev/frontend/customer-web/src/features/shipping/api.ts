import { apiClient } from '../../api/client'
import type { ConvenienceStorePageDto, ShippingOptionsDto } from './types'

/**
 * The shipping-options endpoint resolves the same cart identity as every other cart call
 * (member cookie, else the X-DoSelect-Guest-Cart-Key header), so a guest request without the
 * header is a 400 — not an empty option list.
 */
function guestHeaders(guestCartKey?: string): HeadersInit | undefined {
  return guestCartKey ? { 'X-DoSelect-Guest-Cart-Key': guestCartKey } : undefined
}

export async function getShippingOptions(
  guestCartKey?: string,
  couponCode?: string,
): Promise<ShippingOptionsDto> {
  const { data } = await apiClient.GET('/api/v1/cart/shipping-options', {
    headers: guestHeaders(guestCartKey),
    params: { query: { couponCode } },
  })
  return data!
}

export interface ConvenienceStoreSearchParams {
  providerCode?: string
  city?: string
  district?: string
  q?: string
  pageNumber?: number
  pageSize?: number
}

export async function searchConvenienceStores(
  params: ConvenienceStoreSearchParams,
): Promise<ConvenienceStorePageDto> {
  const { data } = await apiClient.GET('/api/v1/convenience-stores', {
    params: {
      query: {
        ProviderCode: params.providerCode || undefined,
        City: params.city || undefined,
        District: params.district || undefined,
        Q: params.q || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}
