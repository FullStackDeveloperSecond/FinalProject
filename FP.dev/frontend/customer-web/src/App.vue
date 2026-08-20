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
        <RouterLink to="/register">
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
