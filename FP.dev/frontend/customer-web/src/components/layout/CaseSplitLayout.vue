<script setup lang="ts">
/**
 * Customer App 專屬的案件並排版面。
 *
 * 桌面：列表與詳細同層並排，列表約 2/5、詳細約 3/5，詳細不覆蓋列表。
 * 窄畫面：改為上下堆疊，詳細排在列表下方 —— 不使用遮罩式 drawer 或 modal。
 * 詳細只由右上角的關閉按鈕收起。
 *
 * 這是頁面層的暫用版面，刻意不叫 `SplitPanel`，也不放進 shared，
 * 以免與尚未通過規格驗收的共用元件同名或卡住日後替換。
 * 本元件不含任何 user-facing 文案，標題與關閉按鈕名稱皆由呼叫端傳入。
 */
import { ref } from 'vue'
import {
  createPanelEnter,
  createPanelLeave,
  detectReducedMotion,
  useSensitiveMotionPreset,
} from '@doselect/web-shared/motion'

const props = withDefaults(defineProps<{
  /** 是否展開詳細欄。 */
  detailOpen?: boolean
  /** 詳細區標題，由呼叫端以自己的語系文字提供。 */
  detailTitle?: string
  /** 關閉按鈕的可及名稱，由呼叫端提供。 */
  closeLabel?: string
}>(), {
  detailOpen: false,
  detailTitle: undefined,
  closeLabel: undefined,
})

const emit = defineEmits<{
  close: []
}>()

// 客服案件屬售後流程：固定使用 Gentle Guidance，不隨前台 Donggu 的 overshoot。
const preset = useSensitiveMotionPreset()

// 展開／收起期間都維持 data-detail-open="true"，讓 grid 欄寬在動畫全程保持 2fr : 3fr。
// 列表因此不會在收起的瞬間被推寬，也不會被詳細欄蓋住。
const detailPresent = ref(props.detailOpen)

// 只動 opacity / x / scale：不動 width、不動 grid-template-columns。
function onDetailEnter(element: Element, done: () => void): void {
  detailPresent.value = true
  const reducedMotion = detectReducedMotion()
  const tween = createPanelEnter(element, preset.value, { reducedMotion, onComplete: done })
  if (!tween) {
    done()
  }
}

function onDetailLeave(element: Element, done: () => void): void {
  const reducedMotion = detectReducedMotion()
  createPanelLeave(element, preset.value, {
    reducedMotion,
    onComplete: () => {
      detailPresent.value = false
      done()
    },
  })
}
</script>

<template>
  <div
    class="case-split"
    :data-detail-open="detailOpen || detailPresent"
  >
    <div class="case-split__list">
      <slot name="list" />
    </div>

    <Transition
      :css="false"
      @enter="onDetailEnter"
      @leave="onDetailLeave"
    >
      <section
        v-if="detailOpen"
        class="case-split__detail"
        :aria-label="detailTitle"
      >
        <header class="case-split__detail-head">
          <div class="case-split__detail-heading">
            <slot name="detail-heading">
              <h2 class="case-split__detail-title">
                {{ detailTitle }}
              </h2>
            </slot>
          </div>
          <button
            type="button"
            class="case-split__close"
            :aria-label="closeLabel"
            @click="emit('close')"
          >
            <span aria-hidden="true">×</span>
          </button>
        </header>
        <div class="case-split__detail-body">
          <slot name="detail" />
        </div>
      </section>
    </Transition>
  </div>
</template>

<style scoped>
.case-split {
  display: grid;
  grid-template-columns: 1fr;
  gap: var(--space-5);
  align-items: start;
}

/* 桌面：列表 2/5、詳細 3/5，同層並排，詳細不覆蓋列表。 */
@media (min-width: 1024px) {
  .case-split[data-detail-open='true'] {
    grid-template-columns: 2fr 3fr;
  }
}

/*
 * 列表欄宣告成 container：詳細面板一開，這一欄就只剩約 2/5 寬，
 * 裡面的高密度表格必須依「自己的寬度」而不是視窗寬度切換版型
 * （參考圖 05 的案件列表就是窄欄裡的卡片式清單）。
 * 對應的 @container 規則寫在 SupportTicketListPage.vue。
 */
.case-split__list {
  min-width: 0;
  container-type: inline-size;
  container-name: case-list;
}

.case-split__detail {
  min-width: 0;
  display: flex;
  flex-direction: column;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-sm);
  overflow: hidden;
}

@media (min-width: 1024px) {
  .case-split__detail {
    position: sticky;
    top: calc(var(--header-h) + var(--space-4));
    max-height: calc(100vh - var(--header-h) - var(--space-6));
  }
}

.case-split__detail-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  padding: var(--space-4) var(--space-5);
  background: var(--color-surface-strong);
  border-bottom: 1px solid var(--color-border);
}

.case-split__detail-heading {
  min-width: 0;
}

.case-split__detail-title {
  margin: 0;
  font-size: var(--fs-h3);
  line-height: var(--lh-heading);
  color: var(--color-text);
}

.case-split__close {
  flex: none;
  width: 32px;
  height: 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: var(--fs-h2);
  line-height: 1;
  color: var(--color-text-muted);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: color var(--dur-micro) var(--ease-out),
    border-color var(--dur-micro) var(--ease-out);
}

.case-split__close:hover {
  color: var(--color-text);
  border-color: var(--color-text-muted);
}

.case-split__close:focus-visible {
  outline: 2px solid var(--color-focus-ring);
  outline-offset: 2px;
}

.case-split__detail-body {
  padding: var(--space-5);
  overflow-y: auto;
}
</style>
