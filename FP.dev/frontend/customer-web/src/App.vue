<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from './stores/session'

const sessionStore = useSessionStore()
const router = useRouter()

onMounted(() => {
  void sessionStore.refresh()
})

async function handleLogout(): Promise<void> {
  await sessionStore.logout()
  await router.push('/')
}
</script>

<template>
  <div class="app-shell">
    <header class="site-header">
      <RouterLink
        class="brand-link"
        to="/"
      >
        DoSelect 懂選
      </RouterLink>
      <nav aria-label="主要導覽">
        <RouterLink to="/">
          首頁
        </RouterLink>
        <template v-if="sessionStore.isAuthenticated">
          <span class="site-header__member">{{ sessionStore.user?.displayName }}</span>
          <button
            type="button"
            class="site-header__logout"
            @click="handleLogout"
          >
            登出
          </button>
        </template>
        <RouterLink
          v-else-if="sessionStore.status !== 'loading'"
          to="/register"
        >
          登入／註冊
        </RouterLink>
      </nav>
    </header>
    <main class="site-main">
      <RouterView />
    </main>
    <footer class="site-footer">
      畢業專題展示系統｜商品、付款與物流資料皆為示範用途
    </footer>
  </div>
</template>
