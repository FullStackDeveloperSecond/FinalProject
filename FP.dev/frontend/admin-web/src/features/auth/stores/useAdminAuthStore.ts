import { defineStore } from 'pinia'
import { isApiError } from '@doselect/web-shared/api'
import { createApiClient, resetAntiforgeryToken } from '../../../api/client'
import router from '../../../router'
import { resolveSafeRedirect } from '../../../router/safeRedirect'
import type {
  AdminAuthPaths,
  AuthSessionDto,
  CurrentUserDto,
  TotpEnrollBeginResponseDto,
  TotpRebindBeginResponseDto,
} from '../api/paths'

type ChallengeKind = 'totp' | 'enroll'

interface Challenge {
  kind: ChallengeKind
  publicId: string
}

interface AdminAuthState {
  session: AuthSessionDto | null
  challenge: Challenge | null
  // Rebind 是獨立於登入流程的短效 Challenge（DEC-P297）：BeginRebind 簽發、ConfirmRebind
  // 消耗，跟登入用的 `challenge` 不共用同一個欄位，避免兩個流程的狀態互相污染。
  rebindChallengePublicId: string | null
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
    case 'admin_challenge_rate_limited':
      return '嘗試次數過多，請重新登入。'
    case 'admin_rebind_step_up_required':
      return '請輸入目前的驗證碼或一組備援碼以確認身分。'
    default:
      return '發生錯誤，請稍後再試。'
  }
}

export const useAdminAuthStore = defineStore('adminAuth', {
  state: (): AdminAuthState => ({
    session: null,
    challenge: null,
    rebindChallengePublicId: null,
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
        // ⚠ alex review：共用 client 的 middleware 對非 2xx 一律 throw（見
        // frontend/shared/src/api/client.ts 的 onResponse），openapi-fetch 回傳的
        // `error` 欄位實際上永遠不會有值——這裡就算解構出來也是死碼，統一改在下面的
        // catch 依 ApiError.code 處理，不要再寫一次一定進不去的 `if (error)` 分支。
        const { data } = await client().POST('/api/v1/admin/auth/login', {
          body: { email, password },
        })
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
        const { data } = await client().POST('/api/v1/admin/auth/totp/verify', {
          body: { challengePublicId: this.challenge.publicId, code },
        })
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
        // ⚠ alex review：共用 client 對非 2xx 一律 throw，所以這個 catch 才是真正會被
        // 執行到的分支——challenge 的清除邏輯必須放在這裡，放在上面解構出的 `error` 裡
        // 是永遠進不去的死碼（之前的修正因此完全沒有生效）。後端已經讓 challenge 失效
        // （簽出 AdminChallenge Cookie）；清掉本地的 challenge，讓 router guard 的
        // requiresChallenge 檢查把使用者導回登入頁，而不是留著一個已經死掉的 challenge
        // 讓頁面看起來還能用。admin_challenge_invalid（過期／SecurityStamp 不符）跟
        // admin_challenge_rate_limited 都代表同一件事——這個 challenge 已經死了；純粹
        // 驗證碼輸錯（admin_two_factor_invalid）則不會讓 challenge 失效，維持可以重試。
        if (isApiError(caught) &&
          (caught.code === 'admin_challenge_rate_limited' || caught.code === 'admin_challenge_invalid')) {
          this.challenge = null
        }
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
        const { data } = await client().POST('/api/v1/admin/auth/recovery-codes/use', {
          body: { challengePublicId: this.challenge.publicId, code },
        })
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
        // 跟 verifyTotp 一致：admin_challenge_invalid（過期／SecurityStamp 不符）也代表
        // 這個 challenge 已經死了，不能只處理 rate_limited；清除邏輯必須放在 catch，放在
        // 解構出的 `error` 裡是永遠進不去的死碼（alex review）。
        if (isApiError(caught) &&
          (caught.code === 'admin_challenge_rate_limited' || caught.code === 'admin_challenge_invalid')) {
          this.challenge = null
        }
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
        const { data } = await client().POST('/api/v1/admin/auth/totp/enroll/begin', {
          params: { query: { challengePublicId: this.challenge.publicId } },
        })
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
        const { data } = await client().POST('/api/v1/admin/auth/totp/enroll/confirm', {
          body: { challengePublicId: this.challenge.publicId, code },
        })
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
        // 跟 verifyTotp／confirmRebind 一致：admin_challenge_invalid 也代表這個 challenge
        // 已經死了，不能只處理 rate_limited；清除邏輯必須放在 catch（alex review）。
        if (isApiError(caught) &&
          (caught.code === 'admin_challenge_rate_limited' || caught.code === 'admin_challenge_invalid')) {
          this.challenge = null
        }
        return null
      } finally {
        this.loading = false
      }
    },

    /// ⚠ 讓已登入管理員重新綁定 TOTP（例如換手機）。跟 beginEnrollment/confirmEnrollment
    /// 不同，這裡的呼叫者已經是完整登入狀態；但 DEC-P297 仍要求一組短效、單次、綁定使用者
    /// 的 Challenge，跟後端 AdminChallenge Cookie 配對，記在 rebindChallengePublicId
    /// （alex review P1#3）。
    // ⚠ alex review 裁定 A1：只有既有 Admin Cookie 不足以簽發 rebind challenge——必須先證明
    // 目前仍握有一組有效的 TOTP 驗證碼或 Recovery Code，兩者恰好擇一。
    async beginRebind(stepUp: { totpCode: string } | { recoveryCode: string }): Promise<TotpRebindBeginResponseDto | null> {
      this.loading = true
      this.errorMessage = null
      try {
        const { data } = await client().POST('/api/v1/admin/auth/totp/rebind/begin', {
          body: 'totpCode' in stepUp
            ? { totpCode: stepUp.totpCode, recoveryCode: null }
            : { totpCode: null, recoveryCode: stepUp.recoveryCode },
        })
        if (data) {
          this.rebindChallengePublicId = data.challengePublicId
        }
        return data ?? null
      } catch (caught) {
        this.errorMessage = isApiError(caught) ? messageForCode(caught.code) : '無法連線到伺服器。'
        return null
      } finally {
        this.loading = false
      }
    },

    async confirmRebind(code: string): Promise<string[] | null> {
      if (!this.rebindChallengePublicId) {
        return null
      }
      this.loading = true
      this.errorMessage = null
      try {
        const { data } = await client().POST('/api/v1/admin/auth/totp/rebind/confirm', {
          body: { challengePublicId: this.rebindChallengePublicId, code },
        })
        if (data) {
          this.session = { isAuthenticated: true, user: data.user, expiresAtUtc: data.expiresAtUtc, requiresTwoFactor: null }
          this.rebindChallengePublicId = null
          // 完成後這個請求所在的 Session 會用新的 SecurityStamp 重新簽發，
          // 其他既有裝置的 Session 全部失效。保守起見一併重抓 antiforgery token。
          resetAntiforgeryToken()
          return data.recoveryCodes
        }
        return null
      } catch (caught) {
        this.errorMessage = isApiError(caught) ? messageForCode(caught.code) : '無法連線到伺服器。'
        // 後端 ConfirmRebind 不論成功或失敗，這張單次 rebind challenge 都會被簽出／作廢
        // （見 AdminAuthController.ConfirmRebind：任何失敗分支都會 SignOutAsync
        // AdminChallenge），所以任何 ApiError（包含輸錯驗證碼的 admin_two_factor_invalid，
        // 不只 admin_challenge_invalid／admin_challenge_rate_limited）都代表這組
        // challengePublicId 已經死了。清掉本地狀態，逼使用者回到「開始重新綁定」重新拿
        // 一組新的 challenge，而不是讓表單卡在一個重送也不會成功的舊 ID 上（alex review：
        // 清除邏輯必須放在 catch，且要涵蓋 admin_two_factor_invalid）。
        if (isApiError(caught)) {
          this.rebindChallengePublicId = null
        }
        return null
      } finally {
        this.loading = false
      }
    },

    async logout(): Promise<void> {
      await client().POST('/api/v1/admin/auth/logout')
      this.session = { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
      this.challenge = null
      this.rebindChallengePublicId = null
      resetAntiforgeryToken()
    },

    /// ⚠ alex review 第三輪 P2#4：受保護頁面的 API 呼叫回 401（Session 已過期／被撤銷）時，
    /// 由共用 client 的 onApiError 呼叫這裡——清掉本地快取的 Session／challenge 狀態（避免
    /// 畫面顯示「已登入」卻其實每個請求都會再次 401），導向登入頁並保留目前所在的站內路徑，
    /// 讓登入／MFA 完成後可以導回原本要去的頁面。redirect 的值只在消費點（各 auth 頁面的
    /// 導覽目標）才需要驗證是否為安全的站內路徑，這裡只是原樣存進 query，來源是 Vue Router
    /// 自己的 currentRoute.fullPath，不是使用者輸入。
    async handleSessionExpired(currentPath: string): Promise<void> {
      this.session = { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
      this.challenge = null
      this.rebindChallengePublicId = null
      resetAntiforgeryToken()
      await router.push({ name: 'login', query: { redirect: resolveSafeRedirect(currentPath) } })
    },
  },
})
