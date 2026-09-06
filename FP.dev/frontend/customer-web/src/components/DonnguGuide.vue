<script setup lang="ts">
import { computed, ref } from 'vue'
import { companionMessage } from './cityCompanion'
import { useRoute } from 'vue-router'

const route = useRoute()
const avatar = `${import.meta.env.BASE_URL}brand/donggu-hero-wave.png`
const storageKey = 'doselect-donngu-guide-open'
const open = ref(true)
try { open.value = localStorage.getItem(storageKey) !== 'false' } catch { /* Storage is optional. */ }
const touring = ref(false)
const tourStep = ref(0)
const tour = [
  { title: '第一站：說用途', text: '遊戲、創作還是日常？先告訴我你想做什麼，不用背規格。', to: '/ai-search', action: '前往說用途' },
  { title: '第二站：給預算', text: '在商品頁設定價格範圍，讓每一次挑選都更有方向。', to: '/products', action: '前往設定預算' },
  { title: '第三站：看推薦', text: '把用途與預算一起告訴 AI 懂選，再查看推薦與相容性說明。', to: '/ai-search', action: '前往 AI 懂選' },
]
const tourGuide = computed(() => tour[tourStep.value]!)
function startTour() { tourStep.value = 0; touring.value = true }
function nextStop() { if (tourStep.value < 2) tourStep.value++; else touring.value = false }
const toggle = ref<HTMLButtonElement | null>(null)
const guide = computed(() => {
  const path = route.path
  if (path.startsWith('/support')) return { title: '客服補給站', text: '我是 Donngu，你的 AI 客服小幫手。可以先說說遇到的問題，需要人工協助時，也能建立客服案件。' }
  if (path.startsWith('/ai-search')) return { title: '靈感導航站', text: '告訴我用途、預算和手邊已有的零件，我們一起找適合你的電腦。送出前，記得確認零件規格喔！' }
  if (path.startsWith('/products')) return { title: '零件探索區', text: '用分類、品牌和預算縮小範圍，再點商品查看規格。還不確定怎麼選？可以到 AI 懂選描述你的需求。' }
  if (path.startsWith('/checkout') || path.startsWith('/payments')) return { title: '出發確認站', text: '請確認聯絡資料、配送方式與訂單金額，再繼續付款。這裡的付款與物流是專題示範流程。' }
  if (path.startsWith('/cart')) return { title: '裝備整備站', text: '這裡集合了你選好的商品。確認數量與組裝內容，準備好就可以前往結帳。' }
  if (path.includes('/builds')) return { title: '組裝實驗室', text: '從用途或零件開始打造你的電腦。留意相容性檢查，登入後還可以保存組裝清單。' }
  if (path.startsWith('/account')) return { title: '市民服務中心', text: '在這裡管理會員資料、常用地址和保存的清單，讓下次採購更輕鬆。' }
  if (path.includes('/orders') || path.startsWith('/guest-orders')) return { title: '訂單追蹤站', text: '查看訂單、付款與配送進度。訪客請先完成訂單驗證，再查看對應的訂單資料。' }
  if (path.startsWith('/returns')) return { title: '售後服務站', text: '查看退貨進度與可執行的操作，依照畫面提示準備資料；有疑問可以到客服中心尋求協助。' }
  if (path.startsWith('/login') || path.startsWith('/register')) return { title: '歡迎成為城市居民', text: '登入後就能管理會員資料與保存清單。第一次來？可以從註冊開始探索懂選。' }
  return { title: '歡迎來到懂選電腦城', text: '嗨，我是 Donngu！從用途、預算或零件出發，讓我陪你找到適合的電腦。點我的頭像，隨時收起或打開這段介紹。' }
})
function setOpen(value: boolean) {
  open.value = value
  try { localStorage.setItem(storageKey, String(value)) } catch { /* Keep working without persistence. */ }
}
function close() {
  setOpen(false)
  toggle.value?.focus()
}
</script>

<template>
  <aside
    class="donngu-guide"
    :class="{ 'donngu-guide--happy': companionMessage }"
    aria-label="Donngu 頁面導覽"
    @keydown.esc="close"
  >
    <div
      v-if="companionMessage"
      class="donngu-guide__celebration"
      role="status"
    >
      {{ companionMessage }}
    </div>
    <section
      v-if="open"
      id="donngu-dialog"
      class="donngu-guide__bubble"
      aria-labelledby="donngu-title"
    >
      <div class="donngu-guide__bar">
        <span>DONNGU · 城市導覽員</span>
        <button
          type="button"
          aria-label="關閉 Donngu 介紹"
          @click="close"
        >
          ×
        </button>
      </div>
      <h2 id="donngu-title">
        {{ touring ? tourGuide.title : guide.title }}
      </h2>
      <p>{{ touring ? tourGuide.text : guide.text }}</p>
      <div
        v-if="touring"
        class="donngu-tour"
      >
        <span class="donngu-tour__progress">城市初體驗 {{ tourStep + 1 }} / 3</span>
        <RouterLink
          :to="tourGuide.to"
          @click="setOpen(false)"
        >
          {{ tourGuide.action }} →
        </RouterLink>
        <div>
          <button
            type="button"
            @click="nextStop"
          >
            {{ tourStep === 2 ? '完成導覽' : '下一站 →' }}
          </button>
          <button
            type="button"
            @click="touring = false"
          >
            跳過導覽
          </button>
        </div>
      </div>
      <button
        v-else
        class="donngu-guide__tour-start"
        type="button"
        @click="startTour"
      >
        第一次來？陪我逛三站 →
      </button>
      <RouterLink
        v-if="!touring"
        to="/support"
        @click="setOpen(false)"
      >
        前往客服中心 <span aria-hidden="true">↗</span>
      </RouterLink>
    </section>
    <button
      ref="toggle"
      class="donngu-guide__avatar"
      type="button"
      :aria-expanded="open"
      aria-controls="donngu-dialog"
      :aria-label="open ? '收起 Donngu 導覽' : '開啟 Donngu 導覽'"
      @click="setOpen(!open)"
    >
      <img
        :src="avatar"
        alt=""
        width="76"
        height="112"
      >
      <span>Donngu</span>
    </button>
  </aside>
</template>
