import { createApiClient } from '../../api/client'

// Mirrors DoSelect.Application.Members.RegisterMemberService.CurrentTermsVersion until a
// terms-of-service version registry exists.
export const CURRENT_TERMS_VERSION = 1

export interface RegisterRequestBody {
  email: string
  password: string
  displayName: string
  acceptTermsVersion: number
}

export interface RegisterAcceptedResponseBody {
  publicId: string
  emailMasked: string
  accountStatus: string
}

export interface EmailVerificationConfirmRequestBody {
  userPublicId: string
  token: string
}

export interface EmailVerificationConfirmedResponseBody {
  accountStatus: string
}

interface AuthPaths {
  '/api/v1/auth/register': {
    post: {
      requestBody: { content: { 'application/json': RegisterRequestBody } }
      responses: {
        202: { content: { 'application/json': RegisterAcceptedResponseBody } }
      }
    }
  }
  '/api/v1/auth/email-verifications/confirm': {
    post: {
      requestBody: { content: { 'application/json': EmailVerificationConfirmRequestBody } }
      responses: {
        200: { content: { 'application/json': EmailVerificationConfirmedResponseBody } }
      }
    }
  }
}

const client = createApiClient<AuthPaths>()

export async function registerMember(
  body: RegisterRequestBody,
): Promise<RegisterAcceptedResponseBody> {
  const { data } = await client.POST('/api/v1/auth/register', { body })
  return data as RegisterAcceptedResponseBody
}

export async function confirmEmailVerification(
  body: EmailVerificationConfirmRequestBody,
): Promise<EmailVerificationConfirmedResponseBody> {
  const { data } = await client.POST('/api/v1/auth/email-verifications/confirm', { body })
  return data as EmailVerificationConfirmedResponseBody
}
