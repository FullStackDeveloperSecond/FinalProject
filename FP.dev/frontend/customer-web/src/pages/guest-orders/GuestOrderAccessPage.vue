<script setup lang="ts">
import GuestOrderAccessForm from '../../features/guest-orders/GuestOrderAccessForm.vue'
</script>

<template>
  <section aria-labelledby="page-title">
    <h1 id="page-title">
      查詢訂單
    </h1>
    <p>輸入訂單編號與下單時使用的 Email，我們會寄送驗證碼供您查詢訂單狀態。</p>
    <GuestOrderAccessForm />
  </section>
</template>
