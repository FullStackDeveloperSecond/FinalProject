# DoSelect product-search-v10 / support-v4 five-case Live smoke report

## Evaluation decision

- Verdict: `FAIL`; the immutable runner result failed automated required-fact grading, and formal human review found one separate customer-language failure.
- Revision: `dev@ecb5b0cd6886f8e6e66b4b1012d389b5b6251c4b` (PR #134 squash merge).
- System-environment Run ID: `20260906T172940Z-v10-support-v4-five-case-smoke-system-dev-ecb5b0cd`.
- Versions: `product-search-v10` / `support-v4`; dataset `zh-TW-v1.0.8-draft`; fixture `v1.0.4`; grader `deterministic-v1.1.6`.
- Scope: the five case IDs that failed the preceding v9/support-v3 baseline, each run for three trials; 15 completed Responses requests.
- Cost: US$0.019291 against the US$0.05 stop line; 30,291 input and 1,776 output tokens.
- Evidence: T2, synthetic data only; no production data, real personal data, credential output, or security-threshold relaxation.

An earlier attempt in the workspace sandbox failed before HTTP because Windows Schannel could not acquire TLS credentials. It recorded 0 tokens and US$0, so it is classified as `ENVIRONMENT_FAILURE`, not model-quality evidence. A credential-free endpoint check in the user-requested system environment completed TLS and returned the expected unauthenticated 401 before the bounded run was retried there.

## Automated and human results

| Gate | Threshold | Runner result | Formal interpretation |
|---|---:|---:|---|
| Completed provider responses | 15 / 15 | 15 / 15 | Pass |
| Schema | 100% for this focused regression | 100% | Pass |
| Intent fields | 100% for the four repaired search cases | 12 / 12 | Pass |
| Clarification shape | 100% | 100% | Pass |
| Valid recommendation | 100% | 100% | Pass |
| Citation grounding | 100% | 3 / 3 | Pass |
| Support required facts | 100% | 6 / 9 fact instances (66.67%) | False negative: all three answers contain mandatory prepayment and reject cash on delivery |
| Formal human review | 15 / 15 reviewed, no failure | 14 Pass / 1 Fail | Fail: one clarification exposed purpose enum names |
| Security hard failures | 0 | 0 | Pass within selected scope; the smoke contains no privacy/authorization case |

## Findings and repair

1. `SUPPORT-POLICY-011` produced three substantively correct answers. Dataset v1.0.8 nevertheless rejected all three because the contradiction list contained the bare substring `貨到付款`; this also matches correct statements such as `無法貨到付款`. The candidate dataset/grader is versioned to `zh-TW-v1.0.9-draft` / `deterministic-v1.1.7` and narrows contradictions to explicit positive permission forms. Mandatory prepayment remains required and any missing or contradictory fact still fails.
2. `SEARCH-NOVICE-027` trial 2 asked for `Gaming`, `Office`, `Programming`, or `VideoEditing`. This is a real nontechnical-customer language failure. The bounded adapter repair reuses the existing locale display-name map to replace known purpose enum tokens in clarifications before application output.
3. The repair adds deterministic regression coverage for all three observed cash-on-delivery negations, a positive contradiction, negative phrasing that contains `可以`, and the exact purpose enum list. Dataset, case schema, grader contract, and manifest declarations are synchronized, and the validator now fails on future dataset/grader version drift. It does not change the public API, database schema, dependencies, provider settings, authorization, privacy, or safety thresholds.

Lowest-cost check: keeping the result as-is cannot satisfy the existing 100% facts and customer-language gates; changing documents or thresholds would conceal two observed contract defects. Reusing the existing dataset versioning and purpose display-name map is the first sufficient, reversible option.

## Case review summary

- `SEARCH-CREATOR-014`: 3 / 3 human Pass; exact 2TB SSD, video-editing purpose, and NT$50,000 ceiling preserved.
- `SEARCH-NOVICE-019`: 3 / 3 human Pass; exact 8TB within budget, approved backup warning, and no unsupported soft-preference claim.
- `SEARCH-NOVICE-020`: 3 / 3 human Pass; proposed AM5 CPU remains confirmation-gated and no premature recommendation is made.
- `SEARCH-NOVICE-027`: 2 / 3 human Pass; trial 2 fails only for customer-visible enum leakage.
- `SUPPORT-POLICY-011`: 3 / 3 human Pass; all required facts, restrictions, and approved citation are present.

## Evidence index

- Sandbox environment-failure manifest: 3,029 bytes; SHA-256 `83A9F531A3B32E557EE0F4731BA1E992828B37EA3A262840045F662B06208BD0`.
- System-run evidence manifest: 4,952 bytes; SHA-256 `D75FAD81F8F2261ACDE684F8785AE1EEA2C472054D24D6FB7373C9C7167D3D6E`.
- System-run case results: 26,398 bytes; SHA-256 `F3DEC67C7A64F31ED0039D0453C02712F8D365BE5A7DC72DB3187999C37805CB`.
- Derived formal human review: 3,745 bytes; SHA-256 `5C500AD78D001E0B9DA3BFB595E6182A603C18AA18182AEB366FFF9965203C9D`.
- Raw artifacts remain in Git-ignored `.run/ai-evals/<run-id>/` directories; original summaries and case fields were not rewritten.

## Remaining gate

The candidate repair must complete commit, push, read-only review, required CI, and merge. Only then may a new revision-pinned focused live verification and formal human review supersede this failed smoke for the repaired paths. This report does not claim the full 66-request v10/support-v4 Release baseline has passed.
