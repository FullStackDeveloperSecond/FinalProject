import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { resetAntiforgeryToken } from '../api/client'
import {
  fetchSession,
  loginMember,
  logoutMember,
  type AuthSessionDto,
  type CurrentUserDto,
  type LoginRequestBody,
} from '../features/auth/api'

/**
 * 組長 PR #29 round 7 review (P1): 'anonymous' used to mean two different things —
 * "Session API confirmed there is no session" AND "the Session API call itself failed (network
 * error, 500, ...), so we actually have no idea". Cart's own identity gate (useCart.ts) only ever
 * checked `status !== 'loading'`, so a failed refresh() looked exactly like a confirmed guest: if
 * the browser still held a valid member Cookie, the Cart request would carry it and the backend
 * would act on (and return) the real member Cart, which this frontend would then write straight
 * into the *guest* query-cache key — a real member-data leak into whatever the next guest on a
 * shared device sees. 'error' is a distinct third "not resolved" state alongside 'loading': every
 * cart-identity gate below now fails closed on *both* until the Session API actually, successfully
 * confirms one side or the other.
 */
export type SessionStatus = 'loading' | 'authenticated' | 'anonymous' | 'error'

export const useSessionStore = defineStore('session', () => {
  const status = ref<SessionStatus>('loading')
  const user = ref<CurrentUserDto | undefined>(undefined)

  const isAuthenticated = computed(() => status.value === 'authenticated')
  // Identity gate for anything (like Cart) that must never act under a *guessed* identity —
  // true only once the Session API has actually, successfully told us which one it is.
  const isIdentityConfirmed = computed(() => status.value === 'authenticated' || status.value === 'anonymous')

  function applySession(session: AuthSessionDto): void {
    if (session.isAuthenticated && session.user) {
      status.value = 'authenticated'
      user.value = session.user
    } else {
      status.value = 'anonymous'
      user.value = undefined
    }
  }

  async function refresh(): Promise<void> {
    try {
      applySession(await fetchSession())
    } catch {
      // Fail closed, not fail open: a network/5xx failure here tells us NOTHING about whether the
      // browser is actually signed in — it must not be treated as a confirmed 'anonymous'.
      status.value = 'error'
      user.value = undefined
    }
  }

  async function login(request: LoginRequestBody): Promise<void> {
    const session = await loginMember(request)
    resetAntiforgeryToken()
    applySession(session)
  }

  async function logout(): Promise<void> {
    await logoutMember()
    resetAntiforgeryToken()
    status.value = 'anonymous'
    user.value = undefined
  }

  return { status, user, isAuthenticated, isIdentityConfirmed, refresh, login, logout }
})
