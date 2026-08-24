// Dev-only stand-in for real member sign-in (haru's SH-05 work). Talks to the
// Development-only /api/v1/dev/test-sign-in endpoint, which only ever creates/authenticates
// @doselect.local test accounts. Nothing here ships to a real login experience — throw this
// store and its endpoint away once real auth exists.
import { defineStore } from 'pinia'
import { apiBaseUrl } from '../api/client'

const storageKey = 'doselect.dev-session'

interface DevSessionState {
  email: string | null
  memberUserId: string | null
  error: string | null
  isSigningIn: boolean
}

export const useDevSessionStore = defineStore('devSession', {
  state: (): DevSessionState => ({
    email: localStorage.getItem(storageKey),
    memberUserId: null,
    error: null,
    isSigningIn: false,
  }),
  getters: {
    isSignedIn: (state) => Boolean(state.email),
  },
  actions: {
    async signIn(email: string) {
      this.isSigningIn = true
      this.error = null
      try {
        const response = await fetch(`${apiBaseUrl}/api/v1/dev/test-sign-in`, {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email }),
        })

        if (!response.ok) {
          const body: unknown = await response.json().catch(() => null)
          const message = body && typeof body === 'object' && 'message' in body
            ? String((body as { message: unknown }).message)
            : `登入失敗（${response.status}）`
          throw new Error(message)
        }

        const result = (await response.json()) as { memberUserId: string, email: string }
        this.email = result.email
        this.memberUserId = result.memberUserId
        localStorage.setItem(storageKey, result.email)
      } catch (error) {
        this.error = error instanceof Error ? error.message : '登入失敗'
        throw error
      } finally {
        this.isSigningIn = false
      }
    },
    async signOut() {
      await fetch(`${apiBaseUrl}/api/v1/dev/test-sign-out`, {
        method: 'POST',
        credentials: 'include',
      }).catch(() => undefined)
      this.email = null
      this.memberUserId = null
      localStorage.removeItem(storageKey)
    },
  },
})
