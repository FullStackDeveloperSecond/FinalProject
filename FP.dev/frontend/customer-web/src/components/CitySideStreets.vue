<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { BrandIcon, type BrandIconName } from '@doselect/web-shared/components'
import { clearRecentProducts, loadRecentProducts, recentProducts } from './cityCompanion'

const route = useRoute()
const stations: { name: string; to: string; icon: BrandIconName; note: string }[] = [
  { name: 'AI 懂選', to: '/ai-search', icon: 'purpose', note: '核對需求與用途' },
  { name: '商品', to: '/products', icon: 'cpu', note: '挑選電腦零件' },
  { name: '新增組裝清單', to: '/builds/new', icon: 'custom-build', note: '確認配置與購買品項' },
]
const quietPage = computed(() => /^\/(checkout|login|register|orders|guest-orders)/.test(route.path))
const wide = ref(false)
let media: MediaQueryList | undefined
function resize() { wide.value = media?.matches ?? false }
onMounted(() => {
  loadRecentProducts()
  media = window.matchMedia?.('(min-width: 1580px)')
  resize()
  media?.addEventListener('change', resize)
})
onUnmounted(() => media?.removeEventListener('change', resize))
</script>

<template>
  <div
    v-if="!quietPage"
    class="city-streets"
  >
    <details
      class="city-station"
      :open="wide"
    >
      <summary>
        <span aria-hidden="true">⌁</span> 城市路標 <span
          class="city-streets__chevron"
          aria-hidden="true"
        >⌄</span>
      </summary>
      <nav
        aria-label="電腦城市快速導覽"
        class="city-station__routes"
      >
        <p class="city-station__hello">
          跟著我，找下一站！
        </p>
        <RouterLink
          v-for="(station, index) in stations"
          :key="station.to"
          :to="station.to"
          :class="{ 'city-station__active': route.path.startsWith(station.to) }"
        >
          <span class="city-station__number">0{{ index + 1 }}</span><BrandIcon
            :name="station.icon"
            :size="23"
          /><span><strong>{{ station.name }}</strong><small>{{ station.note }}</small></span>
        </RouterLink>
      </nav>
    </details>
    <details
      v-if="recentProducts.length"
      class="city-pocket"
      :open="wide"
    >
      <summary>
        Donngu 的口袋 <span class="city-pocket__count">{{ recentProducts.length }}</span><span
          class="city-streets__chevron"
          aria-hidden="true"
        >⌄</span>
      </summary>
      <div class="city-pocket__contents">
        <p>剛剛心動的配備，我幫你記著。</p>
        <RouterLink
          v-for="item in recentProducts"
          :key="item.id"
          :to="`/products/${item.id}`"
        >
          <BrandIcon
            name="cpu"
            :size="22"
          /><span>{{ item.name }}</span><span aria-hidden="true">→</span>
        </RouterLink>
        <small>最近瀏覽・僅儲存在此瀏覽器</small>
        <button
          type="button"
          @click="clearRecentProducts"
        >
          清空口袋
        </button>
      </div>
    </details>
  </div>
</template>
