<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import gsap from 'gsap'
import { useMotionScope } from '@doselect/web-shared/motion'

const router = useRouter()
const assetBase = import.meta.env.BASE_URL
const scene = ref<HTMLElement | null>(null)
const canExplore = ref(false)
const transitioning = ref(false)
const introMotion = useMotionScope(scene)
const exitMotion = useMotionScope(scene)

onMounted(async () => {
  await nextTick()
  if (!scene.value) return
  introMotion.run(({ reducedMotion }) => {
    if (reducedMotion) {
      canExplore.value = true
      return
    }

    const timeline = gsap.timeline({ defaults: { ease: 'power3.out' } })
    timeline
      .from('.welcome-page__city', { scale: 1.12, opacity: 0, duration: 1.3 })
      .from('.welcome-page__grid', { opacity: 0, y: 18, duration: 0.7 }, '-=0.6')
      .from('.welcome-page__logo-part--tl', { x: -120, y: -80, rotate: -18, opacity: 0, duration: 0.65 }, '-=0.35')
      .from('.welcome-page__logo-part--tr', { x: 120, y: -80, rotate: 18, opacity: 0, duration: 0.65 }, '<')
      .from('.welcome-page__logo-part--bl', { x: -120, y: 80, rotate: 18, opacity: 0, duration: 0.65 }, '<')
      .from('.welcome-page__logo-part--br', { x: 120, y: 80, rotate: -18, opacity: 0, duration: 0.65 }, '<')
      .from('.welcome-page__logo-glow', { scale: 0.55, opacity: 0, duration: 0.7 }, '-=0.35')
      .fromTo(
        '.welcome-page__scanline',
        { scaleY: 0, opacity: 0 },
        { scaleY: 1, opacity: 0.85, duration: 0.25, transformOrigin: '50% 0%' },
        '-=0.1',
      )
      .to('.welcome-page__scanline', { opacity: 0, duration: 0.2 })
      .from('.welcome-page__copy', { y: 22, opacity: 0, duration: 0.55 }, '-=0.2')
      .to('.welcome-page__city', { filter: 'brightness(1) saturate(1)', duration: 0.45 }, '-=0.45')
      .add(() => { canExplore.value = true })
  })
})

function explore(): void {
  if (transitioning.value) return
  transitioning.value = true
  introMotion.revert()
  exitMotion.run(({ reducedMotion }) => {
    if (reducedMotion) {
      void router.push('/')
      return
    }

    const transition = gsap.timeline({ onComplete: () => void router.push('/') })
    transition.to('.welcome-page__explore', { scale: 0.985, opacity: 0.8, duration: 0.08 })
      .to('.welcome-page__portal', { scale: 6, opacity: 1, duration: 0.42, ease: 'power1.inOut' }, '<')
      .to(scene.value, { opacity: 0.08, duration: 0.2, ease: 'power1.inOut' }, '-=0.2')
  })
}
</script>

<template>
  <section
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
          :src="`${assetBase}brand/doselect-logo-city.png`"
          alt=""
        >
        <img
          class="welcome-page__logo-part welcome-page__logo-part--tr"
          :src="`${assetBase}brand/doselect-logo-city.png`"
          alt=""
        >
        <img
          class="welcome-page__logo-part welcome-page__logo-part--bl"
          :src="`${assetBase}brand/doselect-logo-city.png`"
          alt=""
        >
        <img
          class="welcome-page__logo-part welcome-page__logo-part--br"
          :src="`${assetBase}brand/doselect-logo-city.png`"
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
        :disabled="!canExplore || transitioning"
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
  </section>
</template>
