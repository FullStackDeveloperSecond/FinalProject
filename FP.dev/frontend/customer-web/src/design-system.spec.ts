/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

// `npm test` runs vitest with cwd = this package root (frontend/customer-web).
const pkgRoot = process.cwd()
const tokensCssRaw = readFileSync(
  resolve(pkgRoot, '../shared/src/styles/design-tokens.css'),
  'utf8',
)
// Strip CSS comments so documentation text isn't mistaken for a real rule.
const tokensCss = tokensCssRaw.replace(/\/\*[\s\S]*?\*\//g, '')
const mainSource = readFileSync(resolve(pkgRoot, 'src/main.ts'), 'utf8')

describe('design-tokens.css is Light-only', () => {
  it('has no prefers-color-scheme media query that could auto-enable Dark', () => {
    expect(tokensCss).not.toMatch(/prefers-color-scheme/i)
  })

  it('keeps :root[data-theme="dark"] as opt-in future structure only', () => {
    expect(tokensCss).toContain(':root[data-theme="dark"]')
  })
})

describe('PrimeVue theme is Light-only', () => {
  it('registers PrimeVue with darkModeSelector: false', () => {
    expect(mainSource).toMatch(/darkModeSelector:\s*false/)
  })
})
