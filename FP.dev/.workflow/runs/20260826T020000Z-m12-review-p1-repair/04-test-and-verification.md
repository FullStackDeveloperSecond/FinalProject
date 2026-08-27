# Test and Verification

- Run ID: `20260826T020000Z-m12-review-p1-repair`
- Stage: `04-test-and-verification`
- Author/runtime: `Codex / GPT-5`
- UTC time: `2026-08-26T02:12:00Z`
- Input reports: `01-requirements.md`, `02-functional-analysis.md`, Claude Code completion response, `20260824T063812Z-m12-complete-returns/03-implementation-result.md`
- Repository root: `D:\期末小組電商網站\Final\returns-m12-worktree`
- Base commit: `origin/dev@9089d5c`
- Verified head: `5b9dafc427c2fd9d788d5af0b2a0ad45bd76eb68`
- Working-tree summary: tracked clean; `FP.dev/.workflow/` remains untracked by policy.

## Objective

Independently verify the PR #42 repair after rebasing to the actual latest dev, add the smallest meaningful regression coverage, and prove the branch is ready to publish.

## Evidence and test additions

- Application regression tests verify Guest uploads persist `UploadedByGuestOrderId` with null `UploadedByUserId`.
- Application regression test forces metadata persistence failure and verifies the newly stored file is deleted while the original exception is rethrown.
- Review and Inspect tests submit duplicate item A while omitting item B and verify stable validation failure before status/history/inspection mutation.
- Asia/Taipei boundary theory covers delivery immediately before and after local midnight.
- Provider-backed SQL Server regression persists a Guest attachment and verifies the Orders FK round-trip with no ApplicationUser FK.
- HTTP regression sends a valid Guest cookie, obtains a real antiforgery token, uploads multipart content, and verifies the controller passes a Guest actor to `IReturnService`.
- Latest dev added `OrderItem.isCouponEligible`; three existing Return SQL fixtures were updated to compile against that merged contract.

## Commands and exit codes

- `git fetch origin` and `git rebase origin/dev`: exit 0; base advanced from `64b0ef4` to `351fbef`; no conflicts.
- Returns-focused Application tests: 78/78 passed.
- Guest attachment SQL Server focused test: 1/1 passed.
- Guest attachment HTTP focused test: 1/1 passed.
- `dotnet restore DoSelect.slnx`: exit 0.
- `dotnet build DoSelect.slnx --no-restore -warnaserror`: exit 0, 0 warnings, 0 errors.
- `dotnet format DoSelect.slnx --verify-no-changes --no-restore`: exit 0.
- `dotnet ef migrations has-pending-model-changes ... --no-build`: exit 0, no pending model changes.
- Full backend/SQL Server tests: Domain 264 + Application 224 + Infrastructure 303 + API Integration 276 = 1067/1067 passed.
- customer-web: typecheck, lint with zero warnings, 54/54 tests, production build and production dependency audit all passed; 0 vulnerabilities.
- admin-web: typecheck, lint with zero warnings, 17/17 tests, production build and production dependency audit all passed; 0 vulnerabilities.
- Live OpenAPI export/generate completed from the rebased API; generated JSON and TypeScript schema were byte-equivalent after Git normalization (no committed diff).
- `git diff --check`: no content errors. One temporary blank-at-EOF warning in the new HTTP test was normalized by the commit.
- Conflict-marker search: no matches outside excluded workflow reports.

## Diagnostic correction

The first timezone test run expected one day too late. Repository policy says local delivery date plus eight days at Asia/Taipei midnight. The test expectation was corrected from UTC 8/28 16:00 to UTC 8/27 16:00 for a Taipei 8/20 delivery; product code was unchanged. The corrected theory passed both sides of the local-midnight boundary.

## Acceptance-criteria mapping

- Guest FK and mutually exclusive uploader identity: passed at Application and SQL Server provider levels.
- Storage compensation: passed with original exception preservation when cleanup succeeds.
- Exact-set validation before mutation: passed for both Review and Inspect.
- Shared attachment route: retained through rebase; existing Support/Return HTTP suite and full API suite pass.
- C-19 dependency state and admin inspection toggle: typecheck, lint, tests and production builds pass.
- Asia/Taipei calendar deadline: explicit boundary tests pass.
- Latest dev compatibility: second rebase reached `9089d5c`; Program.cs preserves Invoicing and Returns registrations; full merged suite passes.

## Risks and unresolved items

- The provisional Guest cookie name remains pending formal C-17 implementation; no second cookie system was introduced.
- C-19 remains dependency-gated until C-18 supplies eligible item IDs and order RowVersion.
- The overdue cancellation use case remains callable but unscheduled per D1.
- Physical inventory restoration and refund execution remain outside M-12.
