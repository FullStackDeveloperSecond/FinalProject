import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

/**
 * DoSelect 共用 PrimeVue preset（PM-02 / v0.2）。
 *
 * 把 PrimeVue 5 的語意 token 綁到 `design-tokens.css` 的 CSS 變數，
 * 讓 PrimeVue 元件的顏色由單一 Token 來源決定：
 *   primary  → 翡翠綠（--color-primary），全站唯一主操作色
 *   surface  → 白 / 淡藍階
 *   text     → 深藍（--color-text）
 *
 * 薄荷綠（--color-mint）與奶油黃（--color-butter）是裝飾角色，
 * 刻意不綁進任何互動色階（button / link / focus / highlight）。
 *
 * Light-only：不提供 dark colorScheme；main.ts 以 darkModeSelector: false 停用。
 */
export const DoSelectPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: 'var(--color-primary-soft)',
      100: 'var(--color-primary-soft)',
      200: 'var(--color-primary-soft)',
      300: 'var(--color-primary)',
      400: 'var(--color-primary)',
      500: 'var(--color-primary)',
      600: 'var(--color-primary)',
      700: 'var(--color-primary-dark)',
      800: 'var(--color-primary-dark)',
      900: 'var(--color-primary-dark)',
      950: 'var(--color-primary-dark)',
    },
    focusRing: {
      width: '2px',
      style: 'solid',
      color: 'var(--color-focus-ring)',
      offset: '2px',
    },
    colorScheme: {
      light: {
        primary: {
          color: 'var(--color-primary)',
          contrastColor: 'var(--color-on-primary)',
          hoverColor: 'var(--color-primary-dark)',
          activeColor: 'var(--color-primary-dark)',
        },
        text: {
          color: 'var(--color-text)',
          mutedColor: 'var(--color-text-muted)',
        },
        content: {
          background: 'var(--color-surface)',
          borderColor: 'var(--color-border)',
        },
        formField: {
          background: 'var(--color-surface)',
          borderColor: 'var(--color-border)',
          color: 'var(--color-text)',
          focusBorderColor: 'var(--color-primary)',
        },
      },
    },
  },
})
