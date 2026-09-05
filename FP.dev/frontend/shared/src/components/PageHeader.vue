<script setup lang="ts">
import { computed, watchEffect } from 'vue'

interface Breadcrumb {
  label: string
  href?: string
}

const props = withDefaults(defineProps<{
  title: string
  description?: string
  /** Breadcrumb trail. When non-empty, `breadcrumbAriaLabel` must also be supplied. */
  breadcrumbs?: Breadcrumb[]
  /**
   * Accessible name for the breadcrumb <nav>, taken from the host app's locale
   * resources. Required whenever `breadcrumbs` is non-empty — enforced by the
   * runtime guard below so the component never renders an unnamed landmark.
   */
  breadcrumbAriaLabel?: string
}>(), {
  description: undefined,
  breadcrumbs: () => [],
  breadcrumbAriaLabel: undefined,
})

const breadcrumbNavLabel = computed(() =>
  typeof props.breadcrumbAriaLabel === 'string' ? props.breadcrumbAriaLabel.trim() : '',
)

/** Only render the <nav> landmark when it will carry a non-empty accessible name. */
const showBreadcrumbNav = computed(
  () => props.breadcrumbs.length > 0 && breadcrumbNavLabel.value.length > 0,
)

if (import.meta.env.DEV) {
  watchEffect(() => {
    if (props.breadcrumbs.length > 0 && breadcrumbNavLabel.value.length === 0) {
      console.warn(
        '[PageHeader] `breadcrumbs` was provided without a non-empty `breadcrumbAriaLabel`; '
        + 'the breadcrumb <nav> landmark is omitted to avoid an unnamed landmark. '
        + 'Pass a localised `breadcrumbAriaLabel` from the host app.',
      )
    }
  })
}

function isLast(index: number): boolean {
  return index === props.breadcrumbs.length - 1
}
</script>

<template>
  <header class="ds-page-header">
    <nav
      v-if="showBreadcrumbNav"
      class="ds-page-header__breadcrumbs"
      :aria-label="breadcrumbNavLabel"
    >
      <ol>
        <li
          v-for="(crumb, index) in breadcrumbs"
          :key="index"
        >
          <a
            v-if="crumb.href && !isLast(index)"
            :href="crumb.href"
          >{{ crumb.label }}</a>
          <span
            v-else
            :aria-current="isLast(index) ? 'page' : undefined"
          >{{ crumb.label }}</span>
        </li>
      </ol>
    </nav>
    <div class="ds-page-header__bar">
      <div class="ds-page-header__heading">
        <h1 class="ds-page-header__title">
          {{ title }}
        </h1>
        <p
          v-if="description"
          class="ds-page-header__description"
        >
          {{ description }}
        </p>
      </div>
      <div
        v-if="$slots.actions"
        class="ds-page-header__actions"
      >
        <slot name="actions" />
      </div>
    </div>
  </header>
</template>

<style scoped>
.ds-page-header {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding-bottom: var(--space-4);
  border-bottom: 1px solid var(--color-border);
}

.ds-page-header__breadcrumbs ol {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: var(--fs-caption);
  color: var(--color-text-muted);
}

.ds-page-header__breadcrumbs li {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.ds-page-header__breadcrumbs li + li::before {
  content: "/";
  color: var(--color-text-faint);
}

.ds-page-header__breadcrumbs a {
  color: var(--color-text-muted);
  text-decoration: none;
}

.ds-page-header__breadcrumbs a:hover {
  color: var(--color-primary-dark);
  text-decoration: underline;
}

.ds-page-header__bar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
}

.ds-page-header__heading {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  min-width: 0;
}

.ds-page-header__title {
  margin: 0;
  font-size: var(--fs-h1);
  line-height: var(--lh-heading);
  color: var(--color-text);
}

.ds-page-header__description {
  margin: 0;
  max-width: 60ch;
  font-size: var(--fs-body);
  color: var(--color-text-muted);
}

.ds-page-header__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
</style>
