<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { BrandIcon, type BrandIconName } from '@doselect/web-shared/components'
import { clearRecentProducts, loadRecentProducts, recentProducts } from './cityCompanion'

const route = useRoute()
const avatar = `${import.meta.env.BASE_URL}brand/donggu-hero-wave.png`
const stations: { name: string; to: string; icon: BrandIconName; note: string }[] = [
  { name: '靈感站', to: '/ai-search', icon: 'purpose', note: '說說你的用途' },
  { name: '零件街', to: '/products', icon: 'cpu', note: '探索電腦配備' },
  { name: '組裝所', to: '/builds/new', icon: 'custom-build', note: '搭建理想電腦' },
  { name: '補給站', to: '/support', icon: 'recommend', note: '找人幫個忙' },
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
        <div
          class="city-station__host"
          aria-hidden="true"
        >
          <img
            :src="avatar"
            alt=""
            width="60"
            height="88"
          ><span>DONNGU<br>城市嚮導</span>
        </div>
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
    <div
      class="city-streets__scene"
      aria-hidden="true"
    >
      <span class="city-streets__scene-label">NEXT STOP / YOUR NEXT PC</span><div class="city-streets__buildings">
        <i /><i /><i />
      </div><div class="city-streets__courier">
        <img
          :src="avatar"
          alt=""
          width="48"
          height="70"
        ><span>零件補給中</span>
      </div><div class="city-streets__road" />
    </div>
  </div>
</template>
