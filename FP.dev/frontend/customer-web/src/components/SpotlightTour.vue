<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'

const emit = defineEmits<{ close: [] }>()
const router = useRouter()
const assetBase = import.meta.env.BASE_URL
const dialog = ref<HTMLDialogElement | null>(null)
const step = ref(0)
const box = ref({ x: 0, y: 0, width: 0, height: 0 })
const viewport = ref({ width: window.innerWidth, height: window.innerHeight })
const card = ref<HTMLElement | null>(null)
const previousFocus = document.activeElement as HTMLElement | null
const steps = [
  { title: '第一站：說用途', text: '先選這張卡片，告訴 AI 你想打遊戲、做創作或日常使用。不用先認識零件規格。', to: '/ai-search', action: '前往說用途', pose: 'point' },
  { title: '第二站：給預算', text: '這張卡片會帶你到商品頁。設定最低價與最高價，把搜尋範圍縮小到自己的預算。', to: '/products', action: '前往設定預算', pose: 'laptop' },
  { title: '第三站：看推薦', text: '把用途與預算一起交給 AI 懂選，再確認推薦理由與相容性。準備好就從這裡開始！', to: '/ai-search', action: '前往看推薦', pose: 'checklist' },
]
const current = computed(() => steps[step.value]!)
function measure() {
  viewport.value = { width: window.innerWidth, height: window.innerHeight }
  const target = document.querySelectorAll('.home-step')[step.value]
  if (!target) return
  const r = target.getBoundingClientRect()
  box.value = { x: r.left - 6, y: r.top - 6, width: r.width + 12, height: r.height + 12 }
}
const shade = computed(() => {
  const { width: w, height: h } = viewport.value
  const b = box.value
  return `M0 0H${w}V${h}H0Z M${b.x} ${b.y}v${b.height}h${b.width}v-${b.height}Z`
})
const position = computed(() => {
  const b = box.value, v = viewport.value
  const width = Math.min(350, v.width - 32)
  const height = card.value?.offsetHeight ?? 290
  const below = b.y + b.height + 18
  return { width: `${width}px`, left: `${Math.max(16, Math.min(b.x, v.width - width - 16))}px`, top: `${Math.max(64, Math.min(below + height <= v.height - 100 ? below : b.y - height - 18, v.height - height - 100))}px` }
})
async function locate() {
  await nextTick()
  document.querySelectorAll('.home-step')[step.value]?.scrollIntoView({ block: 'start', behavior: 'instant' })
  window.scrollBy({ top: -90, behavior: 'instant' })
  measure()
}
async function change(amount: number) { step.value += amount; await locate() }
function close() { emit('close') }

onMounted(async () => {
  await router.push('/')
  await locate()
  dialog.value?.showModal()
  await nextTick()
  measure()
  window.addEventListener('resize', measure)
  window.addEventListener('scroll', measure, true)
})
onUnmounted(() => {
  window.removeEventListener('resize', measure)
  window.removeEventListener('scroll', measure, true)
  dialog.value?.close()
  if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true })
})
</script>

<template>
  <dialog
    ref="dialog"
    class="spotlight-tour"
    aria-label="Donngu 新手導覽"
    @cancel.prevent="close"
  >
    <svg
      class="spotlight-tour__shade"
      aria-hidden="true"
      :viewBox="`0 0 ${viewport.width} ${viewport.height}`"
      preserveAspectRatio="none"
    ><path
      :d="shade"
      fill-rule="evenodd"
    /></svg>
    <div
      class="spotlight-tour__ring"
      :style="{ left: `${box.x}px`, top: `${box.y}px`, width: `${box.width}px`, height: `${box.height}px` }"
    />
    <button
      class="spotlight-tour__exit"
      type="button"
      aria-label="關閉新手導覽"
      @click="close"
    >
      結束導覽 ×
    </button>
    <section
      ref="card"
      class="spotlight-tour__card"
      :style="position"
      aria-live="polite"
    >
      <img
        :src="`${assetBase}brand/donngu-${current.pose}.png`"
        alt=""
        width="64"
        height="88"
      >
      <span class="spotlight-tour__progress">DONNGU 帶你逛城市 · {{ step + 1 }} / 3</span>
      <h2>{{ current.title }}</h2><p>{{ current.text }}</p>
    </section>
    <div class="spotlight-tour__controls">
      <button
        :disabled="step === 0"
        type="button"
        @click="change(-1)"
      >
        上一站
      </button><span>{{ step + 1 }} / 3</span><button
        v-if="step < 2"
        type="button"
        @click="change(1)"
      >
        下一站 →
      </button><button
        v-else
        type="button"
        @click="close"
      >
        完成導覽
      </button>
    </div>
  </dialog>
</template>

<style>
.spotlight-tour { position: fixed; inset: 0; width: 100vw; height: 100dvh; max-width: none; max-height: none; padding: 0; margin: 0; border: 0; background: transparent; overflow: hidden; color: #174750; }
.spotlight-tour::backdrop { background: transparent; }
.spotlight-tour__shade { position: absolute; inset: 0; width: 100%; height: 100%; fill: #061c2bd1; }
.spotlight-tour__ring { position: absolute; border: 3px solid #d9fa9b; border-radius: 20px; box-shadow: 0 0 20px #b9f9b566; pointer-events: none; }
.spotlight-tour__exit { position: absolute; right: 18px; top: 16px; padding: 12px 18px; background: #fff; border: 0; border-radius: 30px; color: #214e54; cursor: pointer; }
.spotlight-tour__card { position: absolute; box-sizing: border-box; padding: 20px; border: 2px solid #a6c9a3; border-radius: 20px; background: #fbfff5; box-shadow: 0 14px 40px #061c2b55; max-height: calc(100dvh - 88px); overflow-y: auto; }
.spotlight-tour__card img { float: right; object-fit: contain; margin: -5px -6px 4px 8px; border-radius: 12px; }
.spotlight-tour__progress { font-size: 10px; color: #4b7264; }
.spotlight-tour__card h2 { font-size: 21px; margin: 12px 0; }
.spotlight-tour__card p { font-size: 14px; line-height: 1.7; margin: 0 0 15px; }
.spotlight-tour__controls button { min-height: 40px; cursor: pointer; border: 1px solid #94b7a0; border-radius: 8px; padding: 8px 10px; color: #204f44; background: #e5f5d8; }
.spotlight-tour__visit { width: 100%; }
.spotlight-tour__controls { position: absolute; inset: auto 0 0; display: flex; justify-content: space-between; align-items: center; padding: 16px max(20px, env(safe-area-inset-left)); padding-bottom: max(16px, env(safe-area-inset-bottom)); gap: 16px; font-size: 16px; color: #fff; background: #123840; }
.spotlight-tour__controls button:disabled { opacity: .45; cursor: default; }
.spotlight-tour button:focus-visible { outline: 3px solid #168b93; outline-offset: 3px; }
</style>
