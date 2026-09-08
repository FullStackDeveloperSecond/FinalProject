import type { components } from '@doselect/web-shared/api'
import type { CasePriority } from '../support/types'

export type CaseWorkbenchCaseType = components['schemas']['CaseWorkbenchCaseType']
export type CaseWorkbenchAssigneeFilter = NonNullable<components['schemas']['CaseWorkbenchAssigneeFilter']>
export type CaseWorkbenchSortOrder = NonNullable<components['schemas']['CaseWorkbenchSortOrder']>
export type CaseWorkbenchItemDto = components['schemas']['CaseWorkbenchItemDto']
export type CaseWorkbenchPage = components['schemas']['CaseWorkbenchSearchResultDto']

export type { CasePriority }
