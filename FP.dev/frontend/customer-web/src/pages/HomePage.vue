<script setup lang="ts">
import { AppButton, BrandIcon, type BrandIconName } from '@doselect/web-shared/components'
import { useRouter } from 'vue-router'
import { onMounted, ref } from 'vue'
import {
  createIconDraw,
  createListStagger,
  createPageReveal,
  useInjectedMotionPreset,
  useMotionScope,
} from '@doselect/web-shared/motion'

const router = useRouter()

// 進場動畫只透過 template ref 操作自己的節點，不用全域 selector，
// 也不依賴 DOM index 判斷任何業務資料。
const homeRoot = ref<HTMLElement | null>(null)
const heroRef = ref<HTMLElement | null>(null)
const stepItems = ref<HTMLElement[]>([])
const categoryItems = ref<HTMLElement[]>([])
const iconHosts = ref<HTMLElement[]>([])

// 以 BASE_URL 組出執行期 public 路徑，避免被 Vite 當成建置期資產匯入。
const heroArtSrc = `${import.meta.env.BASE_URL}brand/donggu-hero-wave.png`
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
}

const steps: HomeGuideItem[] = [
  { step: '第 1 步', title: '說用途', body: '打電動、剪片、還是文書上網？我們把用途換算成需要的規格。', icon: 'purpose' },
  { step: '第 2 步', title: '給預算', body: '設定金額上限，只看買得起的組合，不會挑到超出預算的。', icon: 'budget' },
  { step: '第 3 步', title: '看推薦', body: '每個推薦都附「為什麼適合你」，以及相容性檢查結果。', icon: 'recommend' },
]

/** 圖形分類入口：用途導向的說明，不用零件術語。 */
interface HomeCategoryItem {
  title: string
  body: string
  icon: BrandIconName
  query?: Record<string, string>
  to?: string
}

const categories: HomeCategoryItem[] = [
  { title: '桌上型電腦', body: '整台組好，插電就能用', icon: 'desktop', query: { category: 'desktop' } },
  { title: '筆記型電腦', body: '帶著走，上課上班都行', icon: 'laptop', query: { category: 'laptop' } },
  { title: '螢幕', body: '看得清楚、眼睛不累', icon: 'monitor', query: { category: 'monitor' } },
  { title: '零組件', body: '升級容量與速度', icon: 'component', query: { category: 'component' } },
  { title: '周邊配件', body: '鍵盤滑鼠耳機', icon: 'peripheral', query: { category: 'accessory' } },
  { title: '自由組裝', body: '自己配零件，我們幫你檢查相容性', icon: 'custom-build', to: '/builds/new' },
]

function goToProducts(): void {
  void router.push('/products')
}

function goToAiSearch(): void {
  void router.push('/ai-search')
}
</script>

<template>
  <div
    ref="homeRoot"
    class="home"
  >
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
          width="320"
          height="480"
          loading="lazy"
          decoding="async"
        >
      </div>
      <div class="home-hero__text">
        <h1 id="page-title">
          說出需求，組出適合你的電腦
        </h1>
        <p>
          不用先懂規格。回答「要拿來做什麼」和「預算多少」，我們幫你篩掉不合適的，
          只留下買得起、搭得起來的組合。
        </p>
        <div class="home-hero__actions">
          <AppButton
            variant="primary"
            @click="goToAiSearch"
          >
            依用途挑選
          </AppButton>
          <AppButton
            variant="secondary"
            @click="goToProducts"
          >
            依預算挑選
          </AppButton>
          <AppButton
            variant="ghost"
            @click="goToProducts"
          >
            看全部商品
          </AppButton>
        </div>
      </div>
    </section>

    <section
      class="home-section"
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
        </li>
      </ol>
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
            :to="item.to ?? { path: '/products', query: item.query }"
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
        從「依用途挑選」開始最快。挑完之後我們會告訴你這台適合做什麼、不適合做什麼，
        不用自己查規格表。
      </p>
      <RouterLink to="/support">
        還是想問人？前往客服中心
      </RouterLink>
    </section>
  </div>
</template>
