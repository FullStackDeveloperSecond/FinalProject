<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, provide, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from './stores/session'
import { useCartIdentityCacheCleanup } from './features/cart/useCart'
import { BrandMark } from '@doselect/web-shared/components'
import DonnguGuide from './components/DonnguGuide.vue'
import CitySideStreets from './components/CitySideStreets.vue'
import MemberMenu from './components/MemberMenu.vue'
import './city-streets.css'
import {
  customerDefaultMotionPresetId,
  motionPresetKey,
  useMotionPreference,
  useMotionPresetSelection,
} from '@doselect/web-shared/motion'

// 切換器只在明確啟用 motion debug 的 dev 進入模組圖；production build 會移除整個分支。
const motionDebugEnabled = import.meta.env.DEV === true
  && import.meta.env.VITE_ENABLE_MOTION_DEBUG === 'true'
const MotionDevSwitcher = motionDebugEnabled
  ? defineAsyncComponent(() => import('@doselect/web-shared/motion/MotionDevSwitcher.vue'))
  : null

const route = useRoute()
const router = useRouter()
const sessionStore = useSessionStore()
const isSupportSection = computed(() => route.path === '/support' || route.path.startsWith('/support/'))
const isWelcomePage = computed(() => route.name === 'welcome')

// 組長 PR #29 round-6 review, P1 (point 3): registered here — mounted for the SPA's entire
// lifetime — rather than inside CartPage.vue, so an identity change (login/logout/account switch)
// evicts the previous identity's cart cache regardless of which page happens to be open at the
// moment it changes.
useCartIdentityCacheCleanup()

// 窄畫面把主導覽收起來，避免導覽列擠壓內容或造成頁面級橫向捲動。
const navOpen = ref(false)

/**
 * 路由一變就把展開的行動版選單關掉。
 *
 * 監聽 `route.fullPath` 而不是 `route.path`：只換 query 或 hash 也算導覽
 * （首頁分類卡去的就是 `/products?category=CPU`，路徑相同、query 不同），
 * 這種情況一樣要收起選單。也因為監聽的是路由狀態而不是點擊事件，
 * RouterLink 與程式導航（`router.push`）兩條路徑都會被涵蓋。
 */
watch(() => route.fullPath, () => {
  navOpen.value = false
})

// GSAP 動態視覺探索：A／B／C 方案由 App 統一選定後 provide 給頁面。
// `canSwitch` 預設為 false；只有明確啟用的本機 dev 才顯示切換介面。
const { presetId, preset, canSwitch, select } = useMotionPresetSelection(customerDefaultMotionPresetId)
const prefersReducedMotion = useMotionPreference()
provide(motionPresetKey, preset)

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
          to="/welcome"
          aria-label="DoSelect 懂選城市入口"
        >
          <!-- 標記是裝飾：旁邊的文字才是這個連結唯一的 accessible name，避免品牌名被念兩次 -->
          <BrandMark decorative />
          <span class="brand-link__text">DoSelect<span class="brand-link__sub">懂選</span></span>
        </RouterLink>

        <button
          type="button"
          class="nav-toggle"
          :aria-expanded="navOpen"
          aria-controls="primary-nav"
          @click="navOpen = !navOpen"
        >
          選單
        </button>

        <nav
          id="primary-nav"
          class="primary-nav"
          :class="{ 'primary-nav--open': navOpen }"
          aria-label="主要導覽"
        >
          <!--
            瀏覽區：不需登入就能看的頁面。順序刻意對齊「城市路標」的四站
            （靈感站→AI 懂選、零件街→商品、組裝所→新增組裝清單），
            讓側欄與頁首講同一套動線；購物車接在組裝流程之後。
          -->
          <div class="primary-nav__group primary-nav__browse">
            <RouterLink to="/">
              首頁
            </RouterLink>
            <RouterLink to="/ai-search">
              AI 懂選
            </RouterLink>
            <RouterLink to="/products">
              商品
            </RouterLink>
            <RouterLink to="/builds/new">
              新增組裝清單
            </RouterLink>
            <RouterLink to="/cart">
              購物車
            </RouterLink>
          </div>

          <!--
            會員區：靠右並以分隔線隔開。這裡只放 router meta 標了 requiresAuth 的目的地
            （/support、/account/builds、/account/favorites、/account、/account/addresses、
            /account/reviews）加上登入入口，讓「點了會要求登入」的項目在視覺上先分好類。
            補給站（客服中心）雖然是城市路標第 04 站，但需要登入，所以歸在這一區。
          -->
          <div class="primary-nav__group primary-nav__account">
            <RouterLink
              to="/support"
              :aria-current="isSupportSection ? 'page' : undefined"
              :class="{ 'router-link-active': isSupportSection }"
            >
              客服中心
            </RouterLink>
            <!--
              登入後原本會攤開 6 個會員項目加名稱與登出，頂層一共 13 項，
              掃視成本太高。改成把個人內容（組裝清單／收藏／評價）與帳戶設定
              （會員資料／收件地址）收進以名稱為觸發鈕的下拉選單，
              頂層只留「客服中心」與會員選單兩項。
            -->
            <MemberMenu
              v-if="sessionStore.isAuthenticated"
              :display-name="sessionStore.user?.displayName"
              @logout="handleLogout"
            />
            <RouterLink
              v-else-if="sessionStore.status === 'anonymous'"
              class="site-header__signin"
              to="/login"
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
          </div>
        </nav>
      </div>
    </header>
    <main
      id="main-content"
      class="site-main"
      :class="{ 'site-main--welcome': isWelcomePage }"
      tabindex="-1"
    >
      <div
        v-if="route.path !== '/' && !isWelcomePage"
        class="city-district"
        aria-hidden="true"
      >
        <span>DOSELECT COMPUTER CITY</span>
        <p>在懂選，找到你的下一站。</p>
      </div>
      <div
        v-if="isWelcomePage"
        class="welcome-content"
      >
        <RouterView />
      </div>
      <div
        v-else
        class="city-content"
      >
        <CitySideStreets />
        <div class="view-shell">
          <RouterView />
        </div>
      </div>
    </main>
    <footer
      v-if="!isWelcomePage"
      class="site-footer"
    >
      <p class="site-footer__brand">
        DoSelect 懂選
      </p>
      <p>畢業專題展示系統｜商品、付款與物流資料皆為示範用途</p>
      <p class="site-footer__links">
        <RouterLink to="/support">
          客服中心
        </RouterLink>
        <RouterLink to="/products">
          全部商品
        </RouterLink>
      </p>
    </footer>
    <DonnguGuide v-if="!isWelcomePage" />
    <component
      :is="MotionDevSwitcher"
      v-if="canSwitch && MotionDevSwitcher"
      :preset-id="presetId"
      :reduced-motion="prefersReducedMotion"
      @select="select"
    />
  </div>
</template>
