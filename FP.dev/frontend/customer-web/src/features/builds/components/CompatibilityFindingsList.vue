<script setup lang="ts">
import type { CompatibilityFindingDto, CompatibilityOverall } from '../types'

defineProps<{
  overall: CompatibilityOverall
  results: CompatibilityFindingDto[]
}>()

const overallLabels: Record<CompatibilityOverall, string> = {
  compatible: '相容',
  warning: '有警告，仍可繼續',
  blocked: '不相容，無法加入購物車',
  insufficientData: '規格資料不足，無法完整判斷',
}

const severityLabels: Record<CompatibilityFindingDto['severity'], string> = {
  compatible: '相容',
  warning: '警告',
  blocked: '不相容',
  insufficientData: '資料不足',
  ruleDisabled: '規則已停用',
}
</script>

<template>
  <section
    class="compat-findings"
    :class="`compat-findings--${overall}`"
    aria-live="polite"
  >
    <p class="compat-findings__overall">
      相容性檢查結果：{{ overallLabels[overall] }}
    </p>
    <ul
      v-if="results.length > 0"
      class="compat-findings__list"
    >
      <li
        v-for="(finding, index) in results"
        :key="`${finding.ruleCode}-${index}`"
        class="compat-findings__item"
        :class="`compat-findings__item--${finding.severity}`"
      >
        <span class="compat-findings__severity">{{ severityLabels[finding.severity] }}</span>
        <span class="compat-findings__rule">{{ finding.ruleCode }}</span>
        <span class="compat-findings__message">{{ finding.messageKey }}</span>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.compat-findings {
  padding: 1rem;
  border-radius: 0.5rem;
  border: 1px solid #e5e7eb;
  background: #f9fafb;
}

.compat-findings--blocked {
  border-color: #fca5a5;
  background: #fef2f2;
}

.compat-findings--warning {
  border-color: #fcd34d;
  background: #fffbeb;
}

.compat-findings--compatible {
  border-color: #86efac;
  background: #f0fdf4;
}

.compat-findings__overall {
  margin: 0 0 0.5rem;
  font-weight: 700;
}

.compat-findings__list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.compat-findings__item {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: baseline;
  font-size: 0.875rem;
}

.compat-findings__severity {
  font-weight: 700;
}

.compat-findings__item--blocked .compat-findings__severity {
  color: #b91c1c;
}

.compat-findings__item--warning .compat-findings__severity {
  color: #92400e;
}

.compat-findings__item--insufficientData .compat-findings__severity,
.compat-findings__item--ruleDisabled .compat-findings__severity {
  color: #4b5563;
}

.compat-findings__rule {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 0.75rem;
  color: #4b5563;
}
</style>
