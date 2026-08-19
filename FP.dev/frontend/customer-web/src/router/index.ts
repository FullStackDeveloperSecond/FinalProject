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
      path: '/products',
      name: 'products',
      component: () => import('../pages/ProductsPage.vue'),
    },
    {
      path: '/products/:productId',
      name: 'product-detail',
      component: () => import('../pages/ProductDetailPage.vue'),
      props: true,
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
