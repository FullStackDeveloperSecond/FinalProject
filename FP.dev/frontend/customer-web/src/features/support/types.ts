import type { components } from '@doselect/web-shared/api'

export type SupportTicketDto = components['schemas']['SupportTicketDto']
export type SupportTicketSummaryDto = components['schemas']['SupportTicketSummaryDto']
export type SupportMessageDto = components['schemas']['SupportMessageDto']
export type SupportTicketCategory = components['schemas']['SupportTicketCategory']
export type SupportTicketStatus = components['schemas']['SupportTicketStatus']
export type SupportSenderType = components['schemas']['SupportSenderType']
export type CasePriority = components['schemas']['CasePriority']
export type CreateSupportTicketRequest = components['schemas']['CreateSupportTicketRequest']
export type CreateSupportMessageRequest = components['schemas']['CreateSupportMessageRequest']
export type CancelSupportTicketRequest = components['schemas']['CancelSupportTicketRequest']
export type SupportTicketPage = components['schemas']['PageResultOfSupportTicketSummaryDto']

/**
 * The generated OpenAPI schema predates the attachment-upload endpoint (contracts have not been
 * regenerated yet), so this shape is authored by hand from the backend's
 * `SupportAttachmentDto` record. It is deliberately public-safe: no StorageKey, physical path,
 * hash, or scanner output.
 */
export type SupportAttachmentDto = components['schemas']['SupportAttachmentDto']
