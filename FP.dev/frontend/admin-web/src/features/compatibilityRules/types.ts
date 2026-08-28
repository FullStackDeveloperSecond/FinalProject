import type { components } from '@doselect/web-shared/api'

export type CompatibilityRuleWarningSettingDto = components['schemas']['CompatibilityRuleWarningSettingDto']
export type CompatibilityRuleAdminDto = components['schemas']['CompatibilityRuleAdminDto']
export type CompatibilityRuleListDto = components['schemas']['CompatibilityRuleListDto']
export type UpdateWarningSettingRequest = components['schemas']['UpdateWarningSettingRequest']
export type SetRuleActivationRequest = components['schemas']['SetRuleActivationRequest']
export type BuildItemInput = components['schemas']['BuildItemInput']
export type CompatibilityFindingDto = components['schemas']['CompatibilityFindingDto']
export type CompatibilityRuleTestRequest = components['schemas']['CompatibilityRuleTestRequest']
export type CompatibilityRuleTestResultDto = components['schemas']['CompatibilityRuleTestResultDto']

/**
 * `severity`/`overall` are plain `string` in the generated schema (HasConversion<string>() with
 * no OpenAPI enum annotation) — this union is this feature's own narrower set for known values,
 * not a wire guarantee.
 */
export type CompatibilitySeverity =
  | 'compatible' | 'warning' | 'blocked' | 'insufficientData' | 'ruleDisabled'
