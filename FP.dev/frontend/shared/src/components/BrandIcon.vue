<script setup lang="ts">
import { computed } from 'vue'

/**
 * DoSelect 導覽圖示（code-native SVG，單一一致風格）。
 *
 * 正式素材庫（D:\期末小組電商網站\DoSelect正式素材）中沒有成組的導覽圖示 ——
 * 23 個檔案全是 0.86–2.11 MB 的吉祥物插畫、Logo 變體與情境場景圖，
 * 把它們縮成 32–56px 的小圖示會糊掉也會混用不同視覺風格。
 * 依素材使用原則第 4 條，這裡改用同一套 code-native SVG，不新增任何圖示套件。
 *
 * 一致性規則（全部十個圖示共用）：
 * - viewBox 一律 `0 0 24 24`
 * - 只用 stroke，`stroke-width: 1.75`、round cap/join，不使用 fill
 * - 顏色一律 `currentColor`，由呼叫端以色彩 Token 決定
 *
 * 預設為純裝飾（`aria-hidden="true"`）：卡片本身已有文字標題，
 * 圖示不重複朗讀。若圖示要承載額外資訊，傳入 `label` 取得可存取名稱。
 */

export type BrandIconName =
  | 'purpose'
  | 'budget'
  | 'recommend'
  | 'cpu'
  | 'motherboard'
  | 'memory'
  | 'gpu'
  | 'storage'
  | 'case'
  | 'custom-build'

const props = withDefaults(defineProps<{
  name: BrandIconName
  /** 有值時圖示成為 img 角色並取得可存取名稱；預設為裝飾性。 */
  label?: string
  /** 顯示尺寸（px）。 */
  size?: number
}>(), { label: undefined, size: 24 })

/**
 * 圖示路徑集中管理，template 內不做條件判斷。
 * 分類圖示對應正式 catalog code（CPU／MOTHERBOARD／MEMORY／GPU／STORAGE／CASE），
 * 名稱與 CompatibilityCatalogContract.Categories 一致，避免圖示與查詢條件各說各話。
 * 型別上 `name` 只能是 BrandIconName，因此不存在未知名稱；
 * 執行期仍保留 fallback，避免 JS 呼叫端傳入非法值時整頁壞掉。
 */
const ICON_PATHS: Record<BrandIconName, string[]> = {
  // 說用途：準星＋對話，代表「說出你要拿來做什麼」
  purpose: [
    'M12 3.5a8.5 8.5 0 1 0 8.5 8.5',
    'M12 7.5a4.5 4.5 0 1 0 4.5 4.5',
    'M12 12 20.5 3.5',
    'M17.5 3.5h3v3',
  ],
  // 給預算：錢包＋金額扣環
  budget: [
    'M4 7.5A2.5 2.5 0 0 1 6.5 5H17a1 1 0 0 1 1 1v1.5',
    'M4 7.5v9A2.5 2.5 0 0 0 6.5 19H18a2 2 0 0 0 2-2v-7a2 2 0 0 0-2-2H6.5',
    'M16.5 13.5h.01',
  ],
  // 看推薦：清單＋勾選＋亮點
  recommend: [
    'M5 5.5h9',
    'M5 10h9',
    'M5 14.5h5.5',
    'M13.5 17.5l2 2 4.5-4.5',
    'M18.5 4.5l.7 1.8 1.8.7-1.8.7-.7 1.8-.7-1.8-1.8-.7 1.8-.7z',
  ],
  // CPU：晶片本體＋接腳
  cpu: [
    'M8 8h8v8H8z',
    'M6 6.5A.5.5 0 0 1 6.5 6h11a.5.5 0 0 1 .5.5v11a.5.5 0 0 1-.5.5h-11a.5.5 0 0 1-.5-.5z',
    'M9.5 3.5V6M14.5 3.5V6M9.5 18v2.5M14.5 18v2.5',
    'M3.5 9.5H6M3.5 14.5H6M18 9.5h2.5M18 14.5h2.5',
  ],
  // 主機板：板子＋插槽＋晶片座
  motherboard: [
    'M4 4.5h16a.5.5 0 0 1 .5.5v14a.5.5 0 0 1-.5.5H4a.5.5 0 0 1-.5-.5V5a.5.5 0 0 1 .5-.5z',
    'M7 8h5v5H7z',
    'M15 8h2.5M15 11h2.5M15 14h2.5',
    'M7 16h5',
  ],
  // 記憶體：模組＋金手指
  memory: [
    'M3.5 6.5h17a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1h-17a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z',
    'M6 9.5v3M9.5 9.5v3M14.5 9.5v3M18 9.5v3',
    'M6.5 15.5v2M17.5 15.5v2',
  ],
  // 顯示卡：卡板＋雙風扇
  gpu: [
    'M3 6.5h16.5a1.5 1.5 0 0 1 1.5 1.5v7a1.5 1.5 0 0 1-1.5 1.5H3z',
    'M8 8.5a3 3 0 1 0 0 6 3 3 0 0 0 0-6z',
    'M16 9.5a2 2 0 1 0 0 4 2 2 0 0 0 0-4z',
    'M5.5 17.5v2M13 17.5v2',
  ],
  // 儲存裝置：碟片堆疊＋活動指示
  storage: [
    'M4 6.5A1.5 1.5 0 0 1 5.5 5h13A1.5 1.5 0 0 1 20 6.5v11a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5z',
    'M4 12h16',
    'M7.5 8.5h.01M7.5 15.5h.01',
    'M16.5 15.5h-4',
  ],
  // 機殼：直立主機＋前面板
  case: [
    'M7 3.5h10a1.5 1.5 0 0 1 1.5 1.5v14a1.5 1.5 0 0 1-1.5 1.5H7A1.5 1.5 0 0 1 5.5 19V5A1.5 1.5 0 0 1 7 3.5z',
    'M9 7h6',
    'M9 10.5h6',
    'M9.5 17h1.5',
  ],
  // 自由組裝：扳手＋螺絲起子交叉
  'custom-build': [
    'M14.5 6.5a3.5 3.5 0 0 0 4.6 4.6l-8 8a2.2 2.2 0 0 1-3.1-3.1z',
    'M6.5 4.5l3 3-2 2-3-3z',
    'M5.5 6.5 3.5 8.5',
  ],
}

const paths = computed(() => ICON_PATHS[props.name] ?? ICON_PATHS.purpose)
const decorative = computed(() => props.label === undefined)
</script>

<template>
  <svg
    class="brand-icon"
    viewBox="0 0 24 24"
    :width="size"
    :height="size"
    fill="none"
    stroke="currentColor"
    stroke-width="1.75"
    stroke-linecap="round"
    stroke-linejoin="round"
    :aria-hidden="decorative ? 'true' : undefined"
    :role="decorative ? undefined : 'img'"
    focusable="false"
  >
    <title v-if="!decorative">{{ label }}</title>
    <path
      v-for="d in paths"
      :key="d"
      :d="d"
    />
  </svg>
</template>

<style scoped>
.brand-icon {
  display: block;
  flex: none;
  /* forced-colors（Windows 高對比）下改用系統文字色，確保仍可辨識 */
  forced-color-adjust: auto;
}

@media (forced-colors: active) {
  .brand-icon {
    stroke: CanvasText;
  }
}
</style>
