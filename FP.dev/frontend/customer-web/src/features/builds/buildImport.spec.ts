import { beforeEach, describe, expect, it } from 'vitest'
import { mergeBuildImport, parseBuildImport, readPendingBuildImport, stageBuildImport, type BuildImport } from './buildImport'

const cpu = { skuPublicId: 'cpu-1', name: 'CPU 一', categoryCode: 'CPU', quantity: 1 }
const memory = { skuPublicId: 'ram-1', name: '記憶體', categoryCode: 'MEMORY', quantity: 1 }
beforeEach(() => window.sessionStorage.clear())
describe('build import rules', () => {
  it('reselecting the same cart row replaces its selected quantity instead of purchasing it twice', () => {
    const source = { publicId: 'cart-a', rowVersion: 'revision-a', items: [{ cartItemPublicId: 'row-a', skuPublicId: memory.skuPublicId, quantity: 1 }] }
    const draft = { name: '草稿', items: [memory], cartSource: source }
    expect(mergeBuildImport(draft, { name: '', items: [memory], cartSource: source }, 'keep').items[0]?.quantity).toBe(1)
    const changed = mergeBuildImport(draft, { name: '', items: [{ ...memory, quantity: 2 }], cartSource: { ...source, items: [{ ...source.items[0]!, quantity: 2 }] } }, 'keep')
    expect(changed.items[0]?.quantity).toBe(2)
    expect(changed.cartSource?.items[0]?.quantity).toBe(2)
  })
  it('requires an explicit conflict policy and keeps unrelated draft categories', () => {
    const draft = { name: '原草稿', items: [cpu, memory] }
    const incoming = { name: '匯入', items: [{ ...cpu, skuPublicId: 'cpu-2', name: 'CPU 二' }] }
    expect(mergeBuildImport(draft, incoming, 'keep').items).toEqual([cpu, memory])
    expect(mergeBuildImport(draft, incoming, 'replace').items).toEqual([memory, incoming.items[0]])
    expect(mergeBuildImport(draft, incoming, 'reset').items).toEqual(incoming.items)
    expect(draft.items).toEqual([cpu, memory])
  })
  it('merges memory quantities without silently clipping an over-limit request', () => {
    expect(mergeBuildImport({ name: '草稿', items: [memory] }, { name: '', items: [memory] }, 'keep').items[0]?.quantity).toBe(2)
    expect(() => mergeBuildImport({ name: '草稿', items: [{ ...memory, quantity: 8 }] }, { name: '', items: [memory] }, 'keep')).toThrow('1–8')
  })
  it('preserves confirmed owned parts separately from purchased items through staging', () => {
    const input: BuildImport = { name: '混合', items: [memory], ownedParts: [{
      sourceType: 'catalogSku', skuPublicId: cpu.skuPublicId, categoryCode: 'CPU', displayName: '自有 CPU',
      quantity: 1, specifications: [], confirmedByUser: true,
    }] }
    stageBuildImport(input)
    const actual = readPendingBuildImport()!
    expect(actual.items).toEqual([memory])
    expect(actual.ownedParts).toEqual(input.ownedParts)
    expect(mergeBuildImport({ name: '', items: [] }, actual, 'reset').ownedParts).toEqual(input.ownedParts)
  })
  it('does not silently combine snapshots from different carts or revisions', () => {
    const source = { publicId: 'cart-a', rowVersion: 'revision-a', items: [{ cartItemPublicId: 'row-a', skuPublicId: memory.skuPublicId, quantity: 1 }] }
    const draft = { name: '草稿', items: [memory], cartSource: source }
    expect(() => mergeBuildImport(draft, { name: '', items: [], cartSource: { ...source, rowVersion: 'revision-b' } }, 'keep')).toThrow('購物車已變更')
    expect(() => mergeBuildImport(draft, { name: '', items: [], cartSource: { ...source, publicId: 'cart-b' } }, 'keep')).toThrow('購物車已變更')
  })
  it('rejects malformed storage, unsupported categories, invalid quantities and singleton duplicates', () => {
    expect(() => parseBuildImport({ name: '', items: [null] })).toThrow()
    expect(() => parseBuildImport({ name: '', items: [{ ...cpu, categoryCode: 'SECRET' }] })).toThrow()
    expect(() => parseBuildImport({ name: '', items: [{ ...cpu, quantity: 1.5 }] })).toThrow()
    expect(() => mergeBuildImport({ name: '', items: [] }, { name: '', items: [cpu, cpu] }, 'reset')).toThrow('只能選擇一件')
    window.sessionStorage.setItem('doselect.build.pendingImport', '{')
    expect(readPendingBuildImport()).toBeNull()
  })
})
