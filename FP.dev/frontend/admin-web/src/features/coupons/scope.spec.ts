import { describe, expect, it } from 'vitest'
import { describeScopeProblem, maximumScopeEntries, toScopeRequestFields } from './scope'

function selection(overrides: Partial<Parameters<typeof describeScopeProblem>[0]> = {}) {
  return {
    scopeType: 'all' as const,
    categoryPublicIds: [],
    productPublicIds: [],
    excludedProductPublicIds: [],
    ...overrides,
  }
}

describe('describeScopeProblem', () => {
  it('accepts 全站 with nothing selected', () => {
    expect(describeScopeProblem(selection())).toBeNull()
  })

  it('rejects 指定範圍 with no category and no product', () => {
    // 後端 RequireValidRule：Restricted 至少要一筆分類或商品，否則是一張永遠
    // 算不出折扣的券。
    expect(describeScopeProblem(selection({ scopeType: 'restricted' })))
      .toBe('指定範圍至少要選擇一個分類或商品。')
  })

  it('does not let an exclusion satisfy 指定範圍 on its own', () => {
    // 後端只看包含集合，排除清單再長都不算數。
    const problem = describeScopeProblem(selection({
      scopeType: 'restricted',
      excludedProductPublicIds: ['p1'],
    }))

    expect(problem).toBe('指定範圍至少要選擇一個分類或商品。')
  })

  it('accepts 指定範圍 with only a category', () => {
    expect(describeScopeProblem(selection({
      scopeType: 'restricted',
      categoryPublicIds: ['cat-1'],
    }))).toBeNull()
  })

  it('rejects a list longer than the server cap, naming the list', () => {
    const tooMany = Array.from({ length: maximumScopeEntries + 1 }, (_, index) => `p${index}`)

    expect(describeScopeProblem(selection({ excludedProductPublicIds: tooMany })))
      .toBe(`排除商品最多 ${maximumScopeEntries} 筆。`)
  })

  it('accepts a list exactly at the cap', () => {
    const atCap = Array.from({ length: maximumScopeEntries }, (_, index) => `p${index}`)

    expect(describeScopeProblem(selection({ excludedProductPublicIds: atCap }))).toBeNull()
  })

  it('rejects a product that is both included and excluded', () => {
    expect(describeScopeProblem(selection({
      scopeType: 'restricted',
      productPublicIds: ['p1', 'p2'],
      excludedProductPublicIds: ['p2'],
    }))).toBe('同一件商品不能同時列為適用與排除。')
  })
})

describe('toScopeRequestFields', () => {
  it('drops the included lists when the scope is 全站', () => {
    // 管理員先挑了分類與商品、又改回全站是很自然的順序。把已選項目一起送出，
    // 後端 RequireValidRule 會以 400 回「All 不得帶包含範圍」。
    const fields = toScopeRequestFields(selection({
      scopeType: 'all',
      categoryPublicIds: ['cat-1'],
      productPublicIds: ['p1'],
    }))

    expect(fields.categoryPublicIds).toBeNull()
    expect(fields.productPublicIds).toBeNull()
  })

  it('keeps the exclusion list on a 全站 coupon', () => {
    // 「全站九折，特定機種除外」是允許的組合：後端只禁止 All 帶包含範圍。
    const fields = toScopeRequestFields(selection({
      scopeType: 'all',
      excludedProductPublicIds: ['p9'],
    }))

    expect(fields.scopeType).toBe('all')
    expect(fields.excludedProductPublicIds).toEqual(['p9'])
  })

  it('sends null rather than an empty array for a list nobody filled in', () => {
    const fields = toScopeRequestFields(selection({
      scopeType: 'restricted',
      categoryPublicIds: ['cat-1'],
    }))

    expect(fields.productPublicIds).toBeNull()
    expect(fields.excludedProductPublicIds).toBeNull()
  })

  it('copies the selected lists instead of handing back the form state', () => {
    const source = selection({ scopeType: 'restricted', categoryPublicIds: ['cat-1'] })

    const fields = toScopeRequestFields(source)

    expect(fields.categoryPublicIds).toEqual(['cat-1'])
    expect(fields.categoryPublicIds).not.toBe(source.categoryPublicIds)
  })
})
