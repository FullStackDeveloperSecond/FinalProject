import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

/**
 * DoSelect 共用 PrimeVue preset（v4.0 reference-aligned）。
 *
 * 把 PrimeVue 的語意 token 綁到 `design-tokens.css` 的 CSS 變數，
 * 讓 PrimeVue 元件的顏色與自製元件由同一份 Token 決定：
 *
 *   primary  → 高飽和亮藍（--color-primary #0B66E8），全站唯一主操作色
 *   surface  → 白／近白冷色階（--color-page / --color-surface / --color-section）
 *   text     → --color-text（深海軍藍 #001C46，白底 16.74:1）
 *
 * 品牌粉（--color-brand-pink #E6C8D4）刻意不綁進互動色階：
 * 它是輔助色，只做局部背景、柔和選取、裝飾光暈與少量漸層；
 * 若同時當按鈕色會讓「可點擊」的訊號消失，粉底上也無法安全承載白色小字。
 *
 * Logo 青綠（--color-accent）同樣不進按鈕色階，只做少量重點與資訊色。
 *
 * Light-only：不提供 dark colorScheme；main.ts 以 darkModeSelector: false 停用。
 */
export const DoSelectPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: 'var(--color-primary-soft)',
      100: 'var(--color-primary-soft)',
      200: 'var(--color-primary-tint)',
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
        highlight: {
          background: 'var(--color-primary-soft)',
          focusBackground: 'var(--color-primary-tint)',
          color: 'var(--color-text)',
          focusColor: 'var(--color-text)',
        },
        text: {
          color: 'var(--color-text)',
          hoverColor: 'var(--color-text)',
          mutedColor: 'var(--color-text-muted)',
          hoverMutedColor: 'var(--color-text-muted)',
        },
        content: {
          background: 'var(--color-surface)',
          hoverBackground: 'var(--color-surface-strong)',
          borderColor: 'var(--color-border-soft)',
          color: 'var(--color-text)',
          hoverColor: 'var(--color-text)',
        },
        overlay: {
          select: {
            background: 'var(--color-surface)',
            borderColor: 'var(--color-border-soft)',
            color: 'var(--color-text)',
          },
          popover: {
            background: 'var(--color-surface)',
            borderColor: 'var(--color-border-soft)',
            color: 'var(--color-text)',
          },
          modal: {
            background: 'var(--color-surface)',
            borderColor: 'var(--color-border-soft)',
            color: 'var(--color-text)',
          },
        },
        list: {
          option: {
            focusBackground: 'var(--color-surface-strong)',
            selectedBackground: 'var(--color-primary-soft)',
            selectedFocusBackground: 'var(--color-primary-tint)',
            color: 'var(--color-text)',
            focusColor: 'var(--color-text)',
            selectedColor: 'var(--color-text)',
            selectedFocusColor: 'var(--color-text)',
          },
        },
        formField: {
          background: 'var(--color-surface)',
          disabledBackground: 'var(--color-surface-strong)',
          filledBackground: 'var(--color-surface-strong)',
          borderColor: 'var(--color-border)',
          hoverBorderColor: 'var(--color-border-strong)',
          focusBorderColor: 'var(--color-primary)',
          invalidBorderColor: 'var(--color-danger)',
          color: 'var(--color-text)',
          disabledColor: 'var(--color-text-faint)',
          placeholderColor: 'var(--color-text-faint)',
          floatLabelColor: 'var(--color-text-muted)',
        },
        navigation: {
          item: {
            focusBackground: 'var(--color-surface-strong)',
            activeBackground: 'var(--color-primary-soft)',
            color: 'var(--color-text)',
            focusColor: 'var(--color-text)',
            activeColor: 'var(--color-text)',
          },
        },
      },
    },
  },
})
