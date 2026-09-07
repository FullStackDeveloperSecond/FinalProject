<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

defineProps<{ displayName?: string }>()
const emit = defineEmits<{ logout: [] }>()

const route = useRoute()
const open = ref(false)
const root = ref<HTMLElement | null>(null)
const trigger = ref<HTMLButtonElement | null>(null)

function close(returnFocus = false): void {
  if (!open.value) return
  open.value = false
  if (returnFocus) trigger.value?.focus()
}

/**
 * 換頁就收起來。監聽 fullPath 而不是 path：選單裡的目的地未來若帶 query，
 * 只換 query 也算導覽，一樣要收合。
 */
watch(() => route.fullPath, () => close())

function onPointerDown(event: PointerEvent): void {
  if (!open.value) return
  if (root.value && !root.value.contains(event.target as Node)) close()
}

function onKeydown(event: KeyboardEvent): void {
  // Escape 關閉並把焦點交回觸發鈕，鍵盤使用者不會掉到頁面開頭
  if (event.key === 'Escape') close(true)
}

onMounted(() => {
  document.addEventListener('pointerdown', onPointerDown)
  document.addEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onPointerDown)
  document.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <div
    ref="root"
    class="member-menu"
  >
    <button
      ref="trigger"
      type="button"
      class="member-menu__trigger"
      :aria-expanded="open"
      aria-haspopup="menu"
      aria-controls="member-menu-panel"
      @click="open = !open"
    >
      <span class="member-menu__name">{{ displayName }}</span>
      <svg
        class="member-menu__chevron"
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2.2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path d="M6 9l6 6 6-6" />
      </svg>
    </button>

    <div
      v-show="open"
      id="member-menu-panel"
      class="member-menu__panel"
    >
      <p class="member-menu__group">
        我的內容
      </p>
      <RouterLink to="/account/builds">
        我的組裝清單
      </RouterLink>
      <RouterLink to="/account/favorites">
        我的收藏
      </RouterLink>
      <RouterLink to="/account/reviews">
        我的評價
      </RouterLink>

      <p class="member-menu__group">
        帳戶設定
      </p>
      <RouterLink to="/account">
        會員資料
      </RouterLink>
      <RouterLink to="/account/addresses">
        收件地址
      </RouterLink>

      <button
        type="button"
        class="member-menu__logout"
        @click="emit('logout')"
      >
        登出
      </button>
    </div>
  </div>
</template>
