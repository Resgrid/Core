# Checklists P1-M1 implementation

Status: implementation complete, pending deployment and the PostgreSQL environment check described below. This milestone is the free, on-demand web checklist workflow. P1-M2 scheduling, linked inventory targets and notifications; P1-M3 mobile/offline execution; P1-M4 reporting; and paid Readiness Pro workflows remain separate milestones.

Subsequent planning amendment: the [Workflow events and ADP contract](readiness-workflows-adp-contract.md) adds P1-M1 follow-up acceptance checks for the existing event/catalog baseline before release. Its field classification, derived-data, serialization and policy-transition checks have not been claimed as executed by this implementation report. Later milestones must deliver Workflow and ADP coverage with their features.

## Delivered behavior

- Template-to-draft creation generates new section/item identities. The editor supports sections, ordering, instructions, ten answer types, numeric units/bounds, explicit passing values, required/critical items, N/A reasons, failure evidence and bounded conditions referencing earlier items.
- Saving a draft, publishing a new immutable version, retiring a definition and deleting an unpublished draft are separate operations. Runs pin their published version. Editing a draft cannot change the target picker for the active published version.
- Department, unit, group/station and personnel targets are authorized on the server. Equipment templates work on demand using a required equipment identifier and a responsible department/unit/group/person; linked inventory asset routing remains P1-M2. Site/building/room descriptions and optional reported coordinates are protected run content.
- Progress saves support resumption and optimistic revisions. Client-generated run IDs make repeated starts idempotent. Terminal retries return the same result only for the same answers, metadata and evidence checksums. Changed submitted content conflicts.
- Missing answers never count as passes. N/A requires permission and a reason. Applicable weights determine the score; zero applicable weight has no score. Critical failures override the score. Optional handover notes in starter templates do not reduce it.
- PNG/JPEG photo and signature evidence uses image identification, bounded dimensions, metadata stripping/re-encoding, a 10 MB limit, scanning, SHA-256 integrity checks and ADP-protected bytes. Only a clean scan is accepted. Duplicate uploads are idempotent; submitted evidence is immutable. Metadata authorization precedes blob retrieval.
- Independent witness submission requires a different active member with results permission and the relevant group scope. The witness attests to the immutable submitted payload. A shared URL does not grant access. Failed-item events occur on submission; the completion event waits for the required witness.
- History, numbered published versions, target snapshots, completed answers, print, JSON export and separately authorized image downloads are available. Normal historical read permissions continue to work after the rollout flag or department module is disabled. Group scope is captured with the run, so moving/deleting a unit does not silently reassign its old results to a different group.
- Definition/run changes write audit rows in the same transaction as the affected data. Completed/failed events enter the durable domain outbox in that transaction, with dispatch after commit. Workflow variables/sample data and ID-only Eventing notifications are registered.
- Permission issuance covers the shared claims path used by MVC identities, cookie principals and JWTs, both policy registrations, both helper copies, and the permission administration screen. The existing Records permission catalog retains its original defaults and membership.
- Neutral and all ten supported language resource sets cover the checklist views and editor labels: en, de, el, es, fr, it, pl, sv, uk and ar. The localization correction adds Arabic, removes English-only interface labels and condition-code instructions, and corrects terminology. Each dictionary has 174 entries. Stored response codes and user-authored text remain unchanged; translated condition selectors submit the original codes.
- GDPR export includes associated completion, answer and evidence metadata through the existing recursive ADP redaction/manifest pipeline. Evidence downloads use the authorized checklist endpoint. The existing SQL Server department-deletion path deletes checklist children before their parents. The platform's pre-existing lack of PostgreSQL department deletion remains unchanged.

## Storage and identifiers

`M0191_AddChecklistWorkflow` and its PostgreSQL twin create seven tables: ChecklistDefinitions, ChecklistDefinitionVersions, ChecklistOccurrences, ChecklistCompletions, ChecklistCompletionItems, ChecklistCompletionFiles and DepartmentChecklistSettings. The last table reserves the department settings aggregate used by P1-M2.

Definitions and version schemas, target snapshots, completion metadata and typed answer payloads use a cataloged `Content` slot. Completion items remain relational rows keyed to their completion and stable item ID, with an indexed parent and a queryable failure marker. File bytes are a separate protected column. This reuses the existing dual-dialect, unit-of-work-aware repository plumbing rather than duplicating SQL across query configurations.

Composite tenant foreign keys, unique published version numbers, one completion per occurrence and one answer per item enforce aggregate integrity. A department row lock serializes commands in the transaction; revisions detect stale edits. Historical versions cannot be updated through the repository.

| Registry | Allocation |
|---|---|
| Migration | M0191; next physical migration M0192, within the existing readiness block |
| Permissions | ManageChecklists = 112; ViewChecklistResults = 113 |
| Workflow triggers | ChecklistCompleted = 67; ChecklistFailed = 68 |
| Eventing | ChecklistUpdated = 11 (IDs only) |
| ADP | Catalog version 14: seven Content slots and checklist file Data |
| Feature flag | Checklists.System, independent of Maintenance.WorkOrders |

Both flags remain seeded off. Checklists invoke no Readiness Pro billing gate. The previously configured USD 150/month Stripe and EUR 195/month Paddle Readiness Pro mappings remain for P2's dedicated purchase/reconciliation workflow.

## Validation and rollout

Final build and focused test results are recorded in the review's P1-M1 completion section. Tests cover the web editor in headless Edge, conditional execution, escaped content, CSRF, concurrent saves, immutable versioning, idempotency, failure evidence, independent witnesses, authorization, ADP denial, audit rollback, workflow contracts and the existing readiness/billing boundaries.

The real SQL Server fixture creates a uniquely named disposable database, applies M0191, rolls it down/up, checks tenant foreign keys, duplicate versions, blob-free list reads, transaction rollback and serialization of two writers. It deletes only its own database. The PostgreSQL fixture provides the same checks and is opt-in through `RESGRID_CHECKLIST_POSTGRES_TEST_CONNECTION`; Docker's Linux engine was unavailable during this work, so those checks remain an explicit environment validation gap.

For rollout, apply migrations through M0191, complete the ADP catalog-14 upgrade for protected departments, configure the existing ClamAV scanner (`AttachmentScanningConfig.Enabled`, Host and Port), and enable Checklists.System plus the department's checklist module. A scanner that reports Skipped, Pending or Rejected cannot satisfy evidence requirements. Refresh sign-in claims after deploying the new permission resources. Checklists can be reviewed at `/User/Checklists` once enabled.

Work is intentionally uncommitted. Suggested commit message: `feat(checklists): complete P1-M1 on-demand web workflow`.
