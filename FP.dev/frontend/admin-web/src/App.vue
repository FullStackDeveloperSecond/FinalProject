<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminAuthStore } from './features/auth/stores/useAdminAuthStore'

const route = useRoute()
const router = useRouter()
const auth = useAdminAuthStore()

const isAuthPage = computed(() => route.path.startsWith('/login'))

async function onLogout(): Promise<void> {
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <div
    v-if="isAuthPage"
    class="app-shell app-shell--bare"
  >
    <RouterView />
  </div>

  <div
    v-else
    class="app-shell"
  >
    <header class="site-header">
      <RouterLink
        class="brand-link"
        to="/"
      >
        DoSelect 懂選｜管理後台
      </RouterLink>
      <div class="site-header__end">
        <span class="demo-badge">DEMO DATA</span>
        <span
          v-if="auth.currentUser"
          class="current-user"
        >{{ auth.currentUser.displayName }}</span>
        <RouterLink
          v-if="auth.isAuthenticated"
          to="/security/totp-rebind"
          class="totp-rebind-link"
        >
          重新綁定 TOTP
        </RouterLink>
        <button
          v-if="auth.isAuthenticated"
          type="button"
          class="logout-button"
          @click="onLogout"
        >
          登出
        </button>
      </div>
    </header>
    <div class="admin-frame">
      <aside
        class="admin-sidebar"
        aria-label="管理功能導覽"
      >
        <nav class="admin-sidebar__nav">
          <RouterLink to="/">
            首頁
          </RouterLink>
          <RouterLink to="/support">
            客服 SLA 佇列
          </RouterLink>
        </nav>
      </aside>
      <main class="site-main">
        <RouterView />
      </main>
    </div>
  </div>
</template>
