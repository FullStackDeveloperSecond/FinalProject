import { reactive } from 'vue'

/**
 * Keeps a registration draft only while this SPA is open.
 *
 * In particular, passwords are never written to sessionStorage, localStorage, a URL, or a
 * server. The draft exists solely so a same-tab visit to the terms/privacy page can return to
 * the registration form without clearing what the visitor just typed.
 */
export const registrationDraft = reactive({
  displayName: '',
  email: '',
  password: '',
  confirmPassword: '',
  acceptTerms: false,
})

export function clearRegistrationDraft(): void {
  registrationDraft.displayName = ''
  registrationDraft.email = ''
  registrationDraft.password = ''
  registrationDraft.confirmPassword = ''
  registrationDraft.acceptTerms = false
}
