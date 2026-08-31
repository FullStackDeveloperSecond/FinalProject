<template>
  <section aria-labelledby="page-title">
    <h1 id="page-title">
      管理後台基礎環境已就緒
    </h1>
    <p>後續功能將依照正式規格逐步加入。</p>

    <article class="home-card card">
      <h2>客服 SLA 工作台</h2>
      <p>檢視待處理與逾時的客服案件，並受理未指派的案件。</p>
      <RouterLink
        class="home-card__link"
        to="/support"
      >
        前往客服 SLA 佇列
      </RouterLink>
    </article>

    <article class="home-card card">
      <h2>訂單管理</h2>
      <p>檢視訂單列表與詳情，並執行狀態轉移。</p>
      <RouterLink
        class="home-card__link"
        to="/orders"
      >
        前往訂單管理
      </RouterLink>
    </article>
  </section>
</template>

<style scoped>
.home-card {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.75rem;
  margin-top: 1.5rem;
}

.home-card h2 {
  margin: 0;
  color: var(--color-text);
}

.home-card p {
  margin: 0;
  color: var(--color-text-muted);
}

.home-card__link {
  display: inline-flex;
  align-items: center;
  min-height: var(--control-height);
  padding: 0 18px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--color-primary);
  background: var(--color-primary);
  color: #fff;
  font-weight: 700;
  text-decoration: none;
}

.home-card__link:hover {
  background: var(--color-primary-dark);
  border-color: var(--color-primary-dark);
}
</style>
