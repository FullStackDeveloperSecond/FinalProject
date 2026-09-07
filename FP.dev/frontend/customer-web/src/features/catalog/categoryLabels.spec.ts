import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { CATALOG_CATEGORY_CODES, categoryLabel } from './categoryLabels'

/**
 * 這份對照表是「顯示層的補充」，不是分類清單的來源 —— 但它引用的代碼必須跟後端
 * Domain 契約 `CompatibilityCatalogContract.Categories` 完全一致，否則首頁分類卡
 * 又會送出後端不認得的值（PR #82 review 第 1 項修的就是這個）。
 */
const contractPath = resolve(
  process.cwd(),
  '../../src/backend/DoSelect.Domain/Catalog/CompatibilityCatalogContract.cs',
)

describe('catalog category labels', () => {
  it('covers exactly the codes the backend contract defines', () => {
    const source = readFileSync(contractPath, 'utf8')
    const categoriesBlock = /public static class Categories\s*\{([\s\S]*?)\n {4}\}/.exec(source)?.[1] ?? ''
    expect(categoriesBlock, 'CompatibilityCatalogContract.Categories 區塊找不到').not.toBe('')

    const contractCodes = [...categoriesBlock.matchAll(/public const string \w+ = "([A-Z_]+)";/g)]
      .map((match) => match[1])
      .sort()

    expect(contractCodes.length).toBeGreaterThan(0)
    expect([...CATALOG_CATEGORY_CODES].sort()).toEqual(contractCodes)
  })

  it('gives every contract code a Chinese label', () => {
    for (const code of CATALOG_CATEGORY_CODES) {
      const label = categoryLabel(code)
      expect(label, `${code} 缺少中文名`).not.toBe(code)
      expect(label).toMatch(/[一-鿿]/)
    }
  })

  it('falls back to the code itself for anything unknown', () => {
    // 後端新增分類時，下拉仍然拿得到它（只是顯示代碼），不會壞掉
    expect(categoryLabel('NEW_CATEGORY_FROM_BACKEND')).toBe('NEW_CATEGORY_FROM_BACKEND')
    expect(categoryLabel('')).toBe('')
  })

  it('agrees with the labels the home page prints on its category cards', () => {
    // 首頁卡片標題與這份對照表必須是同一組字，不然同一個分類在兩處叫不同名字
    const homeSource = readFileSync(resolve(process.cwd(), 'src/pages/HomePage.vue'), 'utf8')
    const cards = [...homeSource.matchAll(/\{ title: '([^']+)', body: '[^']+', icon: '[a-z-]+', categoryCode: '([A-Z_]+)' \}/g)]

    expect(cards.length).toBeGreaterThanOrEqual(5)
    for (const [, title, code] of cards) {
      expect(categoryLabel(code), `首頁「${title}」與對照表的 ${code} 不一致`).toBe(title)
    }
  })
})
