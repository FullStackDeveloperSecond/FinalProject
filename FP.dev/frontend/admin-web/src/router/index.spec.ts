import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import router from './index'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

describe('admin router foundation', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    const auth = useAdminAuthStore()
    auth.session = {
      isAuthenticated: true,
      user: {
        publicId: 'admin-1',
        displayName: 'Admin',
        emailMasked: 'a***@example.test',
        emailVerified: true,
        locale: 'zh-TW',
        roles: ['SuperAdmin'],
      },
      expiresAtUtc: null,
      requiresTwoFactor: false,
    }
  })

  it.each([
    ['/support', 'support-sla-queue'],
    ['/support/tickets/018f2e6a-0000-7000-8000-000000000001', 'support-ticket-detail'],
    ['/cases', 'case-workbench'],
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

  /** PR #24 review: confirmed Route contract (M功能桌面UI與Route規格.md A-06) is /admin/products/:productId, not /admin/products/:id/edit. */
  it('resolves /products/:productId to product-edit with the id as the productId prop', async () => {
    await router.push('/products/p1')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('product-edit')
    expect(router.currentRoute.value.params.productId).toBe('p1')
  })

  /** /products/new must still resolve to the create route, not be swallowed by the dynamic :productId segment. */
  it('resolves /products/new to product-new, not product-edit', async () => {
    await router.push('/products/new')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('product-new')
  })

  /** PR #24 review: confirmed Route contract (M功能桌面UI與Route規格.md A-08) is one combined /admin/catalog/lookups page, not separate /brands, /categories, /tags routes. */
  it('resolves /catalog/lookups to the combined lookups page', async () => {
    await router.push('/catalog/lookups')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('catalog-lookups')
  })

  it('no longer has standalone /brands, /categories, or /tags routes', async () => {
    await router.push('/brands')
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('not-found')

    await router.push('/categories')
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('not-found')

    await router.push('/tags')
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('not-found')
  })
})

describe('admin router role guard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('redirects an authenticated administrator without the required role to forbidden', async () => {
    const auth = useAdminAuthStore()
    auth.session = {
      isAuthenticated: true,
      user: {
        publicId: 'admin-2',
        displayName: 'Support',
        emailMasked: 's***@example.test',
        emailVerified: true,
        locale: 'zh-TW',
        roles: ['CustomerService'],
      },
      expiresAtUtc: null,
      requiresTwoFactor: false,
    }

    await router.push('/products')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('forbidden')
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

  // alex review 第三輪 P2#4：深層連結（?redirect=...）在半路遺失 challenge、被彈回登入頁
  // 重新開始時，這個值不能跟著弄丟——完成登入後才有機會導回原本要去的頁面。
  it('preserves the redirect query param when bouncing back to /login for a missing challenge', async () => {
    const auth = useAdminAuthStore()
    auth.session = { isAuthenticated: false, user: null, expiresAtUtc: null, requiresTwoFactor: null }
    auth.challenge = null

    await router.push('/login/verify?redirect=%2Fproducts%2F123')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/products/123')
  })

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
