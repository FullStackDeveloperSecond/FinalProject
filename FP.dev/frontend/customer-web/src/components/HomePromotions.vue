<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { homePromotions as slides } from './homePromotions'

const active = ref(0)
const paused = ref(false)
const hovered = ref(false)
const focused = ref(false)
const reduced = ref(false)
const slide = computed(() => slides[active.value]!)
let timer: ReturnType<typeof setInterval> | undefined
let media: MediaQueryList | undefined
function preference() { reduced.value = media?.matches ?? false }
function select(index: number) {
  active.value = (index + slides.length) % slides.length
}
onMounted(() => {
  media = window.matchMedia?.('(prefers-reduced-motion: reduce)')
  preference()
  media?.addEventListener('change', preference)
  timer = setInterval(() => {
    if (!paused.value && !hovered.value && !focused.value && !reduced.value && !document.hidden) select(active.value + 1)
  }, 6500)
})
onUnmounted(() => {
  clearInterval(timer)
  media?.removeEventListener('change', preference)
})
function leaveFocus(event: FocusEvent) {
  focused.value = (event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null)
}
</script>

<template>
  <section
    class="home-promotions"
    aria-label="懂選城市快報輪播"
    aria-roledescription="輪播"
    @mouseenter="hovered = true"
    @mouseleave="hovered = false"
    @focusin="focused = true"
    @focusout="leaveFocus"
  >
    <div class="home-promotions__top">
      <span>懂選城市快報</span><span>{{ active + 1 }} / {{ slides.length }}</span>
    </div>
    <div class="home-promotions__stage">
      <Transition
        name="promotion-slide"
        mode="out-in"
      >
        <article
          :key="active"
          class="home-promotions__slide"
          :class="`home-promotions__slide--${slide.theme}`"
          aria-roledescription="投影片"
          :aria-label="`${active + 1} / ${slides.length}`"
        >
          <div>
            <span class="home-promotions__eyebrow">{{ slide.label }}</span><h2>{{ slide.title }}</h2><p>{{ slide.body }}</p><RouterLink :to="slide.to">
              {{ slide.action }} <span aria-hidden="true">→</span>
            </RouterLink>
          </div>
          <div
            class="home-promotions__art"
            aria-hidden="true"
          >
            <span>Do<br>Select.</span>
          </div>
        </article>
      </Transition>
    </div>
    <div class="home-promotions__controls">
      <button
        type="button"
        aria-label="上一則廣告"
        @click="select(active - 1)"
      >
        ←
      </button>
      <div class="home-promotions__dots">
        <button
          v-for="(item, index) in slides"
          :key="item.label"
          type="button"
          :aria-label="`顯示第 ${index + 1} 則廣告：${item.title}`"
          :aria-current="active === index ? 'true' : undefined"
          @click="select(index)"
        >
          <span />
        </button>
      </div>
      <button
        type="button"
        aria-label="下一則廣告"
        @click="select(active + 1)"
      >
        →
      </button>
      <button
        v-if="!reduced"
        type="button"
        class="home-promotions__pause"
        :aria-pressed="paused"
        @click="paused = !paused"
      >
        {{ paused ? '播放輪播' : '暫停輪播' }}
      </button>
      <span
        v-else
        class="home-promotions__pause"
      >手動切換</span>
    </div>
  </section>
</template>
