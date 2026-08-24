export interface AdminMemberSummaryDto {
  publicId: string
  displayName: string
  email: string
  registeredAtUtc: string
  accountStatus: string
}

export interface PageResult<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
}

export interface AdminMemberListStatsDto {
  totalMembers: number
  newTodayCount: number
  activeCount: number
}

export interface AdminMemberListResponseDto {
  members: PageResult<AdminMemberSummaryDto>
  stats: AdminMemberListStatsDto
}

export interface AdminMemberStatsDto {
  totalSpend: number
  totalOrderCount: number
  returnRatePercent: number
}

export interface AdminMemberOrderSummaryDto {
  orderPublicId: string
  orderNumber: string
  placedAtUtc: string
  orderStatus: string
  grandTotal: number
}

export interface AdminMemberActivityEventDto {
  occurredAtUtc: string
  eventType: string
  description: string
}

export interface AdminMemberDetailDto {
  publicId: string
  displayName: string
  email: string
  phone: string | null
  birthDate: string | null
  registeredAtUtc: string
  accountStatus: string
  rowVersion: string
  stats: AdminMemberStatsDto
  recentOrders: AdminMemberOrderSummaryDto[]
  activityLog: AdminMemberActivityEventDto[]
}

export interface UpdateAdminMemberProfileRequest {
  displayName: string
  birthDate: string | null
  rowVersion: string
}

export interface SetMemberAccountStatusRequest {
  suspend: boolean
  rowVersion: string
}

interface ProblemDetails {
  code?: string
  detail?: string
  traceId?: string
  correlationId?: string
}

/**
 * 手寫 Paths（本專案沒有 OpenAPI codegen），對應
 * DoSelect.Api/Admin/Members/AdminMembersController.cs 與 AdminMemberDtos.cs。
 * ⚠ 新範圍：後台會員管理沒有既有 API 規格，這是新設計的契約，待 alex 覆核。
 */
export interface AdminMemberPaths {
  '/api/v1/admin/members': {
    get: {
      parameters: {
        query: {
          search?: string
          status?: string
          registeredFrom?: string
          registeredTo?: string
          pageNumber?: number
          pageSize?: number
        }
      }
      responses: {
        200: { content: { 'application/json': AdminMemberListResponseDto } }
        403: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/members/{publicId}': {
    get: {
      parameters: { path: { publicId: string } }
      responses: {
        200: { content: { 'application/json': AdminMemberDetailDto } }
        404: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
    put: {
      parameters: { path: { publicId: string } }
      requestBody: { content: { 'application/json': UpdateAdminMemberProfileRequest } }
      responses: {
        204: { content: never }
        404: { content: { 'application/problem+json': ProblemDetails } }
        409: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/members/{publicId}/reset-password': {
    post: {
      parameters: { path: { publicId: string } }
      responses: {
        204: { content: never }
        404: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
  '/api/v1/admin/members/{publicId}/status': {
    post: {
      parameters: { path: { publicId: string } }
      requestBody: { content: { 'application/json': SetMemberAccountStatusRequest } }
      responses: {
        204: { content: never }
        404: { content: { 'application/problem+json': ProblemDetails } }
        409: { content: { 'application/problem+json': ProblemDetails } }
      }
    }
  }
}
