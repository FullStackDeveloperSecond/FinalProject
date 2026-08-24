export interface CurrentUserDto {
  publicId: string
  displayName: string
  emailMasked: string
  emailVerified: boolean
  locale: string
  roles: string[] | null
}

export interface AuthSessionDto {
  isAuthenticated: boolean
  user: CurrentUserDto | null
  expiresAtUtc: string | null
  requiresTwoFactor: boolean | null
}

export interface AdminLoginRequest {
  email: string
  password: string
}

export interface AdminLoginResponseDto {
  requiresTwoFactor: boolean
  requiresEnrollment: boolean
  twoFactorChallengePublicId: string
}

export interface TotpVerifyRequest {
  challengePublicId: string
  code: string
}

export interface RecoveryCodeUseRequest {
  challengePublicId: string
  code: string
}

export interface AdminAuthResultDto {
  user: CurrentUserDto
  expiresAtUtc: string
}

export interface TotpEnrollBeginResponseDto {
  secretKey: string
  otpAuthUri: string
  qrCodeDataUri: string
}

export interface TotpEnrollConfirmRequest {
  challengePublicId: string
  code: string
}

export interface TotpEnrollConfirmResponseDto {
  recoveryCodes: string[]
  user: CurrentUserDto
  expiresAtUtc: string
}

export interface TotpRebindConfirmRequest {
  code: string
}

interface ProblemDetails {
  code?: string
  detail?: string
  traceId?: string
  correlationId?: string
}

/**
 * 手寫 Paths（本專案沒有 OpenAPI codegen），對應
 * DoSelect.Api/Admin/Auth/AdminAuthController.cs 與 AdminAuthDtos.cs。
 */
export interface AdminAuthPaths {
  '/api/v1/admin/auth/session': {
    get: {
      responses: {
        200: { content: { 'application/json': AuthSessionDto } }
      }
    }
  }
  '/api/v1/admin/auth/login': {
    post: {
      requestBody: { content: { 'application/json': AdminLoginRequest } }
      responses: {
        200: { content: { 'application/json': AdminLoginResponseDto } }
        400: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/auth/logout': {
    post: {
      responses: {
        204: { content: never }
      }
    }
  }
  '/api/v1/admin/auth/totp/verify': {
    post: {
      requestBody: { content: { 'application/json': TotpVerifyRequest } }
      responses: {
        200: { content: { 'application/json': AdminAuthResultDto } }
        400: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/auth/recovery-codes/use': {
    post: {
      requestBody: { content: { 'application/json': RecoveryCodeUseRequest } }
      responses: {
        200: { content: { 'application/json': AdminAuthResultDto } }
        400: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/auth/totp/enroll/begin': {
    post: {
      parameters: { query: { challengePublicId: string } }
      responses: {
        200: { content: { 'application/json': TotpEnrollBeginResponseDto } }
        400: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/auth/totp/enroll/confirm': {
    post: {
      requestBody: { content: { 'application/json': TotpEnrollConfirmRequest } }
      responses: {
        200: { content: { 'application/json': TotpEnrollConfirmResponseDto } }
        400: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/auth/totp/rebind/begin': {
    post: {
      responses: {
        200: { content: { 'application/json': TotpEnrollBeginResponseDto } }
        401: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/auth/totp/rebind/confirm': {
    post: {
      requestBody: { content: { 'application/json': TotpRebindConfirmRequest } }
      responses: {
        200: { content: { 'application/json': TotpEnrollConfirmResponseDto } }
        400: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
}
