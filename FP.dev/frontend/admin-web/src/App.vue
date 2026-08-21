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
