import { defineStore } from 'pinia'
import { isApiError } from '@doselect/web-shared/api'
import { createApiClient, resetAntiforgeryToken } from '../../../api/client'
import type {
  AdminAuthPaths,
  AuthSessionDto,
  CurrentUserDto,
  TotpEnrollBeginResponseDto,
} from '../api/paths'

type ChallengeKind = 'totp' | 'enroll'

interface Challenge {
  kind: ChallengeKind
  publicId: string
}

interface AdminAuthState {
  session: AuthSessionDto | null
  challenge: Challenge | null
  loading: boolean
  errorMessage: string | null
}

function client() {
  return createApiClient<AdminAuthPaths>()
}

function messageForCode(code: string | undefined): string {
  switch (code) {
    case 'invalid_credentials':
      return '帳號或密碼錯誤。'
    case 'account_locked':
      return '登入失敗次數過多，帳號已鎖定，請稍後再試。'
    case 'account_suspended':
      return '此帳號已被停用。'
    case 'admin_two_factor_invalid':
      return '驗證碼不正確，請重新輸入。'
    case 'admin_recovery_code_invalid':
      return '備援碼無效或已使用過。'
    case 'admin_challenge_invalid':
      return '驗證流程已過期，請重新登入。'
    default:
      return '發生錯誤，請稍後再試。'
  }
}

export const useAdminAuthStore = defineStore('adminAuth', {
  state: (): AdminAuthState => ({
    session: null,
    challenge: null,
    loading: false,
    errorMessage: null,
  }),
  getters: {
    isAuthenticated: (state): boolean => state.session?.isAuthenticated ?? false,
    currentUser: (state): CurrentUserDto | null => state.session?.user ?? null,
  },
  actions: {
    async fetchSession(): Promise<void> {
      // 這個方法會在 router 的初始導覽（beforeEach）裡被呼叫；網路異常時絕對不能
      // 讓例外往外丟，否則會直接中斷 Vue Router 的第一次導覽，讓整個 App 卡死在
      // 半渲染狀態。任何失敗一律視為「未登入」，讓 guard 導去 /login。
      try {
        const { data } = await client().GET('/api/v1/admin/auth/session')
        this.session = data ?? { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
      } catch {
        this.session = { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
      }
    },

    async login(email: string, password: string): Promise<void> {
      this.loading = true
      this.errorMessage = null
      try {
        const { data, error } = await client().POST('/api/v1/admin/auth/login', {
          body: { email, password },
        })
        if (error) {
          this.errorMessage = messageForCode(error.code)
          return
        }
        if (data) {
          this.challenge = {
            kind: data.requiresEnrollment ? 'enroll' : 'totp',
            publicId: data.twoFactorChallengePublicId,
          }
          // 登入成功後身分從匿名變成 AdminChallenge，之前快取的 antiforgery token
          // 是綁定匿名身分產生的，沿用會被 GlobalAntiforgeryFilter 拒絕，必須重抓。
          resetAntiforgeryToken()
        }
      } catch (caught) {
        this.errorMessage = isApiError(caught) ? messageForCode(caught.code) : '無法連線到伺服器。'
      } finally {
        this.loading = false
      }
    },

    async verifyTotp(code: string): Promise<boolean> {
      if (!this.challenge) {
        return false
      }
      this.loading = true
      this.errorMessage = null
      try {
        const { data, error } = await client().POST('/api/v1/admin/auth/totp/verify', {
          body: { challengePublicId: this.challenge.publicId, code },
        })
        if (error) {
          this.errorMessage = messageForCode(error.code)
          return false
        }
        if (data) {
          this.session = { isAuthenticated: true, user: data.user, expiresAtUtc: data.expiresAtUtc, requiresTwoFactor: null }
          this.challenge = null
          // 身分從 AdminChallenge 變成完整 Admin，同理必須重抓 antiforgery token。
          resetAntiforgeryToken()
          return true
        }
        return false
      } catch (caught) {
        this.errorMessage = isApiError(caught) ? messageForCode(caught.code) : '無法連線到伺服器。'
        return false
      } finally {
        this.loading = false
      }
    },

    async useRecoveryCode(code: string): Promise<boolean> {
      if (!this.challenge) {
        return false
      }
      this.loading = true
      this.errorMessage = null
      try {
        const { data, error } = await client().POST('/api/v1/admin/auth/recovery-codes/use', {
          body: { challengePublicId: this.challenge.publicId, code },
        })
        if (error) {
          this.errorMessage = messageForCode(error.code)
          return false
        }
        if (data) {
          this.session = { isAuthenticated: true, user: data.user, expiresAtUtc: data.expiresAtUtc, requiresTwoFactor: null }
          this.challenge = null
          // 身分從 AdminChallenge 變成完整 Admin，同理必須重抓 antiforgery token。
          resetAntiforgeryToken()
          return true
        }
        return false
      } catch (caught) {
        this.errorMessage = isApiError(caught) ? messageForCode(caught.code) : '無法連線到伺服器。'
        return false
      } finally {
        this.loading = false
      }
    },

    async beginEnrollment(): Promise<TotpEnrollBeginResponseDto | null> {
      if (!this.challenge) {
        return null
      }
      this.loading = true
      this.errorMessage = null
      try {
        const { data, error } = await client().POST('/api/v1/admin/auth/totp/enroll/begin', {
          params: { query: { challengePublicId: this.challenge.publicId } },
        })
        if (error) {
          this.errorMessage = messageForCode(error.code)
          return null
        }
        return data ?? null
      } catch (caught) {
        this.errorMessage = isApiError(caught) ? messageForCode(caught.code) : '無法連線到伺服器。'
        return null
      } finally {
        this.loading = false
      }
    },

    async confirmEnrollment(code: string): Promise<string[] | null> {
      if (!this.challenge) {
        return null
      }
      this.loading = true
      this.errorMessage = null
      try {
        const { data, error } = await client().POST('/api/v1/admin/auth/totp/enroll/confirm', {
          body: { challengePublicId: this.challenge.publicId, code },
        })
        if (error) {
          this.errorMessage = messageForCode(error.code)
          return null
        }
        if (data) {
          this.session = { isAuthenticated: true, user: data.user, expiresAtUtc: data.expiresAtUtc, requiresTwoFactor: null }
          this.challenge = null
          // 身分從 AdminChallenge 變成完整 Admin，同理必須重抓 antiforgery token。
          resetAntiforgeryToken()
          return data.recoveryCodes
        }
        return null
      } catch (caught) {
        this.errorMessage = isApiError(caught) ? messageForCode(caught.code) : '無法連線到伺服器。'
        return null
      } finally {
        this.loading = false
      }
    },

    async logout(): Promise<void> {
      await client().POST('/api/v1/admin/auth/logout')
      this.session = { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
      this.challenge = null
      resetAntiforgeryToken()
    },
  },
})
