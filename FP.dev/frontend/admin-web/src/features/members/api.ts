import { createApiClient } from '../../api/client'

export interface AdminMember {
  publicId: string
  displayName: string
  emailMasked: string
  status: string
  emailVerified: boolean
  createdAtUtc: string
  updatedAtUtc: string
  rowVersion: string
}
export interface MemberFilters { Search?: string; Status?: string; Page: number; PageSize: number }
export interface MemberPage { items: AdminMember[]; totalCount: number; page: number; pageSize: number }
export interface MemberStatusBody { active: boolean; rowVersion: string; reasonCode: string }
interface MemberPaths {
  '/api/v1/admin/members': { get: { parameters: { query?: MemberFilters }; responses: { 200: { content: { 'application/json': MemberPage } } } } }
  '/api/v1/admin/members/{publicId}': { get: { parameters: { path: { publicId: string } }; responses: { 200: { content: { 'application/json': AdminMember } } } } }
  '/api/v1/admin/members/{publicId}/status': { post: { parameters: { path: { publicId: string } }; requestBody: { content: { 'application/json': MemberStatusBody } }; responses: { 204: { content?: never } } } }
}
const client = createApiClient<MemberPaths>()
export async function listMembers(query: MemberFilters) {
  const { data } = await client.GET('/api/v1/admin/members', { params: { query } })
  return data!
}
export async function getMember(publicId: string) {
  const { data } = await client.GET('/api/v1/admin/members/{publicId}', { params: { path: { publicId } } })
  return data!
}
export async function changeMemberStatus(publicId: string, body: MemberStatusBody) {
  await client.POST('/api/v1/admin/members/{publicId}/status', { params: { path: { publicId } }, body })
}
