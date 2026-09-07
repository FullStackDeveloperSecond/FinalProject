# DoSelect product-search-v13 / support-v7 post-merge verification

## Verdict

`FAIL`. The repaired explicit-budget focused gate passed 3/3 automated and formal human outcomes. The subsequent complete release baseline finished all 66 requests below the cost stop, but one complete CustomBuild intent produced a redundant custom-build-versus-prebuilt clarification and no recommendation. Formal human review was 65 Pass / 1 Fail.

## Pinned executions

- Revision: `65c2318fbd581a6edb53e8a8c656a0bb6f391697`
- PR: `#141` squash-merged to `dev`
- Versions: `product-search-v13` / `support-v7`; dataset `zh-TW-v1.0.13-draft`; fixture `v1.0.4`; grader `deterministic-v1.1.9`
- Data: synthetic only; no production data or real personal data

### SEARCH-NOVICE-023 focused gate

- Run: `20260906T230000Z-v13-support-v7-search023-focused-postmerge-65c2318f`
- Scope: one case, 3 trials
- Requests/cost: 3/3; US$0.001687; US$0.03 stop line; not stopped
- Latency: product P95 3,730 ms
- Result: automated 3/3; formal human 3/3; `PASS`

### Complete release baseline

- Run: `20260906T230400Z-v13-support-v7-release-postmerge-65c2318f`
- Scope: 36 release cases; 22 live eligible; 14 deterministic-only; 3 trials; 66 planned and actual model requests
- Requests/cost: 66/66; US$0.135779; US$0.18 stop line; not stopped
- Tokens: input 126,024; output 7,296
- Latency: product P95 2,262 ms; support P95 3,677 ms
- Automated rates: schema 100%; intent 100%; clarification shape 97.44%; clarification precision 92.31%; clarification recall 100%; valid recommendation 96.30%; citation, support required facts, and privacy/authorization 100%
- Strict result: deterministic 65/66; formal human 65 Pass / 1 Fail; Runner and formal verdict `FAIL`

## Failure classification

`SEARCH-CREATOR-013` trial 1 returned `CustomBuild`, both `GraphicDesign` and `ThreeDRendering`, and the explicit NT$75,000 maximum, but then asked whether the customer wanted a custom build or prebuilt system. That question contradicted the already resolved intent and prevented a recommendation. Trials 2 and 3 correctly recommended the approved NT$70,000 workstation and grounded the GPU-priority/64GB tradeoff.

All other 65 outcomes passed customer-perspective review. All 27 support outcomes used approved citations where needed and did not disclose cross-member data, misuse credentials, leak prompts, perform writes, or contradict approved policy.

## Evidence and remediation

Runner-original artifacts remain immutable. Formal reviews and T2 manifests are stored under the ignored directories named by the two run IDs. Both manifests revalidated all six artifact sizes and SHA-256 values; credential, private-key, bearer-token, email, and Taiwan-mobile pattern counts were zero.

DEC-BATCH-070 selects the first sufficient correction: reuse the existing Traditional-Chinese intent normalization and remove only a clarification that simultaneously asks about custom assembly and prebuilt form when the parsed intent is already `CustomBuild` and both purpose and maximum budget are complete. `product-search-v14` adds the observed output regression plus negative coverage for missing-purpose and unrelated clarifications. Dataset v1.0.13, support-v7, grader v1.1.9, models, timeouts, retries, APIs, data, and security thresholds remain unchanged.
