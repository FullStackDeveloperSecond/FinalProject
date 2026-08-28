import { apiClient } from '../../api/client'
import type {
  CompatibilityRuleAdminDto,
  CompatibilityRuleListDto,
  CompatibilityRuleTestRequest,
  CompatibilityRuleTestResultDto,
  SetRuleActivationRequest,
  UpdateWarningSettingRequest,
} from './types'

/**
 * The shared API client's middleware throws an `ApiError` for any non-OK response (see
 * `frontend/shared/src/api/client.ts`), so `data` is always populated on the success path
 * handled here — callers do not need to additionally check openapi-fetch's own `error` field.
 */
export async function listCompatibilityRules(): Promise<CompatibilityRuleListDto> {
  const { data } = await apiClient.GET('/api/v1/admin/compatibility-rules')
  return data!
}

export async function updateWarningSetting(
  ruleCode: string,
  request: UpdateWarningSettingRequest,
): Promise<CompatibilityRuleAdminDto> {
  const { data } = await apiClient.PATCH(
    '/api/v1/admin/compatibility-rules/{ruleCode}/warning-settings',
    { params: { path: { ruleCode } }, body: request },
  )
  return data!
}

/** DEC-BATCH-026 (DEC-P311): PATCH .../{ruleCode}/activation, not the old POST .../actions/set-activation. */
export async function setRuleActivation(
  ruleCode: string,
  request: SetRuleActivationRequest,
): Promise<CompatibilityRuleAdminDto> {
  const { data } = await apiClient.PATCH(
    '/api/v1/admin/compatibility-rules/{ruleCode}/activation',
    { params: { path: { ruleCode } }, body: request },
  )
  return data!
}

export async function testCompatibilityRules(
  request: CompatibilityRuleTestRequest,
): Promise<CompatibilityRuleTestResultDto> {
  const { data } = await apiClient.POST('/api/v1/admin/compatibility-rules/test', { body: request })
  return data!
}
