import { describe, expect, it } from 'vitest'
import { batchResultMessage } from './batchPresentation'
import type { BatchShipmentItemResultDto } from './types'

describe('batch shipment Chinese instructions', () => {
  it('explains unfinished assembly without pretending it is ready', () => {
    const item = { errorCode: 'shipping_order_not_ready', message: "The order's assembly is Started." } as BatchShipmentItemResultDto
    expect(batchResultMessage(item)).toContain('組裝中')
    expect(batchResultMessage(item)).toContain('完成測試')
    expect(batchResultMessage(item)).not.toContain('Started')
  })
  it('does not expose a raw unknown server exception', () => {
    expect(batchResultMessage({ errorCode: 'unknown', message: 'internal exception' } as BatchShipmentItemResultDto)).not.toContain('internal exception')
  })
})
