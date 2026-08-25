import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import router from './index'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

describe('admin router foundation', () => {
  it.each([
    ['/support', 'support-sla-queue'],
    ['/support/tickets/018f2e6a-0000-7000-8000-000000000001', 'support-ticket-detail'],
  ])('registers the admin support route %s', (path, name) => {
    const resolved = router.resolve(path)

    expect(resolved.name).toBe(name)
    expect(resolved.matched).toHaveLength(1)
  })

  it('catches unknown routes', async () => {
    await router.push('/missing-page')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('not-found')
  })
})

describe('admin router challenge guard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it.each([
    ['/login/verify', 'login-verify'],
    ['/login/enroll', 'login-enroll'],
  ])(
    'redirects %s back to /login when the MFA challenge is missing (e.g. after a hard reload)',
    async (path) => {
      const auth = useAdminAuthStore()
      // 直接設定 session，避免 guard 內的 fetchSession() 真的打網路——這裡只測試
      // requiresChallenge 這一段邏輯，跟 session 抓取無關。
      auth.session = { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
      auth.challenge = null

      await router.push(path)
      await router.isReady()

      expect(router.currentRoute.value.name).toBe('login')
    },
  )

  it.each([
    ['/login/verify', 'login-verify'],
    ['/login/enroll', 'login-enroll'],
  ])('allows %s through when a matching MFA challenge is pending', async (path, expectedName) => {
    const auth = useAdminAuthStore()
    auth.session = { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
    auth.challenge = { kind: expectedName === 'login-enroll' ? 'enroll' : 'totp', publicId: 'challenge-1' }

    await router.push(path)
    await router.isReady()

    expect(router.currentRoute.value.name).toBe(expectedName)
  })
})
