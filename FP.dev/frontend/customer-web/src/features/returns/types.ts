import type { components } from '@doselect/web-shared/api'

export type ReturnRequestDto = components['schemas']['ReturnRequestDto']
export type ReturnItemDto = components['schemas']['ReturnItemDto']
export type ReturnAttachmentDto = components['schemas']['ReturnAttachmentDto']
export type ReturnShipmentDto = components['schemas']['ReturnShipmentDto']
export type CreateReturnRequestBody = components['schemas']['CreateReturnRequestBody']
export type CreateReturnItemLine = components['schemas']['CreateReturnItemLine']
export type ReturnRequestStatus = ReturnRequestDto['status']
