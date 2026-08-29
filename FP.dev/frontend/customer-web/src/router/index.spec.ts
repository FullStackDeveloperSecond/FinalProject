import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import router from './index'
import { useSessionStore } from '../stores/session'

describe('customer router authentication guard', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await router.push('/')
    await router.isReady()
  })

  it('redirects an anonymous visitor from support to login and preserves the destination', async () => {
    const session = useSessionStore()
    session.status = 'anonymous'

    await router.push('/support/tickets')

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/support/tickets')
  })

  it('allows an authenticated member to open support', async () => {
    const session = useSessionStore()
    session.status = 'authenticated'

    await router.push('/support/tickets')

    expect(router.currentRoute.value.name).toBe('support-ticket-list')
  })

  /**
   * 組長 PR #29 round 7 review, P1: a failed session refresh now resolves to the distinct 'error'
   * state instead of a confirmed-looking 'anonymous'. The guard retries once rather than bouncing
   * a member who may actually still be signed in — but still fails closed if that retry also fails.
   */
  it('retries the session refresh (rather than bouncing straight to login) when the session failed to resolve', async () => {
    const session = useSessionStore()
    session.status = 'error'
    session.refresh = vi.fn().mockImplementation(async () => { session.status = 'authenticated' })

    await router.push('/support/tickets')

    expect(session.refresh).toHaveBeenCalledTimes(1)
    expect(router.currentRoute.value.name).toBe('support-ticket-list')
  })

  it('still redirects to login when the retried session refresh also fails', async () => {
    const session = useSessionStore()
    session.status = 'error'
    session.refresh = vi.fn().mockImplementation(async () => { session.status = 'error' })

    await router.push('/support/tickets')

    // The guard runs again on the redirected /login navigation itself (it carries guestOnly meta),
    // so refresh() is attempted there too — that terminates rather than looping, because /login has
    // no requiresAuth to bounce off and guestOnly only redirects an *authenticated* visitor away.
    expect(session.refresh).toHaveBeenCalled()
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/support/tickets')
  })

  // 組長 PR #35 round-3 review, P2-3: 已儲存的組裝清單（清單本身與明細頁）是會員個人資源，之前完
  // 全沒有 requiresAuth，未登入也能直接打開。
  it('redirects an anonymous visitor from the build list to login and preserves the destination', async () => {
    const session = useSessionStore()
    session.status = 'anonymous'

    await router.push('/account/builds')

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/account/builds')
  })

  it('allows an authenticated member to open the build list', async () => {
    const session = useSessionStore()
    session.status = 'authenticated'

    await router.push('/account/builds')

    expect(router.currentRoute.value.name).toBe('build-lists')
  })

  it('redirects an anonymous visitor from a build detail page to login and preserves the destination', async () => {
    const session = useSessionStore()
    session.status = 'anonymous'

    await router.push('/builds/018f2e6a-0000-7000-8000-000000000001')

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/builds/018f2e6a-0000-7000-8000-000000000001')
  })

  it('allows an authenticated member to open a build detail page', async () => {
    const session = useSessionStore()
    session.status = 'authenticated'

    await router.push('/builds/018f2e6a-0000-7000-8000-000000000001')

    expect(router.currentRoute.value.name).toBe('build-detail')
  })

  // /builds/new (guest 草稿) 與 /builds/shared/:shareToken (刻意公開的唯讀連結) 必須維持不需要登入。
  it.each([
    ['/builds/new', 'build-new'],
    ['/builds/shared/some-token', 'build-shared'],
  ])('leaves %s open to an anonymous visitor', async (path, expectedName) => {
    const session = useSessionStore()
    session.status = 'anonymous'

    await router.push(path)

    expect(router.currentRoute.value.name).toBe(expectedName)
  })
})
