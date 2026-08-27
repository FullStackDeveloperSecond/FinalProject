# Functional Analysis

- Run ID: `20260826T020000Z-m12-review-p1-repair`
- Stage: `02-functional-analysis`
- Author/runtime: `Codex / GPT-5`
- UTC time: `2026-08-26T02:00:00Z`
- Input report: `01-requirements.md`
- Repository root: `D:\期末小組電商網站\Final\returns-m12-worktree`
- Base commit: `origin/dev` `64b0ef4`
- Working tree: tracked clean; `.workflow/` excluded.

## Affected behavior

### Guest attachment persistence and compensation

Update ReturnAttachment domain/EF mapping/Migration/snapshot so Member uploader uses nullable `UploadedByUserId`, Guest uploader uses nullable `UploadedByGuestOrderId`, and exactly one is present. `ReturnService.UploadAttachmentAsync` constructs the correct identity from `ReturnActor`. Wrap DB add after file storage in compensation: delete the new storage key on failure/cancellation, never delete pre-existing files, and avoid leaking key/path in API errors.

### Exact-set validation

Create one reusable validation rule for review approval and inspection. Compare distinct submitted PublicIds to the complete persisted item PublicId set. Cardinality and set equality must both hold. Reject before applying inspection fields, creating histories or changing status. Full-quantity approval must retain quantity equality checks after exact identity validation.

### Existing shared attachment endpoint

During rebase preserve `Controllers/PrivateAttachmentsController.cs` as the only GET `/api/v1/private-attachments/{id}/content`. Support lookup remains first; Return fallback runs only on not-found; each domain performs its own full authorization and unresolved/unauthorized resources return identical 404.

### C-19

Remove manual editable `orderItemPublicId` and Base64 RowVersion fields from the normal form. Until C-18 publishes eligible-item/order-row-version handoff, render a dependency notice and disabled action when route state lacks trusted handoff data. If existing integration navigation data is present, display item identity as read-only and submit it unchanged. Do not add a replacement order API.

### Admin direct refund path

Add review-level boolean state, default true. Map the same selected value to every line's `inspectionRequired`. Explain false means no physical return/inspection and transitions to AwaitingRefund. Ensure reset/refetch behavior does not silently revert a user's choice during an active form.

### Cooling-off calendar

Introduce an explicit timezone/calendar helper rather than `DeliveredAtUtc.Date.AddDays(8)`. Resolve `Asia/Taipei` cross-platform (IANA/Windows mapping or TimeZoneInfo conversion supported by the target runtime), calculate local delivery date plus 8 days at local midnight, then convert to UTC. Boundary: a request strictly before deadline succeeds; at/after deadline fails.

## Migration rules

Generate a new additive migration after rebasing latest dev; do not edit old applied migrations. Existing Member rows retain UploadedByUserId. Existing data cannot contain synthetic Guest IDs in merged dev because PR is unmerged; migration need not fabricate invalid backfill. Add indexes/FKs/check constraint matching project conventions and verify no pending model changes.

## Suggested implementation verification

- fixed SDK 10.0.303 restore/build with warnings as errors and format verify;
- application/domain focused tests already present after fixture updates;
- both frontend typecheck/lint;
- EF pending-model-changes check;
- regenerate OpenAPI/schema after final API composition if DTO/schema changes affect contract;
- `git diff --check`, marker search, status and commit report.

Codex will subsequently add the independent SQL Server Guest upload/rollback, HTTP Guest upload, duplicate/omitted item, timezone boundary and UI behavior regressions and run the full suite.

## Expected Claude report

Actual old/new base and head, conflict files/resolutions, migration name, production files changed, compensation/error decisions, build/typecheck commands, local commits, risks, and explicit `not pushed / PR not edited` confirmation.