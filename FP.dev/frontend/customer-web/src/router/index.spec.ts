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
})
