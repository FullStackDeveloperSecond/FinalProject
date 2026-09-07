# DoSelect v10 / support-v5 unavailable-path diagnostic

## Verdict

`FAIL`. The support case passed all three trials; the product-search case again timed out at the five-second boundary and separately reproduced an Intent-field duplication.

- Revision: `390f3d020a015032c492980a9f6061e248feaf4b`
- Run ID: `20260906T194420Z-v10-support-v5-unavailable-diagnostic-390f3d02`
- Cases: `SEARCH-NOVICE-020`, `SUPPORT-POLICY-013`; 3 trials each
- Execution: 6 planned / 6 actual; not stopped by cost
- Cost: US$0.019745 under the US$0.03 stop line
- Formal human review: 5 Pass / 1 Fail

## Findings

- `SUPPORT-POLICY-013`: 3/3 completed, deterministic and human Pass. The complete-baseline outcome mismatch did not reproduce.
- `SEARCH-NOVICE-020` trial 2: completed with the exact expected structured Intent.
- `SEARCH-NOVICE-020` trial 1: completed and correctly entered existing-part confirmation, but preferences contained both `需要 Wi-Fi` and redundant `支援 AM5 CPU` while `AM5 CPU` was already in `proposedExistingParts`.
- `SEARCH-NOVICE-020` trial 3: unavailable after 5,019 ms with zero tokens and no answer.

The repetition rules out treating product availability as a one-off complete-baseline variance. The redundant preference is also a bounded, reproducible application-normalization gap.

## Evidence

Raw T2 evidence is Git-ignored and pinned to the revision above:

`FP.dev/.run/ai-evals/20260906T194420Z-v10-support-v5-unavailable-diagnostic-390f3d02`

The evidence manifest verifies all six listed artifacts by size and SHA-256. Sanitization found zero credential assignment, private-key, bearer-token, email, and Taiwan-mobile patterns. No Production data or real personal data was used.

## Selected remediation

DEC-BATCH-065 adopts the first sufficient option: product-search-only `service_tier: fast`, with `default` as a configuration rollback, plus deterministic removal of preferences that repeat a proposed existing part. Model, five-second timeout, zero synchronous retry, security controls, public APIs, database schema, and approved evaluation thresholds remain unchanged.
