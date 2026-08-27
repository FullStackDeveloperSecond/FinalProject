import { ApiError } from '@doselect/web-shared/api'
import { describe, expect, it } from 'vitest'
import { describeApiError } from './errorMessages'

describe('describeApiError', () => {
  /** 組長 PR #24 round 5 review, item 5: these two codes previously fell through to the raw English detail. */
  it('translates sku_default_required', () => {
    const error = new ApiError('Cannot unset the current default SKU directly', { status: 409, code: 'sku_default_required' })

    expect(describeApiError(error)).toBe('無法直接取消或刪除目前的預設 SKU，請先將其他 SKU 設為預設')
  })

  it('translates sku_missing_required_specification', () => {
    const error = new ApiError('Missing required specification', { status: 400, code: 'sku_missing_required_specification' })

    expect(describeApiError(error)).toBe('缺少分類規定的必要規格值，無法上架此 SKU')
  })

  it('falls back to the raw message for an unknown code', () => {
    const error = new ApiError('Something unexpected', { status: 500, code: 'unexpected_error' })

    expect(describeApiError(error)).toBe('Something unexpected')
  })
})
