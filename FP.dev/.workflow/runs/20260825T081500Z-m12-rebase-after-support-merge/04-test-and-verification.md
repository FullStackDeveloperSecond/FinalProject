# Test and Verification Report

- Run ID: `20260825T081500Z-m12-rebase-after-support-merge`
- Stage: `04-test-and-verification`
- Author/runtime: `Codex / GPT-5`
- UTC time: `2026-08-25T09:18:00Z`
- Input reports: `01-requirements.md`, `02-functional-analysis.md`, Claude Code response attachment
- Repository root: `D:\期末小組電商網站\Final\returns-m12-worktree`
- Base commit: `27911e8225023ac7289a70dcefb1fa52800cc4b5`
- Working tree: tracked files clean after test commit; only `.workflow/` remains untracked.

## Objective

Independently inspect the Support/M-12 integration, cover the shared private-attachment fallback seam, and verify the rebased result before publication.

## Evidence

- Claude integration head inspected: `d2881c0`; final head after independent test: `8e5dd06`.
- Rebase history contains latest dev `27911e8` as ancestor.
- No conflict markers; `git diff --check` passed.
- Zero-warning solution build passed with fixed .NET SDK 10.0.303.
- Claude full suite evidence: Domain 200, Application 201, Infrastructure 266, API Integration 267; 934/934 passed.
- Codex Returns-focused suite: Domain 11, Application 72, Infrastructure 38, API Integration 150; 271/271 passed.
- Codex shared private-attachment HTTP suite: 13/13 passed.
- Customer Web and Admin Web typecheck/lint with zero warnings passed independently; Claude also reported 54/54 and 17/17 tests plus production builds.
- GitHub Actions on `8e5dd06`: 8/8 successful, including Backend, both frontends, Secret Scan, AI Evaluation Contract and CI Required.

## Test authored

`FP.dev/tests/DoSelect.Api.IntegrationTests/Support/PrivateAttachmentsHttpAcceptanceTests.cs` now verifies that a Support lookup miss falls back to a Return attachment and permits the owning Member to stream the expected bytes, content type and safe filename through the single shared route.

The initial test run failed because the test-only `DispatchProxy` base was declared sealed. This was a test-fixture defect, corrected by making the proxy inheritable; the same 13-test suite then passed. No production defect was introduced or repaired in this Codex pass.

## Commands and exit codes

- `dotnet format ...` and `dotnet build ... -warnaserror`: exit 0.
- focused `PrivateAttachmentsHttpAcceptanceTests`: 13/13, exit 0.
- `dotnet test DoSelect.slnx --filter FullyQualifiedName~Returns`: 271/271, exit 0.
- customer/admin `npm run typecheck` and lint `--max-warnings 0`: exit 0.
- GitHub check-runs query: all completed/success.

## Acceptance mapping

- Support and Returns shared attachment routing: proven by HTTP regression.
- Support/Returns route and DI union: inspected and build/typecheck verified.
- Generated contract union: inspected through Claude evidence and Required CI success.
- History safety: exact force-with-lease used; `.workflow/` excluded.

## Risks and unresolved items

No new blocker. Repository protection still requires reviewer approval. Existing documented boundaries remain: C-17 guest-cookie alignment, shared scheduler ownership, inventory mutation and refund execution.