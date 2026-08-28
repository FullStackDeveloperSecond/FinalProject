import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
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
})
