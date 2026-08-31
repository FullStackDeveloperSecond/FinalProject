import { createApiClient } from '../../api/client'

export interface GuestOrderAccessRequestBody {
  orderNumber: string
  email: string
}

export interface GuestOrderAccessAcceptedResponseBody {
  requestPublicId: string
  expiresAtUtc: string
  resendAvailableAtUtc: string
}

export interface GuestOrderAccessVerificationRequestBody {
  requestPublicId: string
  code: string
}

export interface GuestOrderAccessVerifiedResponseBody {
  orderPublicId: string
  expiresAtUtc: string
}

interface GuestOrdersPaths {
  '/api/v1/guest-orders/access-requests': {
    post: {
      requestBody: { content: { 'application/json': GuestOrderAccessRequestBody } }
      responses: {
        202: { content: { 'application/json': GuestOrderAccessAcceptedResponseBody } }
      }
    }
  }
  '/api/v1/guest-orders/access-requests/{requestPublicId}/actions/resend': {
    post: {
      parameters: { path: { requestPublicId: string } }
      responses: {
        202: { content: { 'application/json': GuestOrderAccessAcceptedResponseBody } }
      }
    }
  }
  '/api/v1/guest-orders/access-verifications': {
    post: {
      requestBody: { content: { 'application/json': GuestOrderAccessVerificationRequestBody } }
      responses: {
        200: { content: { 'application/json': GuestOrderAccessVerifiedResponseBody } }
      }
    }
  }
}

const client = createApiClient<GuestOrdersPaths>()

/**
 * 恆定回應成功（202）——不論訂單編號與 Email 是否存在，兩者都建立一筆 Challenge 並寄出等效的
 * 驗證信，藉此不讓呼叫端能推斷訂單是否存在（GuestOrderAccessUseCase.RequestAccessAsync）。
 */
export async function requestGuestOrderAccess(
  body: GuestOrderAccessRequestBody,
): Promise<GuestOrderAccessAcceptedResponseBody> {
  const { data } = await client.POST('/api/v1/guest-orders/access-requests', { body })
  return data as GuestOrderAccessAcceptedResponseBody
}

export async function resendGuestOrderAccess(
  requestPublicId: string,
): Promise<GuestOrderAccessAcceptedResponseBody> {
  const { data } = await client.POST(
    '/api/v1/guest-orders/access-requests/{requestPublicId}/actions/resend',
    { params: { path: { requestPublicId } } },
  )
  return data as GuestOrderAccessAcceptedResponseBody
}

export async function verifyGuestOrderAccess(
  body: GuestOrderAccessVerificationRequestBody,
): Promise<GuestOrderAccessVerifiedResponseBody> {
  const { data } = await client.POST('/api/v1/guest-orders/access-verifications', { body })
  return data as GuestOrderAccessVerifiedResponseBody
}
