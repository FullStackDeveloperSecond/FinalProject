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

export interface EmailVerificationRequestBody {
  email: string
}

export interface EmailVerificationConfirmRequestBody {
  userPublicId: string
  token: string
}

export interface EmailVerificationConfirmedResponseBody {
  accountStatus: string
}

export interface PasswordResetRequestBody {
  email: string
}

export interface PasswordResetConfirmRequestBody {
  userPublicId: string
  token: string
  newPassword: string
}

export interface LoginRequestBody {
  email: string
  password: string
  rememberMe: boolean
}

export interface CurrentUserDto {
  publicId: string
  displayName: string
  emailMasked: string
  emailVerified: boolean
  locale: string
}

export interface AuthSessionDto {
  isAuthenticated: boolean
  user?: CurrentUserDto
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
  '/api/v1/auth/email-verifications': {
    post: {
      requestBody: { content: { 'application/json': EmailVerificationRequestBody } }
      responses: {
        202: { content: never }
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
  '/api/v1/auth/password-resets': {
    post: {
      requestBody: { content: { 'application/json': PasswordResetRequestBody } }
      responses: {
        202: { content: never }
      }
    }
  }
  '/api/v1/auth/password-resets/confirm': {
    post: {
      requestBody: { content: { 'application/json': PasswordResetConfirmRequestBody } }
      responses: {
        200: { content: never }
      }
    }
  }
  '/api/v1/auth/login': {
    post: {
      requestBody: { content: { 'application/json': LoginRequestBody } }
      responses: {
        200: { content: { 'application/json': AuthSessionDto } }
      }
    }
  }
  '/api/v1/auth/logout': {
    post: {
      responses: {
        204: { content: never }
      }
    }
  }
  '/api/v1/auth/session': {
    get: {
      responses: {
        200: { content: { 'application/json': AuthSessionDto } }
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

export async function requestEmailVerification(
  body: EmailVerificationRequestBody,
): Promise<void> {
  await client.POST('/api/v1/auth/email-verifications', { body })
}

export async function confirmEmailVerification(
  body: EmailVerificationConfirmRequestBody,
): Promise<EmailVerificationConfirmedResponseBody> {
  const { data } = await client.POST('/api/v1/auth/email-verifications/confirm', { body })
  return data as EmailVerificationConfirmedResponseBody
}

export async function requestPasswordReset(
  body: PasswordResetRequestBody,
): Promise<void> {
  await client.POST('/api/v1/auth/password-resets', { body })
}

export async function confirmPasswordReset(
  body: PasswordResetConfirmRequestBody,
): Promise<void> {
  await client.POST('/api/v1/auth/password-resets/confirm', { body })
}

export async function loginMember(body: LoginRequestBody): Promise<AuthSessionDto> {
  const { data } = await client.POST('/api/v1/auth/login', { body })
  return data as AuthSessionDto
}

export async function logoutMember(): Promise<void> {
  await client.POST('/api/v1/auth/logout')
}

export async function fetchSession(): Promise<AuthSessionDto> {
  const { data } = await client.GET('/api/v1/auth/session')
  return data as AuthSessionDto
}
