/**
 * Hand-typed Compatibility Rule Admin contract, mirroring `CompatibilityRuleAdminContracts.cs`,
 * `CompatibilityCheckContracts.cs` and `AdminCompatibilityRulesController.cs` on
 * `feature/build-compat-api` (not merged to `dev` yet, so there is no live API to export
 * `frontend/shared`'s generated OpenAPI schema from). This is a stand-in for that generated
 * schema — see `features/categories/types.ts`'s generated-`components` pattern for what this
 * file should become once build-compat-api merges to `dev` and `api:generate` is run for real.
 */

export interface CompatibilityRuleWarningSettingDto {
  settingCode: string
  value: number
  minValue: number
  maxValue: number
  defaultValue: number
}

export interface CompatibilityRuleAdminDto {
  ruleCode: string
  isActive: boolean
  warningSetting: CompatibilityRuleWarningSettingDto | null
}

export interface CompatibilityRuleListDto {
  rules: CompatibilityRuleAdminDto[]
  settingsVersion: number
}

export interface UpdateWarningSettingRequest {
  value: number
  settingsVersion: number
  reason: string
}

export interface SetRuleActivationRequest {
  isActive: boolean
  settingsVersion: number
  reason: string
}

export interface BuildItemInput {
  skuPublicId: string
  quantity: number
}

export type CompatibilitySeverity =
  | 'compatible' | 'warning' | 'blocked' | 'insufficientData' | 'ruleDisabled'

export interface CompatibilityFindingDto {
  ruleCode: string
  severity: CompatibilitySeverity
  messageKey: string
  subjectSkuPublicIds: string[]
  facts: Record<string, unknown>
}

export interface CompatibilityRuleTestRequest {
  items: BuildItemInput[]
  ruleCodes: string[] | null
  useDraftSettings: boolean
  draftWarningSettings: Record<string, number> | null
}

export interface CompatibilityRuleTestResultDto {
  overall: 'compatible' | 'warning' | 'blocked' | 'insufficientData'
  results: CompatibilityFindingDto[]
  settingsVersion: number
  evaluatedAtUtc: string
}

interface JsonResponse<T> {
  content: {
    'application/json': T
  }
}

interface ProblemResponse {
  content: {
    'application/problem+json': { code: string }
  }
}

export interface CompatibilityRulesApiPaths {
  '/api/v1/admin/compatibility-rules': {
    get: {
      responses: {
        200: JsonResponse<CompatibilityRuleListDto>
      }
    }
  }
  '/api/v1/admin/compatibility-rules/{ruleCode}/warning-settings': {
    patch: {
      parameters: { path: { ruleCode: string } }
      requestBody: { content: { 'application/json': UpdateWarningSettingRequest } }
      responses: {
        200: JsonResponse<CompatibilityRuleAdminDto>
        400: ProblemResponse
        409: ProblemResponse
      }
    }
  }
  '/api/v1/admin/compatibility-rules/{ruleCode}/actions/set-activation': {
    post: {
      parameters: { path: { ruleCode: string } }
      requestBody: { content: { 'application/json': SetRuleActivationRequest } }
      responses: {
        200: JsonResponse<CompatibilityRuleAdminDto>
        400: ProblemResponse
        409: ProblemResponse
      }
    }
  }
  '/api/v1/admin/compatibility-rules/test': {
    post: {
      requestBody: { content: { 'application/json': CompatibilityRuleTestRequest } }
      responses: {
        200: JsonResponse<CompatibilityRuleTestResultDto>
        400: ProblemResponse
      }
    }
  }
}
