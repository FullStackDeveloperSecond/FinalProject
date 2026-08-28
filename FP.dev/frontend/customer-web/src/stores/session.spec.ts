import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionStore } from './session'

const mockFetchSession = vi.fn()

vi.mock('../features/auth/api', () => ({
  fetchSession: () => mockFetchSession(),
  loginMember: vi.fn(),
  logoutMember: vi.fn(),
}))

vi.mock('../api/client', () => ({
  resetAntiforgeryToken: vi.fn(),
}))

beforeEach(() => {
  setActivePinia(createPinia())
  mockFetchSession.mockReset()
})

const memberUser = {
  publicId: 'member-1', displayName: '測試會員', emailMasked: 'm***@example.com', emailVerified: true, locale: 'zh-TW',
}

/**
 * 組長 PR #29 round 7 review, P1: refresh() used to collapse every failure (network error, 500,
 * ...) into a confirmed-looking 'anonymous' — indistinguishable from a genuine "the Session API
 * confirmed there is no session". If the browser still held a valid member Cookie, every
 * cart-identity gate downstream (useCart.ts) would treat this exactly like a real guest and act
 * under the wrong identity. 'error' is the fix: a distinct third "not resolved" state that fails
 * closed instead of fails open.
 */
describe('useSessionStore', () => {
  it('resolves to authenticated with the user on a successful session fetch', async () => {
    mockFetchSession.mockResolvedValue({ isAuthenticated: true, user: memberUser })
    const store = useSessionStore()

    await store.refresh()

    expect(store.status).toBe('authenticated')
    expect(store.user).toEqual(memberUser)
    expect(store.isAuthenticated).toBe(true)
    expect(store.isIdentityConfirmed).toBe(true)
  })

  it('resolves to anonymous when the session API confirms there is no session', async () => {
    mockFetchSession.mockResolvedValue({ isAuthenticated: false, user: null })
    const store = useSessionStore()

    await store.refresh()

    expect(store.status).toBe('anonymous')
    expect(store.user).toBeUndefined()
    expect(store.isIdentityConfirmed).toBe(true)
  })

  it('resolves to \'error\', not \'anonymous\', when the session fetch itself fails', async () => {
    mockFetchSession.mockRejectedValue(new Error('network error'))
    const store = useSessionStore()

    await store.refresh()

    expect(store.status).toBe('error')
    expect(store.status).not.toBe('anonymous')
    expect(store.user).toBeUndefined()
    expect(store.isAuthenticated).toBe(false)
    // The whole point of the distinct 'error' state: identity-sensitive gates (Cart) must treat
    // this the same as still-unresolved, never as a confirmed guest.
    expect(store.isIdentityConfirmed).toBe(false)
  })

  it('treats \'loading\' (the initial state, before any refresh completes) as not identity-confirmed', () => {
    const store = useSessionStore()

    expect(store.status).toBe('loading')
    expect(store.isIdentityConfirmed).toBe(false)
  })

  it('recovers to a confirmed state on a later successful refresh after a failed one', async () => {
    mockFetchSession.mockRejectedValueOnce(new Error('network error'))
    const store = useSessionStore()
    await store.refresh()
    expect(store.status).toBe('error')

    mockFetchSession.mockResolvedValueOnce({ isAuthenticated: true, user: memberUser })
    await store.refresh()

    expect(store.status).toBe('authenticated')
    expect(store.isIdentityConfirmed).toBe(true)
  })
})
