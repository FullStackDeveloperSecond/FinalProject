<template>
  <section aria-labelledby="support-home-title">
    <h1 id="support-home-title">
      客服中心
    </h1>
    <p>人工客服僅開放給已登入會員使用；訪客請改用退貨查詢的訂單 Email 驗證入口。</p>

    <div class="support-home__cards">
      <article class="support-home__card">
        <h2>AI 客服</h2>
        <p>非串流問答，需先同意將對話內容以去識別化方式提供給外部 AI 服務。</p>
        <p class="support-home__badge">
          即將推出
        </p>
      </article>

      <article class="support-home__card">
        <h2>我的客服案件</h2>
        <p>查看歷史案件、追蹤處理進度，或建立新的客服案件。</p>
        <RouterLink
          class="support-home__link"
          to="/support/tickets"
        >
          查看客服案件
        </RouterLink>
      </article>

      <article class="support-home__card">
        <h2>建立新案件</h2>
        <p>訂單、付款、物流、商品保固、退貨協助、帳號或其他問題，皆可在此建立案件。</p>
        <RouterLink
          class="support-home__link"
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
  gap: 1rem;
  margin-top: 1.5rem;
}

.support-home__card {
  padding: 1.25rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
}

.support-home__card h2 {
  margin-top: 0;
}

.support-home__link {
  display: inline-block;
  min-height: 2.75rem;
  padding: 0.75rem 1rem;
  border: 1px solid #1d4ed8;
  border-radius: 0.5rem;
  background: #2563eb;
  color: #fff;
  text-decoration: none;
}

.support-home__badge {
  display: inline-block;
  padding: 0.25rem 0.625rem;
  border-radius: 999px;
  background: #f3f4f6;
  color: #4b5563;
  font-size: 0.8125rem;
  font-weight: 700;
}
</style>
