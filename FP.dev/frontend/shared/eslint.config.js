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
)
