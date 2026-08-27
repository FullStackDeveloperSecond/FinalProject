import { ApiError } from '@doselect/web-shared/api'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockResetAntiforgeryToken = vi.fn()

// ⚠ alex review：共用 client 的 middleware 對非 2xx 一律 throw（見
// frontend/shared/src/api/client.ts 的 onResponse），openapi-fetch 回傳的 `{ data, error }`
// 裡的 `error` 實際上永遠不會有值。這裡用 mockRejectedValueOnce 模擬真正的執行期行為——
// 用 mockResolvedValueOnce({ error }) 會測出一個永遠不會發生的假象，正是先前的修正
// 「看起來對但實際不會生效」的原因。
vi.mock('../../../api/client', () => ({
  createApiClient: () => ({ GET: mockGet, POST: mockPost }),
  resetAntiforgeryToken: mockResetAntiforgeryToken,
}))

const { useAdminAuthStore } = await import('./useAdminAuthStore')

describe('useAdminAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockGet.mockReset()
    mockPost.mockReset()
    mockResetAntiforgeryToken.mockReset()
  })

  describe('verifyTotp', () => {
    it('keeps the challenge on a simple wrong code, so the user can retry', async () => {
      const auth = useAdminAuthStore()
      auth.challenge = { kind: 'totp', publicId: 'challenge-1' }
      mockPost.mockRejectedValueOnce(
        new ApiError('invalid', { status: 400, code: 'admin_two_factor_invalid' }),
      )

      const result = await auth.verifyTotp('000000')

      expect(result).toBe(false)
      expect(auth.challenge).not.toBeNull()
      expect(auth.errorMessage).toBe('驗證碼不正確，請重新輸入。')
    })

    it('clears the challenge when the backend reports admin_challenge_invalid (expired / SecurityStamp mismatch)', async () => {
      const auth = useAdminAuthStore()
      auth.challenge = { kind: 'totp', publicId: 'challenge-1' }
      mockPost.mockRejectedValueOnce(
        new ApiError('invalid', { status: 400, code: 'admin_challenge_invalid' }),
      )

      const result = await auth.verifyTotp('000000')

      expect(result).toBe(false)
      expect(auth.challenge).toBeNull()
    })

    it('clears the challenge when rate limited', async () => {
      const auth = useAdminAuthStore()
      auth.challenge = { kind: 'totp', publicId: 'challenge-1' }
      mockPost.mockRejectedValueOnce(
        new ApiError('limited', { status: 429, code: 'admin_challenge_rate_limited' }),
      )

      await auth.verifyTotp('000000')

      expect(auth.challenge).toBeNull()
    })

    it('completes sign-in and clears the challenge on success', async () => {
      const auth = useAdminAuthStore()
      auth.challenge = { kind: 'totp', publicId: 'challenge-1' }
      const user = { publicId: 'u1', displayName: 'Admin', emailMasked: 'a***@example.com', emailVerified: true, locale: 'zh-TW', roles: ['SuperAdmin'] }
      mockPost.mockResolvedValueOnce({ data: { user, expiresAtUtc: '2026-01-01T00:00:00Z' } })

      const result = await auth.verifyTotp('123456')

      expect(result).toBe(true)
      expect(auth.challenge).toBeNull()
      expect(auth.session?.isAuthenticated).toBe(true)
      expect(mockResetAntiforgeryToken).toHaveBeenCalled()
    })
  })

  describe('useRecoveryCode', () => {
    it('clears the challenge when the backend reports admin_challenge_invalid', async () => {
      const auth = useAdminAuthStore()
      auth.challenge = { kind: 'totp', publicId: 'challenge-1' }
      mockPost.mockRejectedValueOnce(
        new ApiError('invalid', { status: 400, code: 'admin_challenge_invalid' }),
      )

      await auth.useRecoveryCode('some-code')

      expect(auth.challenge).toBeNull()
    })
  })

  describe('confirmEnrollment', () => {
    it('clears the challenge when the backend reports admin_challenge_invalid', async () => {
      const auth = useAdminAuthStore()
      auth.challenge = { kind: 'enroll', publicId: 'challenge-1' }
      mockPost.mockRejectedValueOnce(
        new ApiError('invalid', { status: 400, code: 'admin_challenge_invalid' }),
      )

      await auth.confirmEnrollment('000000')

      expect(auth.challenge).toBeNull()
    })
  })

  describe('confirmRebind', () => {
    it('clears rebindChallengePublicId on a simple wrong code, because the backend invalidates the single-use challenge on any confirm failure', async () => {
      // 這是這輪修正的核心情境（alex review）：ConfirmRebind 後端在任何失敗分支都會
      // SignOutAsync AdminChallenge，讓這組 challenge 作廢——不只是 admin_challenge_invalid／
      // admin_challenge_rate_limited，連單純輸錯碼的 admin_two_factor_invalid 也一樣。
      const auth = useAdminAuthStore()
      auth.rebindChallengePublicId = 'rebind-challenge-1'
      mockPost.mockRejectedValueOnce(
        new ApiError('invalid', { status: 400, code: 'admin_two_factor_invalid' }),
      )

      const result = await auth.confirmRebind('000000')

      expect(result).toBeNull()
      expect(auth.rebindChallengePublicId).toBeNull()
    })

    it('does not clear rebindChallengePublicId on a network error (the request never reached the backend)', async () => {
      const auth = useAdminAuthStore()
      auth.rebindChallengePublicId = 'rebind-challenge-1'
      mockPost.mockRejectedValueOnce(new TypeError('Failed to fetch'))

      const result = await auth.confirmRebind('000000')

      expect(result).toBeNull()
      expect(auth.rebindChallengePublicId).toBe('rebind-challenge-1')
      expect(auth.errorMessage).toBe('無法連線到伺服器。')
    })
  })

  describe('beginRebind', () => {
    it('surfaces the rebind step-up rate-limit message without touching rebindChallengePublicId', async () => {
      const auth = useAdminAuthStore()
      mockPost.mockRejectedValueOnce(
        new ApiError('limited', { status: 429, code: 'admin_challenge_rate_limited' }),
      )

      const result = await auth.beginRebind({ totpCode: '000000' })

      expect(result).toBeNull()
      expect(auth.rebindChallengePublicId).toBeNull()
      expect(auth.errorMessage).toBe('嘗試次數過多，請重新登入。')
    })
  })
})
