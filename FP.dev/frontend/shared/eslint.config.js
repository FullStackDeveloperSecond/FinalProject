import js from '../customer-web/node_modules/@eslint/js/src/index.js'
import eslintPluginVue from '../customer-web/node_modules/eslint-plugin-vue/dist/index.js'
import tseslint from '../customer-web/node_modules/typescript-eslint/dist/index.js'

export default tseslint.config(
  { ignores: ['coverage'] },
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
  },
  {
    // TypeScript-eslint's official guidance: no-undef is redundant (and unreliable, e.g. for
    // ambient DOM lib types like MouseEvent) once TypeScript itself checks for undefined
    // symbols — https://typescript-eslint.io/rules/no-undef/. Mirrors customer-web/admin-web.
    files: ['**/*.ts', '**/*.vue'],
    rules: {
      'no-undef': 'off',
    },
  },
)
