<script setup lang="ts">
defineProps<{
  kind: 'terms' | 'privacy' | 'return'
  embedded?: boolean
}>()
</script>

<template>
  <article
    class="legal-demo"
    aria-labelledby="legal-demo-title"
  >
    <header>
      <p class="legal-demo__eyebrow">
        DoSelect 懂選｜Demo 引導範例
      </p>
      <h1 id="legal-demo-title">
        {{ kind === 'terms' ? '服務條款' : kind === 'return' ? '退換貨政策' : '隱私權政策' }}
      </h1>
      <p
        class="legal-demo__notice"
        role="note"
      >
        本頁是畢業專題 Demo 的引導範例，不具正式法律效力，也不取代正式營運時應由專業人員審閱的法律文件。
      </p>
      <p>展示版本：demo-2026-09-09</p>
    </header>

    <template v-if="kind === 'terms'">
      <section>
        <h2>一、服務內容</h2>
        <p>懂選提供商品瀏覽、電腦組裝建議、購物車、模擬訂單、客服與管理功能。本 Demo 的商品、價格、付款、物流及報表資料皆為測試資料。</p>
      </section>
      <section>
        <h2>二、帳戶使用</h2>
        <p>請使用本人可接收驗證信的 Email，妥善保管密碼，不得冒用他人身分或干擾系統。發現異常時請立即停止操作並通知測試管理者。</p>
      </section>
      <section>
        <h2>三、訂單與付款</h2>
        <p>本 Demo 的下單、付款、退款、發票與配送流程僅供功能展示，不構成真實交易、付款義務或出貨承諾。</p>
      </section>
      <section>
        <h2>四、合理使用</h2>
        <p>不得上傳惡意內容、嘗試未授權存取、規避權限或大量發送請求。系統可能因維護或測試而暫停，展示資料也可能重設。</p>
      </section>
    </template>

    <template v-else-if="kind === 'return'">
      <section>
        <h2>一、模擬退換貨</h2>
        <p>本 Demo 的退換貨只供流程測試，不涉及真實商品寄回、退款或換貨承諾。</p>
      </section>
      <section>
        <h2>二、申請流程</h2>
        <p>請從自己的訂單明細提出申請，選擇品項、數量與原因，再依畫面查看審核和模擬退款進度。訪客須先完成訂單存取驗證。</p>
      </section>
      <section>
        <h2>三、測試注意事項</h2>
        <p>請勿上傳身分證件、金融資料或其他敏感內容。如遇到無法申請或金額異常，請停止該次測試並通知測試管理者。</p>
      </section>
    </template>
    <template v-else>
      <section>
        <h2>一、可能使用的資料</h2>
        <p>Demo 可能使用註冊 Email、顯示名稱、購物車、訂單、客服訊息、操作紀錄與 AI 查詢內容，以完成登入、交易流程展示、問題排查及安全稽核。</p>
      </section>
      <section>
        <h2>二、使用目的</h2>
        <p>資料僅用於測試功能、呈現測試結果、寄送必要通知、防止濫用與改善 Demo。請勿輸入真實付款資訊、身分證件或其他不必要的敏感資料。</p>
      </section>
      <section>
        <h2>三、外部服務</h2>
        <p>啟用相關功能時，系統可能透過 OpenAI 處理 AI 查詢，並透過 SMTP 服務寄送驗證信；只傳送完成該功能所需的最少資料。</p>
      </section>
      <section>
        <h2>四、保存與測試權利</h2>
        <p>測試資料可能隨 Demo 重設而刪除。若要停止測試、查詢或移除自行建立的測試帳戶，請聯絡測試管理者。</p>
      </section>
    </template>

    <p
      v-if="!embedded"
      class="legal-demo__back"
    >
      <RouterLink to="/register">
        返回註冊
      </RouterLink>
      <RouterLink to="/">
        返回首頁
      </RouterLink>
    </p>
  </article>
</template>

<style scoped>
.legal-demo {
  display: grid;
  gap: 1.25rem;
  max-width: 56rem;
  margin: 0 auto;
}

.legal-demo header,
.legal-demo section {
  padding: 1.25rem;
  border: 1px solid var(--color-border, #cbd5e1);
  border-radius: 0.75rem;
  background: var(--color-surface, #fff);
}

.legal-demo__eyebrow {
  font-weight: 700;
}

.legal-demo__notice {
  padding: 0.875rem;
  border-left: 0.3rem solid #d97706;
  background: #fff7ed;
  color: #7c2d12;
}

.legal-demo__back {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
}
</style>
