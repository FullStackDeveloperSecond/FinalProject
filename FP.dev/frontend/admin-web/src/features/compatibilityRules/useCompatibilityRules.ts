import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { listCompatibilityRules, setRuleActivation, testCompatibilityRules, updateWarningSetting } from './api'
import type { CompatibilityRuleTestRequest, SetRuleActivationRequest, UpdateWarningSettingRequest } from './types'

const compatibilityRulesQueryKey = ['compatibility-rules'] as const

export function useCompatibilityRuleList() {
  return useQuery({
    queryKey: compatibilityRulesQueryKey,
    queryFn: () => listCompatibilityRules(),
  })
}

export function useUpdateWarningSetting() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ ruleCode, request }: { ruleCode: string, request: UpdateWarningSettingRequest }) =>
      updateWarningSetting(ruleCode, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: compatibilityRulesQueryKey }),
  })
}

export function useSetRuleActivation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ ruleCode, request }: { ruleCode: string, request: SetRuleActivationRequest }) =>
      setRuleActivation(ruleCode, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: compatibilityRulesQueryKey }),
  })
}

/** Write-free — does not invalidate the rule list (settings version never changes from a test run). */
export function useTestCompatibilityRules() {
  return useMutation({
    mutationFn: (request: CompatibilityRuleTestRequest) => testCompatibilityRules(request),
  })
}
