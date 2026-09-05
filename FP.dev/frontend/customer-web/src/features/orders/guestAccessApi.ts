import type { components } from '@doselect/web-shared/api'
import { apiClient } from '../../api/client'

export type GuestOrderAccessRequest = components['schemas']['GuestOrderAccessRequestDto']
export type GuestOrderAccessAccepted = components['schemas']['GuestOrderAccessRequestAcceptedDto']
export type GuestOrderAccessVerification = components['schemas']['GuestOrderAccessVerificationDto']
export type GuestOrderAccessVerified = components['schemas']['GuestOrderAccessVerifiedDto']

export async function requestGuestOrderAccess(
  body: GuestOrderAccessRequest,
): Promise<GuestOrderAccessAccepted> {
  const { data } = await apiClient.POST('/api/v1/guest-orders/access-requests', { body })
  return data!
}

export async function resendGuestOrderAccess(
  requestPublicId: string,
): Promise<GuestOrderAccessAccepted> {
  const { data } = await apiClient.POST(
    '/api/v1/guest-orders/access-requests/{requestPublicId}/actions/resend',
    { params: { path: { requestPublicId } } },
  )
  return data!
}

export async function verifyGuestOrderAccess(
  body: GuestOrderAccessVerification,
): Promise<GuestOrderAccessVerified> {
  const { data } = await apiClient.POST('/api/v1/guest-orders/access-verifications', { body })
  return data!
}
