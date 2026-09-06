<script setup lang="ts">
type StatusKind = 'in-progress' | 'waiting' | 'complete' | 'failed' | 'stopped'

defineProps<{
  /**
   * Visual semantics only. This component ships no wording of its own —
   * `status` never selects the text.
   */
  status: StatusKind
  /**
   * Domain label, supplied from the host app's locale resources. Required as the
   * stable fallback; the default slot may override the rendered content.
   */
  label: string
}>()
</script>

<template>
  <span
    class="ds-status-badge"
    :class="`ds-status-badge--${status}`"
  >
    <slot>{{ label }}</slot>
  </span>
</template>

<style scoped>
/*
  Every modifier maps text / background / border to explicit semantic tokens.
  No shared color-mix() border: each state states its own border token,
  and success uses its own --color-success-border (distinct from --color-success-bg).
*/
.ds-status-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  padding: var(--space-1) var(--space-3);
  border: 1px solid transparent;
  border-radius: 999px;
  font-size: var(--fs-caption);
  font-weight: 500;
  line-height: 1.4;
  white-space: nowrap;
}

.ds-status-badge--in-progress {
  color: var(--color-info);
  background: var(--color-info-bg);
  border-color: var(--color-info-border);
}

.ds-status-badge--waiting {
  color: var(--color-navy);
  background: var(--color-butter-soft);
  border-color: var(--color-butter-line);
}

.ds-status-badge--complete {
  color: var(--color-primary-dark);
  background: var(--color-success-bg);
  border-color: var(--color-success-border);
}

.ds-status-badge--failed {
  color: var(--color-danger);
  background: var(--color-danger-bg);
  border-color: var(--color-danger-border);
}

.ds-status-badge--stopped {
  color: var(--color-text-muted);
  background: var(--color-surface-strong);
  border-color: var(--color-border);
}
</style>
