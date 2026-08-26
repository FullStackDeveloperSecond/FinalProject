import js from '@eslint/js'
import eslintPluginVue from 'eslint-plugin-vue'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  { ignores: ['dist', 'coverage', 'scripts'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...eslintPluginVue.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    languageOptions: {
      parserOptions: {
        parser: tseslint.parser,
      },
    },
    rules: {
      // typescript-eslint's recommended config already turns this off for .ts/.tsx (the
      // TypeScript compiler catches undefined identifiers, including DOM lib globals like
      // Event/HTMLSelectElement, far more accurately) — that override doesn't reach .vue
      // SFCs on its own, so repeat it here rather than let every DOM-typed event handler
      // in a .vue file hit a false positive.
      'no-undef': 'off',
    },
  },
  {
    // TypeScript-eslint's official guidance: no-undef is redundant (and unreliable, e.g. for
    // ambient DOM lib types like Event/HTMLInputElement) once TypeScript itself checks for
    // undefined symbols — https://typescript-eslint.io/rules/no-undef/.
    files: ['**/*.ts', '**/*.vue'],
    rules: {
      'no-undef': 'off',
    },
  },
)
