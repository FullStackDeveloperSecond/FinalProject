<script setup lang="ts">
import { BrandIcon, type BrandIconName } from '@doselect/web-shared/components'
import HomePromotions from '../components/HomePromotions.vue'
import { onMounted, ref } from 'vue'
import {
  createIconDraw,
  createListStagger,
  createPageReveal,
  useInjectedMotionPreset,
  useMotionScope,
} from '@doselect/web-shared/motion'



// 進場動畫只透過 template ref 操作自己的節點，不用全域 selector，
// 也不依賴 DOM index 判斷任何業務資料。
const homeRoot = ref<HTMLElement | null>(null)
const heroRef = ref<HTMLElement | null>(null)
const stepItems = ref<HTMLElement[]>([])
const categoryItems = ref<HTMLElement[]>([])
const iconHosts = ref<HTMLElement[]>([])

// 以 BASE_URL 組出執行期 public 路徑，避免被 Vite 當成建置期資產匯入。
const heroArtSrc = `${import.meta.env.BASE_URL}brand/city-map.jpg`
const preset = useInjectedMotionPreset()
const motion = useMotionScope(homeRoot)

onMounted(() => {
  motion.run(({ reducedMotion }) => {
    createPageReveal(heroRef.value, preset.value, { reducedMotion })
    createListStagger(stepItems.value, preset.value, { reducedMotion, delay: 0.06 })
    createListStagger(categoryItems.value, preset.value, { reducedMotion, delay: 0.1 })
    // 圖示線條在卡片就位後才描繪，一次性、不循環；reduced motion 下完全不建立。
    createIconDraw(iconHosts.value, preset.value, { reducedMotion, delay: 0.24 })
  })
})

/**
 * 三步驟導購：每一步只做一個決定，不需要先懂規格名稱。
 * icon 只是視覺輔助，卡片的文字標題一律保留，不會變成只有圖示。
 */
interface HomeGuideItem {
  step: string
  title: string
  body: string
  icon: BrandIconName
  to: string
}

const steps: HomeGuideItem[] = [
  { step: '第 1 步', title: '說用途', body: '打電動、剪片、還是文書上網？我們把用途換算成需要的規格。', icon: 'purpose', to: '/ai-search' },
  { step: '第 2 步', title: '給預算', body: '前往商品篩選，設定價格上下限，找到預算內的選擇。', icon: 'budget', to: '/products' },
  { step: '第 3 步', title: '看推薦', body: '前往 AI 懂選，描述需求後查看推薦理由與相容性結果。', icon: 'recommend', to: '/ai-search' },
]

/**
 * 圖形分類入口：用途導向的說明，不用零件術語。
 *
 * `category` 必須是後端 catalog 契約真的認得的代碼 —— 也就是
 * `CompatibilityCatalogContract.Categories`（CPU／MOTHERBOARD／MEMORY／GPU／
 * STORAGE／PSU／CASE／CPU_COOLER），與 `/api/v1/catalog/filter-options` 回傳的
 * `CategoryFilterOption.code` 同一組值。
 *
 * 這裡刻意用固定的 allowlist 而不是打 API 動態產生：首頁要在拿到 filter-options
 * 之前就能立刻顯示分類入口，而寫死一組**保證有效**的代碼，比 fallback 到無效值安全。
 * 之前寫的 desktop／laptop／monitor／component／accessory 都不在契約內，
 * 點進去只會得到空結果。
 */
interface HomeCategoryItem {
  title: string
  body: string
  icon: BrandIconName
  /** 對應 catalog 契約的分類代碼；與 `to` 互斥。 */
  categoryCode?: string
  to?: string
}

const categories: HomeCategoryItem[] = [
  { title: '處理器', body: '決定整台電腦的運算速度', icon: 'cpu', categoryCode: 'CPU' },
  { title: '主機板', body: '把所有零件接起來的底座', icon: 'motherboard', categoryCode: 'MOTHERBOARD' },
  { title: '記憶體', body: '同時開很多程式也不卡', icon: 'memory', categoryCode: 'MEMORY' },
  { title: '顯示卡', body: '打電動、剪片的關鍵', icon: 'gpu', categoryCode: 'GPU' },
  { title: '儲存裝置', body: '升級容量與開機速度', icon: 'storage', categoryCode: 'STORAGE' },
  { title: '機殼', body: '裝得下、散熱好、看得順眼', icon: 'case', categoryCode: 'CASE' },
  { title: '自由組裝', body: '自己配零件，我們幫你檢查相容性', icon: 'custom-build', to: '/builds/new' },
]

</script>

<template>
  <div
    ref="homeRoot"
    class="home"
  >
    <HomePromotions />
    <section
      ref="heroRef"
      class="home-hero"
      aria-labelledby="page-title"
    >
      <div
        class="home-hero__art"
        aria-hidden="true"
      >
        <img
          :src="heroArtSrc"
          alt=""
          width="1264"
          height="843"
          fetchpriority="high"
          decoding="async"
        >
      </div>
      <div class="home-hero__text">
        <h1 id="page-title">
          說出需求，組出適合你的電腦
        </h1>
      </div>
      <section
        class="home-hero__guide"
        aria-labelledby="home-steps-title"
      >
        <h2 id="home-steps-title">
          三步驟就好
        </h2>
        <p class="home-section__lede">
          每一步只做一個決定，不需要背規格名稱。
        </p>
        <ol class="home-steps">
          <li
            v-for="(item, index) in steps"
            ref="stepItems"
            :key="item.title"
            class="home-step"
          >
            <RouterLink
              :to="item.to"
              class="home-step__link"
            >
              <span class="home-step__head">
                <!-- 編號圓標只做流程進度感；文字步驟標籤仍保留在 .home-step__no -->
                <span
                  class="home-step__marker"
                  aria-hidden="true"
                >{{ index + 1 }}</span>
                <span
                  ref="iconHosts"
                  class="home-step__icon"
                  aria-hidden="true"
                >
                  <BrandIcon
                    :name="item.icon"
                    :size="30"
                  />
                </span>
                <span class="home-step__no">{{ item.step }}</span>
              </span>
              <h3>{{ item.title }}</h3>
              <p>{{ item.body }}</p>
              <span class="home-step__go">前往{{ item.title }} <span aria-hidden="true">→</span></span>
            </RouterLink>
          </li>
        </ol>
      </section>
    </section>



    <section
      class="home-section"
      aria-labelledby="home-categories-title"
    >
      <h2 id="home-categories-title">
        看看你需要哪一類
      </h2>
      <p class="home-section__lede">
        用分類快速找到方向，不確定也沒關係。
      </p>
      <ul class="home-categories">
        <li
          v-for="item in categories"
          ref="categoryItems"
          :key="item.title"
        >
          <RouterLink
            class="home-category"
            :to="item.to ?? { path: '/products', query: { category: item.categoryCode } }"
            :data-category-code="item.categoryCode"
          >
            <span
              ref="iconHosts"
              class="home-category__icon"
              aria-hidden="true"
            >
              <BrandIcon
                :name="item.icon"
                :size="26"
              />
            </span>
            <h3>{{ item.title }}</h3>
            <p>{{ item.body }}</p>
          </RouterLink>
        </li>
      </ul>
    </section>

    <section
      class="home-hint"
      aria-labelledby="home-hint-title"
    >
      <h2 id="home-hint-title">
        第一次買電腦？
      </h2>
      <p>
        從「說用途」開始最快。挑完之後我們會告訴你這台適合做什麼、不適合做什麼，
        不用自己查規格表。
      </p>
      <RouterLink to="/support">
        還是想問人？前往客服中心
      </RouterLink>
    </section>
  </div>
</template>
