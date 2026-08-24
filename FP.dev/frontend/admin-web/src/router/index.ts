import { createRouter, createWebHistory } from 'vue-router'
import { HttpStatusPage } from '@doselect/web-shared/components'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    guestOnly?: boolean
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
      meta: { guestOnly: true },
    },
    {
      path: '/login/enroll',
      name: 'login-enroll',
      component: () => import('../features/auth/pages/TotpEnrollPage.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/members',
      name: 'member-list',
      component: () => import('../features/members/pages/MemberListPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/members/:publicId',
      name: 'member-detail',
      component: () => import('../features/members/pages/MemberDetailPage.vue'),
      meta: { requiresAuth: true },
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
  if (!to.meta.requiresAuth && !to.meta.guestOnly) {
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

  return true
})

export default router
