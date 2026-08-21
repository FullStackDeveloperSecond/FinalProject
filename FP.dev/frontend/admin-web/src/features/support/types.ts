import type { components } from '@doselect/web-shared/api'

export type SupportTicketCategory = components['schemas']['SupportTicketCategory']
export type SupportTicketStatus = components['schemas']['SupportTicketStatus']
export type CasePriority = components['schemas']['CasePriority']
export type SupportSenderType = components['schemas']['SupportSenderType']

/**
 * AdminSupportTicketsController's three endpoints (GET .../sla, GET .../{id},
 * POST .../actions/claim) predate the generated OpenAPI schema (contracts have not been
 * regenerated yet), so these shapes are authored by hand from the backend's
 * DoSelect.Application.Support.Admin.Dtos records. They are deliberately public-safe: no
 * internal Identity string Id, admin Email, storage keys, hashes, or physical paths — only
 * PublicId/DisplayName for the assignee, matching AdminAssigneeSummaryDto.
 */
export interface AdminAssigneeSummaryDto {
  publicId: string
  displayName: string
}

export interface AdminSupportMessageDto {
  publicId: string
  senderType: SupportSenderType
  aiGenerated: boolean
  isInternal: boolean
  body: string
  language: string
  sentAtUtc: string
}

export interface AdminSupportTicketDetailDto {
  publicId: string
  ticketNumber: string
  category: SupportTicketCategory
  subject: string
  status: SupportTicketStatus
  priority: CasePriority
  orderPublicId: string | null
  assignee: AdminAssigneeSummaryDto | null
  createdAtUtc: string
  lastActivityAtUtc: string
  firstResponseDueAtUtc: string
  resolutionDueAtUtc: string
  isOverdue: boolean
  firstHumanResponseAtUtc: string | null
  resolvedAtUtc: string | null
  closedAtUtc: string | null
  reopenCount: number
  /**
   * Public-safe, usability-only hint of which actions the caller may currently attempt.
   * Contains exactly "claim" when the ticket is Open and unassigned, otherwise empty. Only the
   * exact "claim" token may show the claim button — the server's Policy and store-level
   * conditional check remain the authoritative gate regardless of this hint.
   */
  availableActions: string[]
  rowVersion: string
  messages: AdminSupportMessageDto[]
}

/** The claim endpoint's response shape (AdminSupportTicketDto): Assignee is always present. */
export interface AdminSupportTicketDto {
  publicId: string
  ticketNumber: string
  category: SupportTicketCategory
  subject: string
  status: SupportTicketStatus
  priority: CasePriority
  orderPublicId: string | null
  assignee: AdminAssigneeSummaryDto
  createdAtUtc: string
  lastActivityAtUtc: string
  firstResponseDueAtUtc: string
  resolutionDueAtUtc: string
  firstHumanResponseAtUtc: string | null
  resolvedAtUtc: string | null
  closedAtUtc: string | null
  reopenCount: number
  rowVersion: string
}

/**
 * One row of the admin SLA queue (SupportSlaItemDto). EffectiveDueAtUtc/UsageRatio/IsOverdue are
 * computed by the backend against server time; the frontend must not recompute them.
 */
export interface SupportSlaItemDto {
  ticketPublicId: string
  ticketNumber: string
  priority: CasePriority
  assignee: AdminAssigneeSummaryDto | null
  status: SupportTicketStatus
  firstResponseDueAtUtc: string
  resolutionDueAtUtc: string
  effectiveDueAtUtc: string
  usageRatio: number
  isOverdue: boolean
  lastActivityAtUtc: string
  rowVersion: string
}

/** CursorPage<SupportSlaItemDto>: NextCursor is null once HasMore is false. */
export interface SupportSlaQueuePage {
  items: SupportSlaItemDto[]
  nextCursor: string | null
  hasMore: boolean
}

export interface ClaimSupportTicketRequest {
  rowVersion: string
}
