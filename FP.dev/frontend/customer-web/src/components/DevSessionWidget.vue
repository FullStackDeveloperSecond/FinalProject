<script setup lang="ts">
import { ref } from 'vue'
import { useDevSessionStore } from '../stores/devSession'

const session = useDevSessionStore()
const emailInput = ref('kafen-test-member-a@doselect.local')

async function handleSignIn() {
  await session.signIn(emailInput.value).catch(() => undefined)
}
</script>

<template>
  <div class="dev-session-widget">
    <span class="dev-session-widget__badge">DEV</span>
    <template v-if="session.isSignedIn">
      <span>{{ session.email }}</span>
      <button
        type="button"
        @click="session.signOut()"
      >
        登出測試帳號
      </button>
    </template>
    <template v-else>
      <input
        v-model="emailInput"
        type="email"
        placeholder="test-member@doselect.local"
      >
      <button
        type="button"
        :disabled="session.isSigningIn"
        @click="handleSignIn"
      >
        {{ session.isSigningIn ? '登入中…' : '以測試帳號登入' }}
      </button>
      <span
        v-if="session.error"
        class="dev-session-widget__error"
      >{{ session.error }}</span>
    </template>
  </div>
</template>

<style scoped>
.dev-session-widget {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
}

.dev-session-widget input {
  min-height: 2rem;
  padding: 0.25rem 0.5rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
  font: inherit;
}

.dev-session-widget button {
  min-height: 2rem;
  padding: 0.25rem 0.75rem;
  font-size: 0.8125rem;
}

.dev-session-widget__badge {
  padding: 0.125rem 0.375rem;
  border-radius: 0.25rem;
  background: #fef3c7;
  color: #92400e;
  font-weight: 700;
  font-size: 0.6875rem;
  letter-spacing: 0.05em;
}

.dev-session-widget__error {
  color: #b91c1c;
}
</style>
