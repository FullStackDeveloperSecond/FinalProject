import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

export const DoSelectAura = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#f4f8fc',
      100: '#e8f2fc',
      200: '#c9dcec',
      300: '#9fc6e8',
      400: '#64a1dc',
      500: '#2f7dd3',
      600: '#256fbd',
      700: '#1f63ac',
      800: '#1b528e',
      900: '#153f6e',
      950: '#102d4f',
    },
  },
})

export const doSelectPrimeVueOptions = {
  theme: {
    preset: DoSelectAura,
    options: {
      darkModeSelector: false,
    },
  },
}
