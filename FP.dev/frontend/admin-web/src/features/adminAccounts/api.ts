import { apiClient } from '../../api/client'

export interface AdminAccount {
  publicId: string
  displayName: string
  employeeCode: string
  email: string
  status: string
  emailVerified: boolean
  twoFactorEnabled: boolean
  roles: string[]
  createdAtUtc: string
  updatedAtUtc: string
  rowVersion: string
}

export interface AdminAccountFilters {
  Search?: string
  Status?: 'active' | 'pendingEmailVerification' | 'suspended'
  Role?: string
  Page: number
  PageSize: number
}

export interface AdminAccountPage {
  items: AdminAccount[]
  totalCount: number
  page: number
  pageSize: number
  availableRoles: string[]
}

export interface CreateAdminAccountBody {
  email: string
  password: string
  displayName: string
  employeeCode: string
  roles: string[]
  confirmSuperAdmin: boolean
}

export interface UpdateAdminRolesBody {
  roles: string[]
  rowVersion: string
  reasonCode: string
  confirmSuperAdmin: boolean
}

export interface AcceptAdminInvitationBody {
  publicId: string
  token: string
  newPassword: string
}

export async function listAdminAccounts(query: AdminAccountFilters): Promise<AdminAccountPage> {
  const { data } = await apiClient.GET('/api/v1/admin/administrators', { params: { query } })
  return data as AdminAccountPage
}

export async function createAdminAccount(body: CreateAdminAccountBody): Promise<AdminAccount> {
  const { data } = await apiClient.POST('/api/v1/admin/administrators', { body })
  return data as AdminAccount
}

export async function updateAdminRoles(publicId: string, body: UpdateAdminRolesBody): Promise<AdminAccount> {
  const { data } = await apiClient.PUT('/api/v1/admin/administrators/{publicId}/roles', {
    params: { path: { publicId } },
    body,
  })
  return data as AdminAccount
}

export async function resendAdminInvitation(publicId: string, rowVersion: string): Promise<void> {
  await apiClient.POST('/api/v1/admin/administrators/{publicId}/actions/resend-invitation', {
    params: { path: { publicId } },
    body: { rowVersion },
  })
}

export async function acceptAdminInvitation(body: AcceptAdminInvitationBody): Promise<void> {
  await apiClient.POST('/api/v1/admin/auth/invitations/accept', { body })
}
