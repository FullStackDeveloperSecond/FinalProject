# DoSelect v10 / support-v4 post-merge focused verification

## Verdict

`PASS` for the two repaired paths only. This six-call run does not replace the complete Release baseline.

- Revision: `4f094ac7223a9705fc13b81879863d1d9c01e27c`
- Run ID: `20260906T183500Z-v10-support-v4-focused-postmerge-4f094ac7`
- Versions: `product-search-v10` / `support-v4`; dataset `zh-TW-v1.0.9-draft`; fixture `v1.0.4`; grader `deterministic-v1.1.7`
- Scope: `SEARCH-NOVICE-027` and `SUPPORT-POLICY-011`, three trials each
- Requests: 6 planned / 6 actual; cost US$0.013841 under the US$0.03 stop line
- Automated result: 6/6 deterministic pass; Schema, Intent, clarification, citation, and required-fact rates all 100% where applicable
- Formal human review: 6 Pass / 0 Fail

The three product-search answers used natural zh-TW purpose examples and did not expose the prior `Gaming` / `Office` / `Programming` / `VideoEditing` enum tokens. The three support answers stated NT$300 shipping, the NT$30,000 post-coupon eligible subtotal threshold, excluded fees and gifts, no convenience-store pickup, mandatory prepayment, and no cash on delivery.

## Evidence

The raw runner directory is Git-ignored and pinned to the revision above:

`FP.dev/.run/ai-evals/20260906T183500Z-v10-support-v4-focused-postmerge-4f094ac7`

| Artifact | SHA-256 |
|---|---|
| `case-results.jsonl` | `D4A2260D8907724727ACFE2B77D92CA2B75AF50CC5D503C8C9A64B31EBE3EC87` |
| `checkpoint.json` | `89BDB2598A005BDEA3ED2404747AE41F4DE9766EE609C497EC1F0BEFFA4C5A78` |
| `human-review.md` | `2394DB2AA402D8752FA3FA345F1666CAA9017DF4A933C7151B708D0C7195DE8A` |
| `run-metadata.json` | `4B8A17BF8F8109B491890FBA725389C3D830A3C0927FF875698619F020546B5F` |
| `summary.json` | `E0E1565A4660B1E813B832C0586D98D0ECB8D66B138A4E4366421BFFCF6CD8CD` |
| `human-review-codex.md` | `020D066471AC9B0C945B8CCD54B613A33196082E0A5926F51C9CBB1BACEA83A7` |

The T2 evidence manifest validates every listed size/hash. Sanitization found zero credential assignment, private-key, bearer-token, email, and Taiwan mobile patterns. No Production data or real personal data was used.

## Limitations

- This run covers only two repaired cases and contains no privacy/authorization case.
- The runner-original pending-human verdict remains immutable; the separate formal review completes it.
- Cost stopping is checked between cases, not as an absolute per-transaction cap.
