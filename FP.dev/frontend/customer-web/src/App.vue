<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from './stores/session'
import { useCartIdentityCacheCleanup } from './features/cart/useCart'

const route = useRoute()
const router = useRouter()
const sessionStore = useSessionStore()
const isSupportSection = computed(() => route.path === '/support' || route.path.startsWith('/support/'))

// 組長 PR #29 round-6 review, P1 (point 3): registered here — mounted for the SPA's entire
// lifetime — rather than inside CartPage.vue, so an identity change (login/logout/account switch)
// evicts the previous identity's cart cache regardless of which page happens to be open at the
// moment it changes.
useCartIdentityCacheCleanup()

onMounted(() => {
  void sessionStore.refresh()
})

async function handleLogout(): Promise<void> {
  await sessionStore.logout()
  await router.push('/')
}
</script>

<template>
  <a
    class="skip-link"
    href="#main-content"
  >
    跳到主要內容
  </a>
  <div class="app-shell">
    <header class="site-header">
      <div class="header-bar">
        <RouterLink
          class="brand-link"
          to="/"
        >
          DoSelect 懂選
        </RouterLink>
        <nav
          class="primary-nav"
          aria-label="主要導覽"
        >
          <RouterLink to="/">
            首頁
          </RouterLink>
          <RouterLink to="/products">
            商品
          </RouterLink>
          <RouterLink to="/cart">
            購物車
          </RouterLink>
          <RouterLink
            to="/support"
            :aria-current="isSupportSection ? 'page' : undefined"
            :class="{ 'router-link-active': isSupportSection }"
          >
            客服中心
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
            v-else-if="sessionStore.status === 'anonymous'"
            to="/register"
          >
            登入／註冊
          </RouterLink>
          <button
            v-else-if="sessionStore.status === 'error'"
            type="button"
            class="site-header__identity-retry"
            @click="sessionStore.refresh()"
          >
            無法確認登入狀態，點此重試
          </button>
        </nav>
      </div>
    </header>
    <main
      id="main-content"
      class="site-main"
      tabindex="-1"
    >
      <div class="view-shell">
        <RouterView />
      </div>
    </main>
    <footer class="site-footer">
      畢業專題展示系統｜商品、付款與物流資料皆為示範用途
    </footer>
  </div>
</template>
