<script setup lang="ts">
import { ref } from 'vue'
import { motionPresetIds, motionPresets, type MotionPresetId } from './presets'
import { isMotionExplorationEnabled } from './useMotionPresetSelection'

/**
 * A／B／C 方案切換器 —— **開發限定**。
 *
 * `isMotionExplorationEnabled` 在 production build 是常數 false，
 * 整個 template 因此被 v-if 掉，Rollup 會把這個元件與字串一起移除。
 * 正式產品不會出現任何「動畫模式選單」。
 */
defineProps<{
  presetId: MotionPresetId
  reducedMotion?: boolean
}>()

const expanded = ref(false)
const emit = defineEmits<{ select: [MotionPresetId] }>()
</script>

<template>
  <aside
    v-if="isMotionExplorationEnabled"
    class="motion-dev-switcher"
    data-motion-dev-switcher
    aria-label="動態方案切換（開發限定）"
  >
    <button
      type="button"
      class="motion-dev-switcher__title"
      :aria-expanded="expanded"
      aria-controls="motion-dev-options"
      @click="expanded = !expanded"
    >
      動態方案（開發限定）
    </button>
    <div
      v-show="expanded"
      id="motion-dev-options"
      class="motion-dev-switcher__options"
    >
      <button
        v-for="id in motionPresetIds"
        :key="id"
        type="button"
        class="motion-dev-switcher__option"
        :aria-pressed="presetId === id"
        @click="emit('select', id)"
      >
        {{ motionPresets[id].label }}
      </button>
    </div>
    <p
      v-if="reducedMotion && expanded"
      class="motion-dev-switcher__note"
    >
      系統偏好為減少動態，目前不建立任何位移／縮放動畫。
    </p>
  </aside>
</template>

<style scoped>
.motion-dev-switcher {
  position: fixed;
  right: var(--space-4);
  bottom: var(--space-4);
  z-index: var(--z-toast);
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  max-width: 15rem;
  padding: var(--space-3);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-md);
  font-size: var(--fs-caption);
}

.motion-dev-switcher__title {
  margin: 0;
  font-weight: 700;
  color: var(--color-text-muted);
}

.motion-dev-switcher__options {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.motion-dev-switcher__option {
  padding: var(--space-1) var(--space-2);
  font: inherit;
  text-align: left;
  color: var(--color-text);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  cursor: pointer;
}

.motion-dev-switcher__option[aria-pressed='true'] {
  color: var(--color-on-primary);
  background: var(--color-primary);
  border-color: var(--color-primary);
}

.motion-dev-switcher__option:focus-visible {
  outline: 2px solid var(--color-focus-ring);
  outline-offset: 2px;
}

.motion-dev-switcher__note {
  margin: 0;
  color: var(--color-text-muted);
}
</style>
