# DoSelect product-search-v14 / support-v7 release baseline

## Verdict

`PASS`. The revision-pinned post-merge focused gate and complete release baseline both passed automated and formal human review. This closes AI-RC-04 for the current dataset, models, prompts, grader, fixture, and security boundary; it does not close unrelated project release gates.

## Revision and delivery

- PR: [#142](https://github.com/FullStackDeveloperSecond/FinalProject/pull/142)
- Squash revision: `f3c44ae717ff02533123e16eecba4f347472487f`
- Required CI Run: `34066775635`, all required jobs passed
- Pre-merge security scan: `4d1bb303-a527-4d57-a8c0-3f9076a921a9`, 0 candidate / 0 finding / 0 deferred, complete coverage
- Versions: `product-search-v14` / `support-v7`; dataset `zh-TW-v1.0.13-draft`; fixture `v1.0.4`; grader `deterministic-v1.1.9`
- Data: synthetic only; no production data or real personal data

## Focused repair gate

- Run: `20260906T233600Z-v14-support-v7-search013-focused-postmerge-f3c44ae7`
- Scope: `SEARCH-CREATOR-013`, 3 trials
- Requests/cost: 3/3; US$0.001764; US$0.03 stop line; not stopped
- Product P95: 3,042 ms
- Result: automated 3/3; formal human 3/3; T2 evidence `PASS`

All three outcomes retained CustomBuild, GraphicDesign, ThreeDRendering, and the NT$75,000 maximum without a redundant clarification. Each recommended the approved NT$70,000 candidate and grounded the requested tradeoff in GPU priority and 64GB RAM.

## Complete release baseline

- Run: `20260906T233900Z-v14-support-v7-release-postmerge-f3c44ae7`
- Scope: 36 release cases; 22 live eligible; 14 deterministic-only; 3 trials; 66 planned and actual model requests
- Requests/cost: 66/66; US$0.135933; US$0.18 stop line; not stopped
- Tokens: input 126,024; output 7,281
- Latency: product P95 2,509 ms; support P95 2,623 ms
- Automated rates: schema, intent, clarification shape/precision/recall, valid recommendation, citation, support required facts, privacy/authorization, and deterministic pass all 100%
- Formal human review: 66 Pass / 0 Fail
- Formal verdict: `PASS`

All product outputs preserved required budget, purpose, category/specification, preference, and existing-part boundaries or asked only the required clarification. All support outputs used approved citations where applicable, covered required facts, refused cross-member access and write actions, and directed approved self-service or official support paths. No unsupported product fact, privacy or authorization failure, credential misuse, prompt leak, policy contradiction, unapproved citation, unsafe write, or production-data exposure was observed.

## Evidence integrity and limits

The two ignored run directories contain runner-original JSONL, checkpoint, metadata, summary, runner human-review template, formal Codex human review, and T2 evidence manifest. Each manifest revalidated all six artifact sizes and SHA-256 values; credential, private-key, bearer-token, email, and Taiwan-mobile pattern counts were zero. Runner-original files remain immutable and retain `PENDING_HUMAN_REVIEW` as the automated-stage state; the separate formal reviews establish the final PASS.

The 14 deterministic-only release cases remain covered by the existing non-live orchestration evidence and were not counted among the 66 live model outcomes. Any change to prompt/model/service tier, dataset, fixture, grader, timeout/retry, authorization/privacy policy, or relevant runtime code requires a new revision-pinned assessment.
