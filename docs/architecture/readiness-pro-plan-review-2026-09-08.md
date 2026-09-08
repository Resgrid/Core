# Checklists and Readiness Pro: implementation review

Reviewed 2026-09-08 against `../int-Coordination/docs/architecture/checklists-maintenance-workorders-design.md`, the identifier registry, and the current Core checkout.

## Decision

The original plan is a strong starting point, but is not complete without the requirements below. With this amendment it is suitable for incremental implementation. This is design verification, not certification of operational readiness or a claim that the planned features have shipped.

The [Workflow events and ADP contract](readiness-workflows-adp-contract.md), added 2026-09-08 at the user's request, is mandatory across all milestones. It supersedes older raw event/audit payload guidance and adds explicit P1-M1 follow-up verification before release. Event production and field/data-flow protection must be delivered with each feature, including models that may receive incidental PII/PHI.

- **Checklists are free for every department**, including templates, authoring, scheduling, completion, failure evidence, notifications, history and checklist reports. They require neither a paid base plan nor Readiness Pro.
- **Readiness Pro** is the new **monthly** add-on covering maintenance, work requests, work orders, preventive maintenance, parts/labor, approvals and maintenance reports. It is a department subscription independent of the base plan's billing interval. User-confirmed pricing: **USD 150/month for US via Stripe; EUR 195/month for EU via Paddle**. Matching test/live monthly provider product/price identifiers and verified purchase/reconciliation flows are required before checkout opens; do not invent a free trial, annual option or activation from a pending checkout.
- `PlanAddonTypes.ReadinessPro = 3`; `PTT = 1` and `ADP = 2` are already used and remain unchanged.
- Independent feature flags: `Checklists.System` and `Maintenance.WorkOrders`. Both seed off. A flag controls rollout; the second flag does **not** confer a paid entitlement. Neither depends on the other. Maintenance may operate without Checklists; failed free checks remain saved and visible without the add-on.
- Reuse `MaintenanceDisabled`. Add `ChecklistsDisabled` to serialized `DepartmentModuleSettings` using new protobuf member 23; there is **no DepartmentModuleSettings table/column migration**. Apply settings, flags, department scope and permissions on the server, including workers/integrations, not only navigation.

## Operational coverage and acceptance

Every row is required before the corresponding milestone is considered complete. Suggested checklists are starting points to adapt to the organization's equipment, adopted standards, manufacturer procedures and jurisdiction; templates must not claim regulatory certification.

| Users / workflow | Existing coverage | Required completion of the design | Acceptance milestone |
|---|---|---|---|
| Fire / EMS apparatus, boats, aviation and special operations | Rig checks, SCBA/PPE, unit and serialized equipment history | Pre-use/post-use/event checks; manufacturer-specific readings and units; critical defects override any aggregate passing score; quarantine and qualified return-to-service authorization | P1-M1/M2, P2-M2 |
| EMS bags, medications and biomedical equipment | Par quantities, AED checks, two signature items | Lot/serial/expiry and temperature excursions via inventory; calibrated instruments; independent authenticated witness identity for controlled counts, discrepancy escalation and restricted evidence. Two drawings from one user are not a dual attestation. Do not collect patient records in generic checks | P1-M1/M3, P2-M2 |
| SAR / wildland / volunteer organizations | Rope caches, personal packs, agency pre-use templates | Post-deployment rehabilitation and decontamination; retirement/inspection limits; no-shift scheduling; equipment checked out to individuals; pooled kit and borrowed-resource readiness | P1-M2/M3, P2-M2 |
| Emergency management / EOC / shelters | Department and station targets, readiness packet | EOC activation/handover/demobilization, shelter opening/accessibility, cache deployment/return, communications exercises, emergency power and continuity checks; site/group ownership, operational-period schedule and overdue escalation | P1-M1/M2/M4 |
| Industrial / construction / warehouses | Workplace audits, forklift pre-op, PM | Meter/odometer/hour/cycle-driven PM with calendar-or-meter whichever-first rules; condition thresholds; calibration/certification expiry; job hazards, isolation/permit references, qualified technicians, shift handover and independent release approval | P2-M1/M2/M3 |
| Businesses / facilities / campuses / fleets | Facility issues, vehicle inspections, costs | Site/building/room or free-text location without an inventory dependency; opening/closing/continuity checks, recurring vendor services, warranty/vendor/contact links, service documentation, cost centers and downtime windows | P1-M1/M2, P2-M1/M4 |
| Small organizations | Anyone in scope can complete, text parts fallback | Manual checks and work requests without shifts, inventory or a dedicated mechanic; simple defaults, accessible forms, printable/exportable evidence and clear ownership | P1-M1/M3, P2-M1 |
| Large / multi-site organizations | Role routing, digests, paging | Group/site-scoped access, bulk assignment/import with preview and row errors, technician queues, approval thresholds, cost currency, service-level targets by priority and business calendars, filtered/export limits | P1-M2/M4, P2-M1/M4 |

## Required domain corrections

### Checklists and evidence (free)

1. Support explicit Pass, Fail and Not Applicable outcomes, with a configured reason for N/A. Missing answers must never become passes. Calculate score over applicable scored answers; zero applicable weight produces no score, never 100%. A critical failure makes the run fail regardless of score. Separate completed, passed, late, missed, skipped and exempt states in reports.
2. Item definitions need instructions, response type, stable identity, requiredness, criticality, allowed N/A, explicit pass semantics (including yes/no and selected options), numeric unit/range, evidence requirements and conditional visibility/requiredness. Never execute arbitrary expressions from definitions. Validate definition size/depth, unique item IDs, ranges, positive weights and valid references.
3. Published definitions are immutable snapshots. Draft editing/publishing/retiring are distinct. Copying a template produces a department-owned draft with new definition/section/item IDs; changing catalog content never rewrites a department definition. Pin all started runs and generated occurrences to the published version. Inactive/retired targets stop future generation without removing history.
4. Keep authenticated actor, target snapshot, server receive/submit time and client capture time separately. Device clocks and GPS are evidence, not authorization. Independent witness requirements use separate authorized users, timestamps and an explicit attestation. Restrict sensitive signatures/counts and account for the ADP protected-field catalog before persisting them.
5. Client GUIDs are scoped/validated for department and occurrence. An identical retry returns the original result; a changed payload under the same completed ID returns a conflict. Use optimistic concurrency for drafts/progress, one terminal completion per occurrence, uniqueness in both databases, and transactional answer/occurrence/completion/audit writes. Emit downstream effects through the existing durable outbox after commit.
6. Offline runs preserve published definitions, target/assignment snapshots and attachment queues. Membership revoked before sync blocks the mutation without discarding the local work. Reject stale conflicting submissions explicitly; do not silently overwrite another user's completion. Scan and authorize uploads before they count as required evidence; apply size/type limits and existing storage/protected-data rules.
7. Define recurrence in department local time with UTC storage, a documented DST gap/fold rule, month-end clamping, leap-year behavior and schedule effective dates. Materialized rows include target in their occurrence uniqueness key when one schedule expands to multiple targets. Grace windows, late completion, excused skips, reassignment, asset retirement and worker outage catch-up preserve what was actually due. Report denominator rules must be explicit.
8. Notification deduplication is per recipient/channel/occurrence or digest window. Resolve current asset ownership at send time, honor preferences/quiet hours, support an explicitly configured emergency exception, and prevent worker retries from duplicating alerts. A module/rollout pause freezes new work/notifications without manufacturing missed checks for the paused period.
9. Failure notes, photos, critical defect indicators and checklist alerts remain free. Work order creation is a separate authorized paid effect; a failed billing lookup must never roll back a valid free completion. Paid effects need unique source occurrence/item keys and retryable outbox processing.

### Maintenance and work orders (Readiness Pro)

1. Add work-request triage outcomes (accepted, rejected with reason, duplicate linked to canonical order), reopen with reason, structured hold/cancel reasons, assignment acceptance, multiple contributors and restricted external vendor details. Centralize the transition matrix and enforce field/role preconditions at the service boundary. Sequence human numbers atomically per department/year, with a unique index.
2. Model completion separately from verification/closure. Closure requires resolution/cause, applicable test/inspection evidence and authorized verification for safety-critical work. For hazardous work, record procedure/version, permit/isolation references and authorized personnel. Application checkboxes do not execute or replace physical energy-isolation procedures.
3. **Remove automatic unconditional restoration of the previous unit/asset state.** Track every active defect/maintenance hold and its source, reason and release authorization. Closing one order cannot clear another open hold, a later dispatcher status change, retirement, or a manual safety restriction. Use conditional state updates and a qualified return-to-service step. Billing expiry or disabling a flag must never clear a hold.
4. PM supports calendar, usage and condition triggers, units, source/timestamp of readings, meter replacement/reset, interval baselines, due-soon and overdue, fixed-vs-completion-based scheduling and blackout/service windows. Generate at most one work order per schedule/target/cycle, even across concurrent workers. Record rescheduling/deferral approvals, without erasing original due dates.
5. Parts flows include reservation/issue/consume/unused return, serial/lot/expiry when available and idempotent ledger links. Correct consumed parts with a linked inventory reversal, never deletion or an unrelated Adjust. Record labor time, rate snapshots, vendor charges, estimated/approved/actual cost, currency and cost center using decimal quantities/money. Keep free-text fallback and indicate when stock was not posted.
6. Add task steps/checklists on a work order, skills/role eligibility, attachments, vendor/warranty/reference links, downtime intervals, response/repair SLAs, priority escalation, customer/requester updates and assignment history. Reports distinguish elapsed downtime, active repair time, waiting time, PM compliance, repeated failures, backlog aging and costs.
7. Historical access/export must survive subscription cancellation, module disablement and feature rollout pause, under normal authorization and retention rules. Define a narrow safety-release operation for existing holds after expiry; it must not authorize new maintenance work. Commercial suspension does not delete or rewrite evidence. Plan separate read/export and create/update authorization methods when those data surfaces ship.

## Billing and access contract

- Trust reconciled department-scoped entitlements with exact add-on IDs, effective start and exclusive paid-through end. Missing/null/malformed billing responses, wrong department/type, future/expired dates and billing transport failures deny new paid operations. Do not rely on list count or feature flag alone.
- Core's generic `SubscriptionsService` helpers can return synthetic `SYSTEM`/forever entitlements when Billing API is unconfigured. Readiness Pro must reject this behavior explicitly; do not change PTT/ADP behavior as part of this feature.
- Readiness Pro renews monthly even if the base plan is annual. Do not use `PlanAddon.GetEndDateFromNow()`'s generic 7-day grace/annual-plan fallback to grant paid access. A successful reconciled payment supplies actual monthly period boundaries; checkout creation alone grants nothing.
- Cancellation at period end preserves access through the already paid interval. Immediate cancellation/revocation shortens that interval. Billing webhook/API reconciliation must distinguish these cases, verify signatures, deduplicate/replay safely, handle out-of-order events and invalidate entitlement caches. No new implicit grace period is approved in this amendment.
- One department-level subscription, without invented per-seat pricing. Buying/managing it requires existing department billing authorization. Test/live Stripe and Paddle mappings must be isolated. No zero-cost placeholder product or guessed provider identifiers. Readiness Pro is not offered for purchase until the external billing implementation is verified.
- Acceptance: all combinations of both flags, both module settings, free/paid base plan, no addon, PTT-only, ADP-only, active/future/expired/cancel-at-end Readiness Pro and unconfigured/unavailable billing. Checklists must make **zero billing calls**. Maintenance-only operation must work with the Checklists flag off. Recheck entitlement at writes/worker effects, not only session login.

## Integration and identifier reconciliation

- Live migrations end at M0188 in both dialects. Use **M0189** for the initial readiness flag seed. The registry currently reserves M0189-M0195 for this plan; subsequent migrations take the next physical number and must recheck the registry at authoring time. Do not reuse the original M0122-M0128/M0131-M0137 references.
- Current registry reservations: checklist/work-order permissions **112-115**, workflow triggers **67-73**, workers **63-66**. These are reserved, not implemented by the initial slice. Do not use the plan's stale values. Extend allocation tests to pin addon 3 and check duplicate addon values.
- Services use the existing Autofac registration/module pattern; current services/controllers support constructor injection. Do not introduce another service locator or persistence framework. SQL remains dual Dapper/FluentMigrator.
- Inventory, Unit, Responder, billing and coordination repositories are separate deliverables. Inventory absence must not prevent unit/personnel/site checklists or manual maintenance requests. Validate soft references against the owning department, preserve historical asset location/serial snapshots and do not infer call-time equipment from today's location.
- Reuse RMS's immutable PDF + evidence-manifest capture contract, source authorization and checksums. Plan ADP field ownership, purge/export/legal-hold semantics, access audit, classification and attachment scanning before checklist/work-order persistence. Do not create legacy Logs after Records cutover. Hydrant/prevention inspections already owned by RMS stay there; readiness checks link to them without duplicating violations or official inspections.
- Standard references need edition metadata and local review dates. The old plan's NFPA 1911 chapter references must not imply the current consolidated NFPA 1910 is identical, or that a short starter template implements a standard in full.

## Delivery and release gates

**Initial implementation slice:** independent flag seeds and keys; addon identity/monthly interval semantics; server access service; serialized checklist module toggle; free catalog with sector coverage; authenticated web catalog and v4 catalog/access endpoints; focused billing/gating/catalog/API tests. Seeded flags remain off. This slice does not provide persisted checklist execution or a purchasable maintenance product.

**Current state:** P1-M1 implementation is complete and uncommitted: definition builder, immutable publication/versioning, authorized on-demand web runs, evidence, independent witnesses, history/export, permissions, ADP, audit transactions and durable workflow events. See the [implementation report](checklists-p1-m1-implementation.md) and completion validation below. Rollout flags remain off; PostgreSQL runtime verification is still pending.

**Additional release gate:** the subsequent [Workflow/ADP amendment](readiness-workflows-adp-contract.md#milestone-acceptance-gates) requires a field-by-field and serialized-sink audit of this baseline, including derived scores/outcomes, safe Workflow projections and redaction metadata. Those new checks are planned, not covered by the earlier passing test counts. Complete the P1-M1 follow-up gaps before release and the corresponding checks in each later milestone.

**Next milestone:** P1-M2 scheduling/targets/notifications/calendar, then P1-M3 offline mobile and P1-M4 reports. These are not part of the completed P1-M1 scope. Implement the expanded P2 milestones after free Checklists has shipped. Meter/condition PM, approval and safety-release requirements belong to Phase 2 GA, not a stretch backlog.

Required proofs beyond existing milestone tests: concurrent edit/submit/worker races on both databases; cross-department object IDs and uploads; critical-failure and N/A score cases; end-to-end cancellation/failure/recovery; two simultaneous asset holds plus an intervening dispatch status change; historical asset moves; verified immutable records after template edits/retirement; outage catch-up/DST/leap days; large-department paging/digests/export bounds; database up/down/up on disposable databases; browser accessibility and both apps offline-to-online. Do not claim these pass until executed.

## Sources checked for the review

- [USFA/FEMA: NIMS resource management](https://www.usfa.fema.gov/a-z/nims/managing-resources.html): preparedness, resource tracking through demobilization and reporting support the EM coverage additions.
- [OSHA recommended safety and health practices](https://www.osha.gov/shpguidelines/docs/OSHA_SHP_Recommended_Practices.pdf): documented inspections and verifying corrective actions support auditable failure/closure workflows.
- [OSHA powered industrial trucks, 1910.178](https://www.osha.gov/laws-regs/regulations/standardnumber/1910/1910.178) and [NIOSH daily inspections](https://www.cdc.gov/niosh/docs/wp-solutions/2022-100/): pre-use/shift examination and unsafe-equipment handling support event checks and explicit safety release.
- [OSHA hazardous energy control, 1910.147](https://www.osha.gov/laws-regs/regulations/standardnumber/1910/1910.147): authorized-person and verification requirements support procedure references and role-controlled maintenance approval.
- [NFPA publications catalog](https://link.nfpa.org/all-publications/655/2012): lists NFPA 1910 (2024) for in-service emergency vehicles; use edition-specific references reviewed by the adopting organization.

## Initial slice implementation and verification (2026-09-08)

Implemented in Core, left uncommitted:

- M0189 flag seeds in SQL Server and PostgreSQL; independent keys, both off. Down deliberately preserves operator-managed rows because a guarded Up cannot establish ownership of existing keys.
- Readiness Pro add-on 3 and monthly interval estimation independent of the base plan; configured USD 150 Stripe / EUR 195 Paddle monthly offers. No provider product or payment row is fabricated, and the API explicitly reports checkout unavailable.
- Autofac-registered `ReadinessAccessService`, rejecting missing billing configuration, wrong department/addon/type, missing/future/expired dates, synthetic SYSTEM/forever payments, null payloads and billing exceptions. Cancellation remains active through the reconciled paid-through date. Free checklist access never invokes billing.
- Serialized `ChecklistsDisabled` protobuf member 23, department settings controls and gated navigation. Existing MaintenanceDisabled is reused.
- 28 immutable starter templates covering the requested sectors; stable section/item GUIDs, critical-check metadata, failure-note requirements and witness metadata. Authenticated web gallery/preview plus v4 `Checklists/GetChecklistTemplates`, `Checklists/GetChecklistTemplate` and `Readiness/GetAccess`. Server service gates apply to direct catalog reads. Neutral English UI resources provide fallback; translations and localized template content remain for P1 completion.

Validation executed:

- `dotnet build Resgrid.sln --no-restore --verbosity quiet`: **passed**, 0 errors, 43 existing compatibility/obsolete API warnings. This includes compiled Razor views.
- Focused tests in `ReadinessAccessServiceTests`, `ChecklistTemplateServiceTests`, `ReadinessApiTests` and `IdentifierAllocationTests`: **54 passed**, 0 failed. Includes both migration assemblies' numbering/parity checks, the preserved addon IDs, invalid/unpaid entitlement cases, independent flags/modules, catalog structure/search and department-scoped API contracts.
- `git diff --check`: passed.
- **Not executed:** SQL Server/PostgreSQL Up/Down/Up against live databases (no isolated test connections configured; Docker daemon unavailable), authenticated browser smoke tests, mobile checks or billing-provider checkout/reconciliation. No live database migrations or feature activation were performed.

At the end of this initial slice, the remaining P1-M1 builder/versioned persistence/on-demand execution was next. It is now implemented as recorded below. Paid work-order lifecycle, PM, integrations and purchase/reconciliation remain P2; the initial gate/catalog slice is not a GA declaration.

## Provider mapping follow-up (2026-09-08)

The user supplied these production mappings for the previously confirmed monthly prices:

| Provider / region | Monthly price | Product ID | Price ID |
|---|---|---|---|
| Stripe / US | USD 150 | `prod_VDtkPNAa2qNBx3` | `price_0UDRwaqJFDZJcnkVnYP8bAcd` |
| Paddle / EU | EUR 195 | `pro_01m20xwmzpnkxzp7mm7nwwxp7p` | `pri_01m20xy5x54j0sp4mcydcm4q6m` |

- M0190 (both dialects) seeds the Stripe PlanAddon row: ID `8a82f517-13db-4950-a514-d990248a67e6`, AddonType 3, Cost 150, ExternalId above, no base PlanId and empty TestExternalId. An existing Readiness Pro catalog is preserved. This is the Stripe/USD catalog amount; Paddle/EUR uses its own price mapping and EUR 195 offer.
- Paddle price selection follows the existing PaymentProviderConfig convention: `PaddleReadinessProAddon`, `PaddleReadinessProAddonTest`, `GetPaddleReadinessProAddonPriceId()`.
- Test/sandbox IDs are not supplied and remain unset. Readiness Pro's Stripe and Paddle selectors never fall back from test mode to production. Existing PTT and ADP key-selection behavior is unchanged.
- Legacy BuyAddon GET/POST and the PTT-specific subscription service methods reject Readiness Pro, preventing the new catalog row from opening the wrong purchase flow. Dedicated checkout remains unavailable until P2-M1 purchase/reconciliation is implemented and verified.
- Provider account metadata (monthly interval, currency, amount, product ownership) still needs checking in the dedicated checkout implementation. The supplied IDs have been recorded, not queried or modified at Stripe/Paddle. No live migration or purchase was executed.
- M0190 consumed the second migration in the existing readiness reservation. P1-M1 subsequently consumed M0191; the next physical migration is now M0192. Recheck before authoring further migrations.

Provider mapping validation: full solution build passed (0 errors, 41 existing warnings); 75 focused readiness, mapping, allocation, payment-configuration and ADP billing-authorization tests passed. Database migration round-trips and live provider checks remain unexecuted. Changes are uncommitted.

## P1-M1 completion (2026-09-08)

The free on-demand web workflow is implemented. The [implementation report](checklists-p1-m1-implementation.md) records the delivered behavior, persistence choices, deployment prerequisites and remaining phase boundaries. This completion supersedes the earlier initial-slice status; it does not declare all of Phase 1 or Readiness Pro shipped.

- Full solution build: `dotnet build Resgrid.sln --no-restore --verbosity quiet` passed with **0 errors and 6,379 warnings**. Warnings remain in the solution, including the database fixture's use of the existing obsolete migration-source test pattern.
- Focused regression run: **226 total, 222 passed, 4 skipped, 0 failed**. Coverage includes checklist validation/services/authorization, readiness and billing boundaries, identifier allocations, ADP catalog/GDPR export, Records permission/claim regressions, workflow contracts and browser scripts.
- SQL Server: four real database checks passed on a dedicated disposable LocalDB instance, including M0191 Up/Down/Up and repeated Up, tenant foreign keys, duplicate versions, blob-free metadata lists, transaction rollback and two-writer serialization. Each fixture database is removed after the test.
- Browser: the checklist editor/run scripts passed in headless Edge, covering stable item identities and ordering, escaped content, CSRF, conditional answer clearing, serialized saves, conflict preservation and edits made during an in-flight save. Existing browser script tests also passed. A deployed authenticated application walkthrough was not performed.
- PostgreSQL: the corresponding four real database checks were skipped because no test connection was configured and Docker's Linux engine was unavailable. Both migration projects compile and allocation/parity tests pass; this does not substitute for PostgreSQL execution.
- `git diff --check` passed. Resource XML was validated across neutral and nine language files. Starter-template content localization and later-phase mobile/reporting work are not claimed complete.

M0191 creates the seven checklist tables in both dialects. Implemented allocations are permissions **112-113**, workflow triggers **67-68**, `EventingTypes.ChecklistUpdated = 11` and **ADP catalog 14**. Workers **63-66**, remaining triggers **69-73** and work-order permissions **114-115** remain reserved for later milestones. The next physical migration is **M0192**, within the existing readiness block; no subsequent block moves.

Both feature flags remain off and no production migration, deployment, purchase or provider-account mutation was performed. Checklists have no billing dependency. Readiness Pro remains USD 150/month via Stripe and EUR 195/month via Paddle, with the supplied production IDs preserved and dedicated checkout/reconciliation pending P2-M1.

Suggested commit message: `feat(checklists): complete P1-M1 on-demand web workflow`. Work is left uncommitted for human review.

## Localization correction (2026-09-08)

The supported-locale registry includes Arabic, which the initial P1-M1 resources omitted. The checklist resource family now includes the neutral dictionary and all ten supported languages: English, German, Greek, Spanish (Latin America), French, Italian, Polish, Swedish, Ukrainian and Arabic. Each has 174 matching keys with real translations, including interface choices, status names, permission labels, confirmations, signature instructions, accessibility labels and progress messages. German passing-answer terminology, Greek authentication wording, Swedish performer terminology and Ukrainian phrasing were corrected.

The condition editor displays translated choices and preserves the existing stored response codes. It no longer asks users to type English boolean/pass-fail codes. Completion history localizes those codes, and user-authored names/options remain verbatim. Shared spellings that are correct in both languages, product names such as Readiness Pro and format names such as JSON remain intentional. This correction covers the dictionaries and their interface consumers; starter-template content and server validation message localization remain separate from those dictionaries.

Validation: the affected test project and its web/Razor dependencies build with zero errors. All 15 focused tests pass, including one compiled-resource/placeholder/format-argument check per supported locale and all browser scripts. The new browser check exercises the editor and runner in every supported language, preserving condition/answer codes and user-authored text. The project memory now requires supported-locale coverage and genuine translations instead of English placeholders. Changes remain uncommitted.

## Workflow and ADP planning amendment (2026-09-08)

The user requires Workflow engine events and ADP coverage wherever readiness data can contain PII/PHI. The [delivery contract](readiness-workflows-adp-contract.md) now defines event timing and trigger mappings, transaction/outbox and consumer deduplication rules, pre-serialization redaction, protected-model/data-flow inventory, worker/source-reference behavior, and acceptance checks for every milestone. Checklists remain free, Readiness Pro remains a separate monthly product, and durable department ADP state continues to govern protection independently of billing and rollout flags.

The amendment was checked against the current ADP plan and the P1-M1 event/catalog implementation. It changes documentation only; it does not claim implementation or runtime verification of the new acceptance gates. No new numeric allocation, migration, feature activation or deployment is included.
