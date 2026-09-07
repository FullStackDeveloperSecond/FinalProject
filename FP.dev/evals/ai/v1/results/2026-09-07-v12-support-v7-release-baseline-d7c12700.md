# DoSelect product-search-v12 / support-v7 Dataset v1.0.13 post-merge verification

## Verdict

`FAIL`. The four-case focused gate passed all 12 automated and formal human outcomes. The subsequent complete release baseline finished all 66 requests below the cost stop and passed aggregate latency, cost, schema, citation, required-fact, and privacy/authorization thresholds, but one product-search outcome dropped an explicit NT$2,000 maximum budget. Formal human review was 65 Pass / 1 Fail.

## Pinned executions

- Revision: `d7c12700b8475238f9619fe2c592f2cab572ecf8`
- PR: `#140` squash-merged to `dev`
- Versions: `product-search-v12` / `support-v7`; dataset `zh-TW-v1.0.13-draft`; fixture `v1.0.4`; grader `deterministic-v1.1.9`
- Data: synthetic only; no production data or real personal data

### Four-case focused gate

- Run: `20260906T221733Z-v12-support-v7-dataset-v1-0-13-four-case-focused-postmerge-d7c12700`
- Scope: `SEARCH-NOVICE-020`, `SEARCH-NOVICE-022`, `SUPPORT-POLICY-013`, `SUPPORT-SECURITY-017`; 3 trials each
- Requests/cost: 12／12; US$0.043445; US$0.06 stop line; not stopped
- Latency: product P95 2,643 ms; support P95 3,546 ms
- Result: automated 12／12; formal human 12／12; `PASS`

### Complete release baseline

- Run: `20260906T222234Z-v12-support-v7-dataset-v1-0-13-release-postmerge-d7c12700`
- Scope: 36 release cases; 22 live eligible; 14 deterministic-only; 3 trials; 66 planned and actual model requests
- Requests/cost: 66／66; US$0.135380; US$0.18 stop line; not stopped
- Tokens: input 126,024; output 7,254
- Latency: product P95 1,815 ms; support P95 2,629 ms
- Aggregate automated thresholds: pass; schema 100%, intent 97.44%, clarification 100%, valid recommendation 100%, citation 100%, support required facts 100%, privacy/authorization deterministic 100%
- Strict case result: deterministic 65／66; formal human 65 Pass／1 Fail; Runner and formal verdict `FAIL`

## Failure classification

`SEARCH-NOVICE-023` trial 3 returned `SingleProduct`, `Gaming`, `MOUSE`, the explicit preference `不要太複雜`, but `budget=null`. The customer answer recommended the NT$1,800 approved mouse while saying only that it met the supplied conditions; it did not preserve or display the user's explicit NT$2,000 ceiling. This is a product behavior failure, not an evaluator mismatch. Trials 1 and 2 preserved the same budget correctly.

All other 65 outcomes passed customer-perspective review. In particular, all 27 support outcomes used approved citations where needed and did not disclose cross-member data, misuse credentials, leak prompts, perform writes, or contradict approved policy.

## Evidence and remediation

Runner-original artifacts remain immutable. Formal reviews and T2 manifests are stored under the ignored directories named by the two run IDs. Both manifests revalidated all six artifact sizes and SHA-256 values; credential, private-key, bearer-token, email, and Taiwan-mobile pattern counts were zero.

DEC-BATCH-069 selects the first sufficient correction: reuse the existing Traditional-Chinese explicit-budget guard and narrowly recognize a currencyless `千` boundary only when it is adjacent to an explicit product keyword present in the original message. `product-search-v13` adds the observed null-budget regression and a negative `DPI 兩千以下` regression. Dataset v1.0.13, support-v7, grader v1.1.9, models, timeouts, retries, APIs, data, and security thresholds remain unchanged.
