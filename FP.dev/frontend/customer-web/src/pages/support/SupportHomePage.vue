<template>
  <section aria-labelledby="support-home-title">
    <h1 id="support-home-title">
      客服中心
    </h1>
    <p class="view-lede">
      人工客服僅開放給已登入會員使用；訪客請改用退貨查詢的訂單 Email 驗證入口。
    </p>

    <div class="support-home__cards">
      <article class="support-home__card card">
        <h2>AI 客服</h2>
        <p>非串流問答，需先同意將對話內容以去識別化方式提供給外部 AI 服務。</p>
        <span class="tag">即將推出</span>
      </article>

      <article class="support-home__card card">
        <h2>我的客服案件</h2>
        <p>查看歷史案件、追蹤處理進度，或建立新的客服案件。</p>
        <RouterLink
          class="btn-link"
          to="/support/tickets"
        >
          查看客服案件
        </RouterLink>
      </article>

      <article class="support-home__card card">
        <h2>建立新案件</h2>
        <p>訂單、付款、物流、商品保固、退貨協助、帳號或其他問題，皆可在此建立案件。</p>
        <RouterLink
          class="btn-link"
          to="/support/tickets/new"
        >
          建立客服案件
        </RouterLink>
      </article>
    </div>
  </section>
</template>

<style scoped>
.support-home__cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
  gap: 1.25rem;
  margin-top: 1.5rem;
}

.support-home__card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.75rem;
}

.support-home__card h2 {
  margin: 0;
  color: var(--color-text);
}

.support-home__card p {
  margin: 0;
  color: var(--color-text-muted);
}
</style>
