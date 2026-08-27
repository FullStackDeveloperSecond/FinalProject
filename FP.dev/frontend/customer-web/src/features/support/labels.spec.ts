import { describe, expect, it } from 'vitest'
import { categoryLabels, formatDateTime, priorityLabels, statusLabels } from './labels'
import type { CasePriority, SupportTicketCategory, SupportTicketStatus } from './types'

describe('support labels', () => {
  it('has a Traditional Chinese label for every SupportTicketCategory token', () => {
    const categories: SupportTicketCategory[] = [
      'order',
      'payment',
      'logistics',
      'productWarranty',
      'returnHelp',
      'account',
      'other',
    ]

    for (const category of categories) {
      expect(categoryLabels[category]).toBeTruthy()
    }
  })

  it('has a Traditional Chinese label for every SupportTicketStatus token', () => {
    const statuses: SupportTicketStatus[] = [
      'open',
      'assigned',
      'inProgress',
      'waitingForCustomer',
      'waitingForInternal',
      'resolved',
      'closed',
      'cancelled',
    ]

    for (const status of statuses) {
      expect(statusLabels[status]).toBeTruthy()
    }
  })

  it('has a Traditional Chinese label for every CasePriority token', () => {
    const priorities: CasePriority[] = ['low', 'normal', 'high', 'urgent']

    for (const priority of priorities) {
      expect(priorityLabels[priority]).toBeTruthy()
    }
  })

  it('formats a UTC timestamp using the Asia/Taipei time zone', () => {
    // 2026-08-19T03:00:00Z is 2026-08-19 11:00 in Asia/Taipei (UTC+8).
    const formatted = formatDateTime('2026-08-19T03:00:00Z')

    expect(formatted).toContain('2026')
    expect(formatted).toContain('19')
  })

  it('renders an em dash for a missing timestamp', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime(undefined)).toBe('—')
  })
})
