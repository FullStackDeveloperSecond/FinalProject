<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import gsap from 'gsap'

const router = useRouter()
const scene = ref<HTMLElement | null>(null)
const canExplore = ref(false)

onMounted(async () => {
  await nextTick()
  if (!scene.value) return
  const timeline = gsap.timeline({ defaults: { ease: 'power3.out' } })
  timeline
    .from('.welcome-page__city', { scale: 1.12, opacity: 0, duration: 1.3 })
    .from('.welcome-page__grid', { opacity: 0, y: 18, duration: 0.7 }, '-=0.6')
    .from('.welcome-page__logo-part--tl', { x: -120, y: -80, rotate: -18, opacity: 0, duration: 0.65 }, '-=0.35')
    .from('.welcome-page__logo-part--tr', { x: 120, y: -80, rotate: 18, opacity: 0, duration: 0.65 }, '<')
    .from('.welcome-page__logo-part--bl', { x: -120, y: 80, rotate: 18, opacity: 0, duration: 0.65 }, '<')
    .from('.welcome-page__logo-part--br', { x: 120, y: 80, rotate: -18, opacity: 0, duration: 0.65 }, '<')
    .from('.welcome-page__logo-glow', { scale: 0.55, opacity: 0, duration: 0.7 }, '-=0.35')
    .from('.welcome-page__scanline', { scaleY: 0, opacity: 0, duration: 0.35, transformOrigin: '50% 0%' }, '-=0.1')
    .from('.welcome-page__copy', { y: 22, opacity: 0, duration: 0.55 }, '-=0.2')
    .add(() => { canExplore.value = true })
  gsap.to('.welcome-page__city', { filter: 'brightness(1) saturate(1)', duration: 1.25, delay: 3.8, ease: 'power2.out' })
})

function explore(): void {
  const transition = gsap.timeline({ onComplete: () => void router.push('/') })
  transition.to('.welcome-page__explore', { scale: 0.985, opacity: 0.8, duration: 0.22 })
    .to('.welcome-page__portal', { scale: 2.5, opacity: 0.2, duration: 1.65, ease: 'power1.inOut' }, '-=0.02')
    .to('.welcome-page', { opacity: 0.08, duration: 1.2, ease: 'power1.inOut' }, '-=1.05')
}
</script>

<template>
  <main
    ref="scene"
    class="welcome-page"
    aria-labelledby="welcome-title"
  >
    <div
      class="welcome-page__city"
      aria-hidden="true"
    />
    <div
      class="welcome-page__grid"
      aria-hidden="true"
    />
    <div class="welcome-page__content">
      <p class="welcome-page__eyebrow">
        DOSELECT COMPUTER CITY · CITY GATE
      </p>
      <div
        class="welcome-page__logo"
        aria-label="DoSelect 懂選商標"
      >
        <span
          class="welcome-page__logo-glow"
          aria-hidden="true"
        />
        <img
          class="welcome-page__logo-part welcome-page__logo-part--tl"
          src="/brand/doselect-logo-city.png"
          alt=""
        >
        <img
          class="welcome-page__logo-part welcome-page__logo-part--tr"
          src="/brand/doselect-logo-city.png"
          alt=""
        >
        <img
          class="welcome-page__logo-part welcome-page__logo-part--bl"
          src="/brand/doselect-logo-city.png"
          alt=""
        >
        <img
          class="welcome-page__logo-part welcome-page__logo-part--br"
          src="/brand/doselect-logo-city.png"
          alt=""
        >
        <span
          class="welcome-page__scanline"
          aria-hidden="true"
        />
      </div>
      <h1 id="welcome-title">
        歡迎來到懂選電腦城
      </h1>
      <p class="welcome-page__copy">
        Donngu 已經在城市入口等你，準備陪你找到下一台適合的電腦。
      </p>
      <button
        class="welcome-page__explore"
        type="button"
        :disabled="!canExplore"
        @click="explore"
      >
        開始探索 <span aria-hidden="true">→</span>
      </button>
      <p class="welcome-page__hint">
        點擊品牌標誌即可再次回到城市入口
      </p>
    </div>
    <span
      class="welcome-page__portal"
      aria-hidden="true"
    />
  </main>
</template>
