import { createRouter, createWebHistory } from 'vue-router'
import { HttpStatusPage } from '@doselect/web-shared/components'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    guestOnly?: boolean
    requiresChallenge?: boolean
  }
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../pages/HomePage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('../features/auth/pages/LoginPage.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/login/verify',
      name: 'login-verify',
      component: () => import('../features/auth/pages/TotpVerifyPage.vue'),
      meta: { guestOnly: true, requiresChallenge: true },
    },
    {
      path: '/login/enroll',
      name: 'login-enroll',
      component: () => import('../features/auth/pages/TotpEnrollPage.vue'),
      meta: { guestOnly: true, requiresChallenge: true },
    },
    {
      path: '/security/totp-rebind',
      name: 'totp-rebind',
      component: () => import('../features/auth/pages/TotpRebindPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/support',
      name: 'support-sla-queue',
      component: () => import('../pages/support/SupportSlaQueuePage.vue'),
    },
    {
      path: '/support/tickets/:ticketId',
      name: 'support-ticket-detail',
      component: () => import('../pages/support/SupportTicketDetailPage.vue'),
    },
    {
      // Confirmed Route contract (M功能桌面UI與Route規格.md A-08): one page manages
      // brand／category／tag together, not three separate routes.
      path: '/catalog/lookups',
      name: 'catalog-lookups',
      component: () => import('../pages/CatalogLookupsPage.vue'),
    },
    {
      path: '/products',
      name: 'products',
      component: () => import('../pages/ProductsPage.vue'),
    },
    {
      path: '/products/new',
      name: 'product-new',
      component: () => import('../pages/ProductEditPage.vue'),
    },
    {
      // Confirmed Route contract (M功能桌面UI與Route規格.md A-06): /admin/products/:productId,
      // not /admin/products/:id/edit.
      path: '/products/:productId',
      name: 'product-edit',
      component: () => import('../pages/ProductEditPage.vue'),
      props: true,
    },
    {
      path: '/returns',
      name: 'admin-return-queue',
      component: () => import('../pages/returns/AdminReturnQueuePage.vue'),
    },
    {
      path: '/returns/:returnId',
      name: 'admin-return-detail',
      component: () => import('../pages/returns/AdminReturnDetailPage.vue'),
    },
    {
      path: '/unauthorized',
      name: 'unauthorized',
      component: HttpStatusPage,
      props: { status: 401, homeHref: '/admin/' },
    },
    {
      path: '/forbidden',
      name: 'forbidden',
      component: HttpStatusPage,
      props: { status: 403, homeHref: '/admin/' },
    },
    {
      path: '/error',
      name: 'server-error',
      component: HttpStatusPage,
      props: { status: 500, homeHref: '/admin/' },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: HttpStatusPage,
      props: { status: 404, homeHref: '/admin/' },
    },
  ],
})

// 這是本 App 第一個 router guard：未登入導向 /login，已登入卻造訪登入頁則導回首頁。
// 前端這裡的判斷只是體驗優化，真正把關在後端 Policy（DoSelectPolicies.*）。
router.beforeEach(async (to) => {
  if (!to.meta.requiresAuth && !to.meta.guestOnly && !to.meta.requiresChallenge) {
    return true
  }

  const auth = useAdminAuthStore()
  if (auth.session === null) {
    await auth.fetchSession()
  }

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.guestOnly && auth.isAuthenticated && to.name !== 'login-enroll') {
    return { name: 'home' }
  }

  // challengePublicId 只存在 Pinia 記憶體（全站沒有任何 persistence plugin）。重新整理
  // /login/verify 或 /login/enroll 會讓它遺失；雖然 .DoSelect.AdminChallenge Cookie 本身
  // reload 後仍有效，但對前端 JS 是 httponly 不可讀，沒有辦法還原這個值。與其讓使用者卡在
  // 一個沒有 challenge 可用的死頁面（原本的 bug），直接導回登入頁重新開始。保留原本帶著的
  // redirect（如果有）——不這樣做，從深層連結進來、半路遺失 challenge 重新走一次登入的人，
  // 完成後會被導回首頁而不是原本要去的頁面（alex review 第三輪 P2#4）。
  if (to.meta.requiresChallenge && auth.challenge === null) {
    return { name: 'login', query: to.query.redirect ? { redirect: to.query.redirect } : {} }
  }

  return true
})

export default router
