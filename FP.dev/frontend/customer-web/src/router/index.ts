import { createRouter, createWebHistory } from 'vue-router'
import { HttpStatusPage } from '@doselect/web-shared/components'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../pages/HomePage.vue'),
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('../pages/RegisterPage.vue'),
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('../pages/LoginPage.vue'),
    },
    {
      path: '/verify-email',
      name: 'verify-email',
      component: () => import('../pages/VerifyEmailPage.vue'),
    },
    {
      path: '/forgot-password',
      name: 'forgot-password',
      component: () => import('../pages/ForgotPasswordPage.vue'),
    },
    {
      path: '/reset-password',
      name: 'reset-password',
      component: () => import('../pages/ResetPasswordPage.vue'),
    },
    {
      path: '/unauthorized',
      name: 'unauthorized',
      component: HttpStatusPage,
      props: { status: 401, homeHref: '/' },
    },
    {
      path: '/forbidden',
      name: 'forbidden',
      component: HttpStatusPage,
      props: { status: 403, homeHref: '/' },
    },
    {
      path: '/error',
      name: 'server-error',
      component: HttpStatusPage,
      props: { status: 500, homeHref: '/' },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: HttpStatusPage,
      props: { status: 404, homeHref: '/' },
    },
  ],
})

export default router
