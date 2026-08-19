<template>
  <div class="app-shell">
    <header class="site-header">
      <RouterLink
        class="brand-link"
        to="/"
      >
        DoSelect 懂選｜管理後台
      </RouterLink>
      <span class="demo-badge">DEMO DATA</span>
    </header>
    <div class="admin-frame">
      <aside
        class="admin-sidebar"
        aria-label="管理功能導覽"
      >
        <RouterLink to="/">
          首頁
        </RouterLink>
      </aside>
      <main class="site-main">
        <RouterView />
      </main>
    </div>
  </div>
</template>
