<script setup lang="ts">
withDefaults(defineProps<{ name: string; size?: number }>(), { size: 22 })
const paths: Record<string, string> = {
  home: 'M3 10 12 3l9 7M5 9v12h5v-7h4v7h5V9',
  support: 'M4 13v-2a8 8 0 0 1 16 0v2M4 12H2v6h4v-6ZM20 12h2v6h-4v-6ZM20 18v3h-7',
  box: 'm3 7 9-4 9 4v10l-9 4-9-4ZM3 7l9 4 9-4M12 11v10M7 5l10 4',
  warehouse: 'M3 21V8l9-5 9 5v13M7 21V11h10v10M7 15h10M7 18h10',
  truck: 'M2 5h12v12H2ZM14 10h4l4 5v2h-8M6 17a2 2 0 1 0 0 4 2 2 0 0 0 0-4M18 17a2 2 0 1 0 0 4 2 2 0 0 0 0-4',
  chart: 'M3 3v18h18M7 16v-5M12 16V7M17 16V4',
  wallet: 'M3 6h17v15H3ZM3 6V3h14v3M16 11h6v5h-6ZM18 13h1',
  tag: 'M3 3h8l10 10-8 8L3 11ZM7 7h.01',
  settings: 'M4 7h16M4 17h16M8 4v6M16 14v6',
  document: 'M5 3h10l4 4v14H5ZM14 3v5h5M8 12h8M8 16h6',
  upload: 'M12 16V3M7 8l5-5 5 5M4 15v6h16v-6',
  return: 'M8 4 3 9l5 5M3 9h12a6 6 0 0 1 0 12h-4',
  star: 'm12 3 3 6 7 1-5 5 1 7-6-3-6 3 1-7-5-5 7-1Z',
  layers: 'm12 3 10 5-10 5L2 8ZM2 13l10 5 10-5M2 18l10 5 10-5',
  clock: 'M12 8v5l3 2M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18',
}
</script>
<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
  ><path :d="paths[name] ?? paths.document" /></svg>
</template>
