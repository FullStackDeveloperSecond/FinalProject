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

export const useSessionStore = defineStore('session', () => {
  const status = ref<'loading' | 'authenticated' | 'anonymous'>('loading')
  const user = ref<CurrentUserDto | undefined>(undefined)

  const isAuthenticated = computed(() => status.value === 'authenticated')

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
      status.value = 'anonymous'
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

  return { status, user, isAuthenticated, refresh, login, logout }
})
