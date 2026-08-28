import { useQuery } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import { computed, toValue } from 'vue'
import { apiClient } from '../../api/client'
// A-24 reads the same root key every DES-23 support action already invalidates on success/409
// (see ../support/queries.ts's caseWorkbenchRootKey comment) so this page refreshes right along
// with those actions without either module needing to know the other's query shape.
import { caseWorkbenchRootKey } from '../support/queries'
import type { CasePriority, CaseWorkbenchCaseType, CaseWorkbenchPage } from './types'

export interface CaseWorkbenchFilters {
  caseTypes?: CaseWorkbenchCaseType[]
  statuses?: string[]
  priorities?: CasePriority[]
  assigneePublicId?: string
  overdueOnly?: boolean
  keyword?: string
  cursor?: string
  pageSize?: number
}

export const defaultCaseWorkbenchPageSize = 20

export function caseWorkbenchQueryKey(filters: CaseWorkbenchFilters = {}) {
  return [caseWorkbenchRootKey, filters] as const
}

export function useCaseWorkbenchQuery(filters: MaybeRefOrGetter<CaseWorkbenchFilters> = {}) {
  return useQuery({
    queryKey: computed(() => caseWorkbenchQueryKey(toValue(filters))),
    queryFn: async (): Promise<CaseWorkbenchPage> => {
      const current = toValue(filters)
      const { data } = await apiClient.GET('/api/v1/admin/case-workbench', {
        params: {
          query: {
            CaseTypes: current.caseTypes,
            Statuses: current.statuses,
            Priorities: current.priorities,
            AssigneePublicId: current.assigneePublicId,
            OverdueOnly: current.overdueOnly,
            Keyword: current.keyword,
            Cursor: current.cursor,
            PageSize: current.pageSize ?? defaultCaseWorkbenchPageSize,
          },
        },
      })
      return data as CaseWorkbenchPage
    },
  })
}
