using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// NERIS incident report lifecycle (RMS plan sections 4.2, 5.2.1, 5.3, 5.5; RMS-2). Mirrors RecordsService for
	/// the second aggregate: the same state machine (RmsLifecycle), the same revision/audit/projection/group-scope
	/// tables, the same outbox. What is specific here: source-aware prefill from the Call with an RmsSourceFact
	/// per prefilled value, local validation against the pinned NERIS contract before finalize, an attestation
	/// signature bound to the revision checksum, and a submission row per revision with its own idempotency key.
	/// </summary>
	public class IncidentReportsService : IIncidentReportsService
	{
		public const string AttestationStatementVersion = "1";
		public const string IncidentAggregate = "RmsIncidentReport";
		public const string DispatchCommentFactPrefix = "dispatch.comment.";
		public const string NumberPrefix = "INC";

		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsSourceFactsRepository _facts;
		private readonly IRmsUnitResponsesRepository _units;
		private readonly IRmsIncidentTypesRepository _types;
		private readonly IRmsActionTacticsRepository _tactics;
		private readonly IRmsAidsRepository _aids;
		private readonly IRmsLocationsRepository _locations;
		private readonly IRmsNarrativesRepository _narratives;
		private readonly IRmsValidationIssuesRepository _issues;
		private readonly IRmsSubmissionsRepository _submissions;
		private readonly IRmsSignaturesRepository _signatures;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IRmsRecordGroupScopesRepository _scopes;
		private readonly IRmsRecordSharesRepository _shares;
		private readonly IRmsRecordSearchProjectionsRepository _projections;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUserProfileService _profiles;
		private readonly IPersonnelRolesService _roles;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _calls;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IUnitOfWork _unitOfWork;
		private readonly INerisProfileService _neris;
		private readonly INerisMappingService _mapping;
		private readonly INerisValidationService _validation;

		public IncidentReportsService(IRmsIncidentReportsRepository reports, IRmsSourceFactsRepository facts, IRmsUnitResponsesRepository units,
			IRmsIncidentTypesRepository types, IRmsActionTacticsRepository tactics, IRmsAidsRepository aids, IRmsLocationsRepository locations,
			IRmsNarrativesRepository narratives, IRmsValidationIssuesRepository issues, IRmsSubmissionsRepository submissions, IRmsSignaturesRepository signatures,
			IRmsRevisionsRepository revisions, IRmsAccessAuditsRepository audits, IRmsRecordGroupScopesRepository scopes, IRmsRecordSharesRepository shares,
			IRmsRecordSearchProjectionsRepository projections, IDomainEventOutboxService outbox, IDepartmentSettingsService settings,
			IDepartmentGroupsService groups, IUserProfileService profiles, IPersonnelRolesService roles, IUnitsService unitsService, ICallsService calls,
			IDepartmentDataProtectionService dataProtection, IUnitOfWork unitOfWork, INerisProfileService neris, INerisMappingService mapping,
			INerisValidationService validation)
		{
			_reports = reports;
			_facts = facts;
			_units = units;
			_types = types;
			_tactics = tactics;
			_aids = aids;
			_locations = locations;
			_narratives = narratives;
			_issues = issues;
			_submissions = submissions;
			_signatures = signatures;
			_revisions = revisions;
			_audits = audits;
			_scopes = scopes;
			_shares = shares;
			_projections = projections;
			_outbox = outbox;
			_settings = settings;
			_groups = groups;
			_profiles = profiles;
			_roles = roles;
			_unitsService = unitsService;
			_calls = calls;
			_dataProtection = dataProtection;
			_unitOfWork = unitOfWork;
			_neris = neris;
			_mapping = mapping;
			_validation = validation;
		}

		#region Start / read

		public async Task<IncidentReportAggregate> StartFromCallAsync(int departmentId, string userId, int callId, RmsOriginClient origin = RmsOriginClient.Web, CancellationToken cancellationToken = default)
		{
			if (callId <= 0) throw new ArgumentException("A call is required.", nameof(callId));
			if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user is required.", nameof(userId));

			var profile = await _neris.GetProfileAsync(departmentId);
			var entity = ReportingEntityFor(departmentId, profile);

			// SingleAuthoritative (plan 5.2.1): a second start returns the existing report, never a duplicate.
			var existing = await _reports.GetByCallAsync(departmentId, callId, entity);
			if (existing != null && !existing.DeletedOn.HasValue)
				return await GetAsync(departmentId, existing.RmsIncidentReportId, false);

			var call = await _calls.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId)
				throw new ArgumentException($"Call {callId} does not belong to this department.");
			call = await _calls.PopulateCallData(call, true, false, true, false, true, false, false, false, false) ?? call;

			var now = DateTime.UtcNow;
			var reportId = Guid.NewGuid().ToString();
			var authorGroup = await _groups.GetGroupForUserAsync(userId, departmentId);
			var facts = new List<RmsSourceFact>();

			var report = new RmsIncidentReport
			{
				RmsIncidentReportId = reportId,
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				CallId = callId,
				ReportingEntityId = entity,
				DefinitionKey = RmsDefinitionKeys.NerisIncidentReport,
				DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion,
				ProfileVersion = _neris.ContractVersion,
				LifecyclePreset = (int)RmsDefinitionKeys.LockedDefaultPreset,
				State = (int)RmsRecordState.Draft,
				DraftReference = NewDraftReference(),
				IncidentNumber = string.IsNullOrWhiteSpace(call.Number) ? null : call.Number,
				DispatchIncidentCode = string.IsNullOrWhiteSpace(call.Type) ? null : call.Type,
				CallCreatedOn = call.LoggedOn,
				CallAnsweredOn = call.LoggedOn,
				IncidentClearedOn = call.ClosedOn,
				StationGroupId = authorGroup?.DepartmentGroupId,
				AuthorUserId = userId,
				OwnerUserId = userId,
				OriginClient = (int)origin,
				CreatedOn = now,
				CreatedByUserId = userId,
				ModifiedOn = now,
				ModifiedByUserId = userId,
				RowVersion = 1
			};
			facts.Add(Fact(report, NerisFactKeys.IncidentNumber, RmsSourceKind.Dispatch, "Calls", "Call", callId.ToString(), call.Number, call.LoggedOn, now));
			facts.Add(Fact(report, NerisFactKeys.IncidentCode, RmsSourceKind.Dispatch, "Calls", "Call", callId.ToString(), call.Type, call.LoggedOn, now));
			facts.Add(Fact(report, NerisFactKeys.CallCreate, RmsSourceKind.Dispatch, "Calls", "Call", callId.ToString(), Iso(call.LoggedOn), call.LoggedOn, now));
			// Resgrid holds no PSAP answered time; the create time stands in and is marked Derived so the author sees it.
			facts.Add(Fact(report, NerisFactKeys.CallAnswered, RmsSourceKind.Derived, "Calls", "Call", callId.ToString(), Iso(call.LoggedOn), call.LoggedOn, now));
			if (call.ClosedOn.HasValue)
				facts.Add(Fact(report, NerisFactKeys.IncidentClear, RmsSourceKind.Dispatch, "Calls", "Call", callId.ToString(), Iso(call.ClosedOn), call.ClosedOn, now));

			var location = BuildLocationFromCall(report, call, now);
			if (location != null)
			{
				facts.Add(Fact(report, NerisFactKeys.Location, RmsSourceKind.Dispatch, "Calls", "Call", callId.ToString(), location.AddressText, call.LoggedOn, now));
				if (location.Latitude.HasValue)
					facts.Add(Fact(report, NerisFactKeys.Point, RmsSourceKind.Dispatch, "Calls", "Call", callId.ToString(), $"{location.Latitude},{location.Longitude}", call.LoggedOn, now));
			}

			var units = await BuildUnitsFromCallAsync(report, call, facts, now);
			report.CallArrivalOn = units.Where(u => u.OnSceneOn.HasValue).Select(u => u.OnSceneOn).OrderBy(t => t).FirstOrDefault();
			if (report.CallArrivalOn.HasValue)
				facts.Add(Fact(report, NerisFactKeys.CallArrival, RmsSourceKind.App, "UnitStates", "Call", callId.ToString(), Iso(report.CallArrivalOn), report.CallArrivalOn, now));

			var types = new List<RmsIncidentType>();
			var mappedType = await _neris.ResolveCrosswalkAsync(departmentId, "incident_type", NerisCrosswalkSources.CallType, call.Type);
			if (!string.IsNullOrWhiteSpace(mappedType))
			{
				types.Add(new RmsIncidentType
				{
					RmsIncidentTypeId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = reportId,
					TypeCode = mappedType, IsPrimary = true, LocalCode = call.Type, ValueSetVersion = _neris.ContractVersion, Ordinal = 0, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				});
				facts.Add(Fact(report, NerisFactKeys.IncidentType, RmsSourceKind.Derived, "Crosswalk", "CallType", call.Type, mappedType, call.LoggedOn, now));
			}

			var ordinal = 0;
			foreach (var note in (call.CallNotes ?? new List<CallNote>()).Where(n => !string.IsNullOrWhiteSpace(n.Note)).OrderBy(n => n.Timestamp))
				facts.Add(Fact(report, DispatchCommentFactPrefix + ordinal++, RmsSourceKind.Dispatch, "Calls", "CallNote", note.CallNoteId.ToString(), note.Note, note.Timestamp, now));

			var narrative = new RmsNarrative
			{
				RmsNarrativeId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = reportId,
				CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};

			report.DisplaySummary = BuildSummary(report, types, call.Name);

			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await _reports.InsertAsync(report, cancellationToken, true);
				if (location != null)
					await _locations.InsertAsync(location, cancellationToken, true);
				foreach (var unit in units)
					await _units.InsertAsync(unit, cancellationToken, true);
				foreach (var type in types)
					await _types.InsertAsync(type, cancellationToken, true);
				await _narratives.InsertAsync(narrative, cancellationToken, true);
				foreach (var fact in facts)
					await _facts.InsertAsync(fact, cancellationToken, true);

				var aggregate = new IncidentReportAggregate { Report = report, Location = location, Units = units, Types = types, Narrative = narrative, Facts = facts };
				await RecomputeGroupScopeAsync(aggregate, authorGroup?.DepartmentGroupId, cancellationToken);
				await UpsertProjectionAsync(aggregate, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(report, null, WorkflowTriggerEventType.RecordCreated, RmsRecordState.Draft, RmsRecordState.Draft, null, null, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, reportId, null, RmsAccessAuditAction.Change, "Start incident report from call", origin, cancellationToken, new { callId, prefilledFacts = facts.Count });
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);

			return await GetAsync(departmentId, reportId, false);
		}

		public async Task<IncidentReportAggregate> GetAsync(int departmentId, string reportId, bool includeHistory = false)
		{
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			if (report == null || report.DeletedOn.HasValue)
				return null;

			return await HydrateAsync(report, null, includeHistory);
		}

		public async Task<IncidentReportAggregate> GetForCallAsync(int departmentId, int callId)
		{
			var profile = await _neris.GetProfileAsync(departmentId);
			var report = await _reports.GetByCallAsync(departmentId, callId, ReportingEntityFor(departmentId, profile))
				?? (await _reports.GetByCallAnyEntityAsync(departmentId, callId))?.FirstOrDefault(r => !r.DeletedOn.HasValue);
			return report == null ? null : await HydrateAsync(report, null, false);
		}

		public async Task<NerisIncidentSnapshot> BuildSnapshotAsync(int departmentId, string reportId, string revisionId = null)
		{
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			if (report == null)
				return null;

			var aggregate = await HydrateAsync(report, revisionId, false);
			return ToSnapshot(aggregate);
		}

		public static NerisIncidentSnapshot ToSnapshot(IncidentReportAggregate aggregate)
		{
			return new NerisIncidentSnapshot
			{
				Report = aggregate.Report,
				Location = aggregate.Location,
				Types = aggregate.Types,
				Units = aggregate.Units,
				Aids = aggregate.Aids,
				Tactics = aggregate.Tactics,
				Narrative = aggregate.Narrative,
				Facts = aggregate.Facts,
				DispatchComments = aggregate.Facts.Where(f => f.FactKey != null && f.FactKey.StartsWith(DispatchCommentFactPrefix, StringComparison.Ordinal))
					.OrderBy(f => f.SourceTime).Select(f => new NerisDispatchComment { Timestamp = f.SourceTime, Comment = f.CurrentValue ?? f.SourceValue }).ToList(),
				SpecialModifiers = SplitCsv(aggregate.Report.SpecialModifiersCsv)
			};
		}

		#endregion

		#region Draft / validate

		public async Task<IncidentReportAggregate> SaveDraftAsync(int departmentId, string userId, string reportId, long expectedRowVersion, IncidentReportDraftInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var report = await LoadAsync(departmentId, reportId);
			RequireEditable(report);

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, expectedRowVersion, cancellationToken);
				var facts = (await _facts.GetForRecordAsync(departmentId, reportId, null))?.ToList() ?? new List<RmsSourceFact>();

				ApplyHeader(report, input, facts, userId, now);
				var location = await ReplaceLocationAsync(report, input.Location, facts, userId, now, cancellationToken);
				var types = await ReplaceTypesAsync(report, input.Types, now, cancellationToken);
				var units = await ReplaceUnitsAsync(report, input.Units, facts, userId, now, cancellationToken);
				var aids = await ReplaceAidsAsync(report, input.Aids, now, cancellationToken);
				var tactics = await ReplaceTacticsAsync(report, input.Tactics, now, cancellationToken);
				var narrative = await ReplaceNarrativeAsync(report, input, now, cancellationToken);
				foreach (var fact in facts.Where(f => f.CorrectedOn == now))
					await _facts.UpdateAsync(fact, cancellationToken, true);

				report.DisplaySummary = BuildSummary(report, types, null);
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);

				var aggregate = new IncidentReportAggregate { Report = report, Location = location, Types = types, Units = units, Aids = aids, Tactics = tactics, Narrative = narrative, Facts = facts };
				await RecomputeGroupScopeAsync(aggregate, null, cancellationToken);
				await UpsertProjectionAsync(aggregate, cancellationToken);
				await AuditAsync(departmentId, userId, reportId, null, RmsAccessAuditAction.Change, "Save draft", input.OriginClient, cancellationToken);
			});

			return await GetAsync(departmentId, reportId, false);
		}

		public async Task<List<RmsValidationIssue>> ValidateAsync(int departmentId, string reportId, bool includeDestination, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			var aggregate = await HydrateAsync(report, null, false);
			var profile = await _neris.GetProfileAsync(departmentId);
			var snapshot = ToSnapshot(aggregate);

			var local = _validation.ValidateLocal(snapshot, profile);
			await _issues.ReplaceForRecordAsync(departmentId, reportId, RmsValidationSource.Local, local, cancellationToken);

			if (includeDestination && await _neris.IsSubmissionEnabledAsync(departmentId) && !local.Any(i => i.Severity == (int)RmsValidationSeverity.Error))
			{
				var remote = await _validation.ValidateRemoteAsync(profile, _mapping.BuildIncidentPayloadJson(snapshot, profile), cancellationToken);
				foreach (var issue in remote)
				{
					issue.DepartmentId = departmentId;
					issue.RecordId = reportId;
				}
				await _issues.ReplaceForRecordAsync(departmentId, reportId, RmsValidationSource.Destination, remote, cancellationToken);
			}
			else
			{
				await _issues.ReplaceForRecordAsync(departmentId, reportId, RmsValidationSource.Destination, Enumerable.Empty<RmsValidationIssue>(), cancellationToken);
			}

			await AuditAsync(departmentId, null, reportId, null, RmsAccessAuditAction.Change, includeDestination ? "Validate (local + destination)" : "Validate (local)", RmsOriginClient.Web, cancellationToken, new { errors = local.Count(i => i.Severity == (int)RmsValidationSeverity.Error) });
			return (await _issues.GetForRecordAsync(departmentId, reportId))?.ToList() ?? new List<RmsValidationIssue>();
		}

		#endregion

		#region Review

		public async Task<IncidentReportAggregate> SubmitForReviewAsync(int departmentId, string userId, string reportId, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			var from = (RmsRecordState)report.State;
			RequireTransition(report, from, RmsRecordState.ReadyForReview);

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, expectedRowVersion, cancellationToken);
				report.State = (int)RmsRecordState.ReadyForReview;
				report.SubmittedForReviewOn = now;
				report.ReviewDueOn = now.AddHours(await _settings.GetRecordsReviewDueHoursAsync(departmentId));
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				await RefreshProjectionAsync(report, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(report, null, WorkflowTriggerEventType.RecordSubmittedForReview, from, RmsRecordState.ReadyForReview, null, null, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, reportId, null, RmsAccessAuditAction.Change, "Submit for review", RmsOriginClient.Web, cancellationToken);
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, reportId, false);
		}

		public async Task<IncidentReportAggregate> ReturnForCorrectionAsync(int departmentId, string userId, string reportId, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("A reason code is required to return a report.", nameof(reasonCode));
			var report = await LoadAsync(departmentId, reportId);
			var from = (RmsRecordState)report.State;
			RequireTransition(report, from, RmsRecordState.Returned);

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, report.RowVersion, cancellationToken);
				report.State = (int)RmsRecordState.Returned;
				report.ReturnedOn = now;
				report.ReturnReasonCode = reasonCode;
				report.ReturnReasonText = reasonText;
				report.ReturnCount += 1;
				report.ReviewerUserId = userId;
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				await RefreshProjectionAsync(report, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(report, null, WorkflowTriggerEventType.RecordReturnedForCorrection, from, RmsRecordState.Returned, reasonCode, null, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, reportId, null, RmsAccessAuditAction.Change, "Return for correction", RmsOriginClient.Web, cancellationToken, new { reasonCode });
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, reportId, false);
		}

		#endregion

		#region Finalize / correct / amend

		public Task<IncidentReportAggregate> FinalizeAsync(int departmentId, string userId, string reportId, long expectedRowVersion, string attestationStatementVersion, string ipAddress, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			return SignRevisionAsync(departmentId, userId, reportId, expectedRowVersion, attestationStatementVersion, ipAddress, reasonCode, reasonText, false, cancellationToken);
		}

		public Task<IncidentReportAggregate> CorrectAndResubmitAsync(int departmentId, string userId, string reportId, long expectedRowVersion, string attestationStatementVersion, string ipAddress, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			return SignRevisionAsync(departmentId, userId, reportId, expectedRowVersion, attestationStatementVersion, ipAddress, reasonCode, reasonText, true, cancellationToken);
		}

		private async Task<IncidentReportAggregate> SignRevisionAsync(int departmentId, string userId, string reportId, long expectedRowVersion, string attestationStatementVersion, string ipAddress, string reasonCode, string reasonText, bool correction, CancellationToken cancellationToken)
		{
			var report = await LoadAsync(departmentId, reportId);
			var from = (RmsRecordState)report.State;
			var isAmendment = report.AmendsRevisionId != null;
			RmsRecordState to;
			if (correction)
			{
				if (from != RmsRecordState.Rejected)
					throw new RecordTransitionException(reportId, from, RmsRecordState.Corrected, "only a rejected report can be corrected and resubmitted");
				to = RmsRecordState.Corrected;
			}
			else
			{
				to = isAmendment ? RmsRecordState.Amended : RmsRecordState.Finalized;
				RequireTransition(report, from, to);
			}

			var needsReason = isAmendment || correction;
			if (needsReason && string.IsNullOrWhiteSpace(reasonCode))
				throw new ArgumentException("A reason code is required to finalize an amendment or a correction.", nameof(reasonCode));

			var now = DateTime.UtcNow;
			var profile = await _neris.GetProfileAsync(departmentId);
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, expectedRowVersion, cancellationToken);
				var draft = await HydrateAsync(report, null, false);

				// Validation blocks the signature (plan 4.2 "progressive validation"): the issues stay on the report for the author.
				var local = _validation.ValidateLocal(ToSnapshot(draft), profile);
				await _issues.ReplaceForRecordAsync(departmentId, reportId, RmsValidationSource.Local, local, cancellationToken);
				var errors = local.Where(i => i.Severity == (int)RmsValidationSeverity.Error).ToList();
				if (errors.Count > 0)
					throw new IncidentReportValidationException(reportId, errors);

				if (string.IsNullOrWhiteSpace(report.RecordNumber))
					report.RecordNumber = await AllocateRecordNumberAsync(report, cancellationToken);

				var transition = isAmendment || correction ? RmsRevisionTransition.Amended : RmsRevisionTransition.Finalized;
				var revision = await WriteRevisionAsync(report, draft, transition, userId, reasonCode, reasonText, attestationStatementVersion ?? AttestationStatementVersion, now, cancellationToken);
				await WriteSignatureAsync(report, revision, userId, attestationStatementVersion ?? AttestationStatementVersion, ipAddress, now, cancellationToken);

				var priorState = from;
				report.State = (int)to;
				if (!isAmendment && !correction)
				{
					report.FinalizedOn = now;
					report.FinalizedByUserId = userId;
				}
				report.CurrentRevisionId = revision.RmsRevisionId;
				report.RevisionCount = revision.RevisionNumber;
				report.AmendsRevisionId = null;
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);

				draft.Report = report;
				await RecomputeGroupScopeAsync(draft, null, cancellationToken);
				await UpsertProjectionAsync(draft, cancellationToken);

				var trigger = correction || isAmendment ? WorkflowTriggerEventType.RecordAmended : WorkflowTriggerEventType.RecordFinalized;
				var lifecycle = await EnqueueLifecycleEventAsync(report, revision, trigger, priorState, to, reasonCode, null, cancellationToken);
				outboxIds.Add(lifecycle.DomainEventOutboxId);
				await AuditAsync(departmentId, userId, reportId, revision.RmsRevisionId, RmsAccessAuditAction.Sign, correction ? "Correct and resubmit" : isAmendment ? "Finalize amendment" : "Finalize", (RmsOriginClient)report.OriginClient, cancellationToken, new { revision.RevisionNumber, revision.Checksum, reasonCode }, ipAddress);

				// Submission: every finalized/corrected revision gets its own idempotency key (plan 5.3). Queued here when the
				// profile allows; otherwise the author queues it explicitly.
				var autoSubmit = correction || (profile != null && profile.AutoSubmitOnFinalize);
				if (autoSubmit && await _neris.IsSubmissionEnabledAsync(departmentId))
				{
					var queued = await QueueSubmissionCoreAsync(report, draft, revision, profile, userId, now, cancellationToken);
					outboxIds.Add(queued.outboxId);
				}
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, reportId, true);
		}

		public async Task<IncidentReportAggregate> QueueSubmissionAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			var state = (RmsRecordState)report.State;
			if (!RmsLifecycle.IsFinalizedFamily(state) || RmsLifecycle.IsTerminal(state) || string.IsNullOrWhiteSpace(report.CurrentRevisionId))
				throw new RecordTransitionException(reportId, state, RmsRecordState.Submitted, "only a finalized revision can be submitted");
			if (report.AmendsRevisionId != null)
				throw new RecordTransitionException(reportId, state, RmsRecordState.Submitted, "close the open amendment before submitting");
			if (!await _neris.IsSubmissionEnabledAsync(departmentId))
				throw new InvalidOperationException("NERIS submission is not enabled for this department.");

			var profile = await _neris.GetProfileAsync(departmentId);
			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, report.RowVersion, cancellationToken);
				var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, report.CurrentRevisionId);
				var aggregate = await HydrateAsync(report, revision.RmsRevisionId, false);
				var queued = await QueueSubmissionCoreAsync(report, aggregate, revision, profile, userId, now, cancellationToken);
				outboxIds.Add(queued.outboxId);
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				await RefreshProjectionAsync(report, cancellationToken);
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, reportId, true);
		}

		private async Task<(RmsSubmission submission, long outboxId)> QueueSubmissionCoreAsync(RmsIncidentReport report, IncidentReportAggregate aggregate, RmsRevision revision, RmsNerisProfile profile, string userId, DateTime now, CancellationToken cancellationToken)
		{
			var payload = _mapping.BuildIncidentPayloadJson(ToSnapshot(aggregate), profile);
			var key = IdempotencyKey(report.DepartmentId, report.RmsIncidentReportId, revision.RmsRevisionId);

			var submission = await _submissions.GetByIdempotencyKeyAsync(key);
			if (submission != null && submission.State != (int)RmsSubmissionState.Failed && submission.State != (int)RmsSubmissionState.Superseded && submission.State != (int)RmsSubmissionState.Rejected)
				throw new InvalidOperationException("This revision is already queued or delivered.");

			if (submission == null)
			{
				submission = new RmsSubmission
				{
					RmsSubmissionId = Guid.NewGuid().ToString(),
					DepartmentId = report.DepartmentId,
					ProtectionId = Guid.NewGuid().ToString(),
					RecordId = report.RmsIncidentReportId,
					RecordKind = (int)RmsRecordKind.IncidentReport,
					RevisionId = revision.RmsRevisionId,
					Destination = RmsSubmissionDestinations.Neris,
					DestinationVersion = profile?.ContractVersion ?? _neris.ContractVersion,
					IdempotencyKey = key,
					MaxAttempts = Math.Max(1, Config.NerisConfig.MaxAttempts),
					PayloadJson = payload,
					PayloadChecksum = RecordSnapshotSerializer.Checksum(payload),
					QueuedOn = now,
					CreatedByUserId = userId,
					CreatedOn = now,
					ModifiedOn = now,
					RowVersion = 1
				};
				await _submissions.SupersedeOpenForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, submission.RmsSubmissionId, now, cancellationToken);
				await _submissions.InsertAsync(submission, cancellationToken, true);
			}
			else
			{
				// A failed or rejected delivery of the same revision is re-queued with the same key: retry, not a new payload.
				submission.State = (int)RmsSubmissionState.Queued;
				submission.Attempts = 0;
				submission.NextAttemptOn = null;
				submission.LeaseOwner = null;
				submission.LeaseExpiresOn = null;
				submission.ErrorSummary = null;
				submission.QueuedOn = now;
				submission.ModifiedOn = now;
				submission.RowVersion += 1;
				await _submissions.UpdateAsync(submission, cancellationToken, true);
			}

			var priorState = (RmsRecordState)report.State;
			report.State = (int)RmsRecordState.Submitted;
			report.LastSubmissionId = submission.RmsSubmissionId;
			report.LastSubmissionState = submission.State;
			report.LastSubmittedOn = now;
			await _reports.UpdateAsync(report, cancellationToken, true);
			await RefreshProjectionAsync(report, cancellationToken);

			var entry = await EnqueueLifecycleEventAsync(report, revision, WorkflowTriggerEventType.RecordSubmissionQueued, priorState, RmsRecordState.Submitted, null, SubmissionBlock(submission), cancellationToken);
			await AuditAsync(report.DepartmentId, userId, report.RmsIncidentReportId, revision.RmsRevisionId, RmsAccessAuditAction.Submit, "Queue submission", RmsOriginClient.Web, cancellationToken, new { submission.RmsSubmissionId, submission.IdempotencyKey, submission.PayloadChecksum });
			return (submission, entry.DomainEventOutboxId);
		}

		public async Task<IncidentReportAggregate> OpenAmendmentAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			var state = (RmsRecordState)report.State;
			if (!RmsLifecycle.CanTransition((RmsLifecyclePreset)report.LifecyclePreset, state, RmsRecordState.Amended))
				throw new RecordTransitionException(reportId, state, RmsRecordState.Amended);
			if (report.AmendsRevisionId != null)
				throw new RecordTransitionException(reportId, state, RmsRecordState.Amended, "an amendment draft is already open");

			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, report.RowVersion, cancellationToken);
				report.AmendsRevisionId = report.CurrentRevisionId;
				report.OwnerUserId = userId;
				report.ModifiedOn = DateTime.UtcNow;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				await AuditAsync(departmentId, userId, reportId, report.CurrentRevisionId, RmsAccessAuditAction.Change, "Open amendment", RmsOriginClient.Web, cancellationToken);
			});
			return await GetAsync(departmentId, reportId, true);
		}

		public async Task<IncidentReportAggregate> AbandonAmendmentAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			if (report.AmendsRevisionId == null)
				throw new RecordTransitionException(reportId, (RmsRecordState)report.State, (RmsRecordState)report.State, "no amendment draft is open");

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, report.RowVersion, cancellationToken);
				// The draft rows are rebuilt from the current revision's copies so the finalized content is what the author sees again.
				var current = await HydrateAsync(report, report.CurrentRevisionId, false);
				await ReplaceDraftRowsFromAsync(report, current, now, cancellationToken);
				report.AmendsRevisionId = null;
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				await AuditAsync(departmentId, userId, reportId, report.CurrentRevisionId, RmsAccessAuditAction.Change, "Abandon amendment", RmsOriginClient.Web, cancellationToken);
			});
			return await GetAsync(departmentId, reportId, true);
		}

		public async Task<IncidentReportAggregate> VoidAsync(int departmentId, string userId, string reportId, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("A reason code is required to void a report.", nameof(reasonCode));
			var report = await LoadAsync(departmentId, reportId);
			var from = (RmsRecordState)report.State;
			RequireTransition(report, from, RmsRecordState.Voided);
			if (report.AmendsRevisionId != null)
				throw new RecordTransitionException(reportId, from, RmsRecordState.Voided, "abandon the open amendment first");

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, report.RowVersion, cancellationToken);
				var current = await HydrateAsync(report, report.CurrentRevisionId, false);
				var revision = await WriteRevisionAsync(report, current, RmsRevisionTransition.Voided, userId, reasonCode, reasonText, null, now, cancellationToken);
				report.State = (int)RmsRecordState.Voided;
				report.VoidedOn = now;
				report.VoidedByUserId = userId;
				report.VoidReasonCode = reasonCode;
				report.VoidReasonText = reasonText;
				report.CurrentRevisionId = revision.RmsRevisionId;
				report.RevisionCount = revision.RevisionNumber;
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				await _submissions.SupersedeOpenForRecordAsync(departmentId, reportId, null, now, cancellationToken);
				await RefreshProjectionAsync(report, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(report, revision, WorkflowTriggerEventType.RecordVoided, from, RmsRecordState.Voided, reasonCode, null, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, reportId, revision.RmsRevisionId, RmsAccessAuditAction.Change, "Void", RmsOriginClient.Web, cancellationToken, new { reasonCode });
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, reportId, true);
		}

		public async Task<IncidentReportAggregate> CancelAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			var from = (RmsRecordState)report.State;
			RequireTransition(report, from, RmsRecordState.Cancelled);

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, report.RowVersion, cancellationToken);
				report.State = (int)RmsRecordState.Cancelled;
				report.CancelledOn = now;
				report.CancelledByUserId = userId;
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				await RefreshProjectionAsync(report, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(report, null, WorkflowTriggerEventType.RecordCancelled, from, RmsRecordState.Cancelled, null, null, cancellationToken, new { number_disposition = string.IsNullOrWhiteSpace(report.RecordNumber) ? "none" : "voided" })).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, reportId, null, RmsAccessAuditAction.Change, "Cancel", RmsOriginClient.Web, cancellationToken);
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, reportId, false);
		}

		#endregion

		#region Queries / audit

		public async Task<List<RmsIncidentReport>> QueryAsync(int departmentId, RmsIncidentReportQuery query)
		{
			return (await _reports.QueryAsync(departmentId, query ?? new RmsIncidentReportQuery()))?.ToList() ?? new List<RmsIncidentReport>();
		}

		public Task<int> CountAsync(int departmentId, RmsIncidentReportQuery query)
		{
			return _reports.CountAsync(departmentId, query ?? new RmsIncidentReportQuery());
		}

		public async Task<List<int>> GetYearsAsync(int departmentId)
		{
			return (await _reports.GetYearsAsync(departmentId))?.ToList() ?? new List<int>();
		}

		public Task RecordAccessAsync(int departmentId, string userId, string reportId, string revisionId, RmsAccessAuditAction action, string purpose = null, string ipAddress = null)
		{
			return AuditAsync(departmentId, userId, reportId, revisionId, action, purpose, RmsOriginClient.Web, CancellationToken.None, null, ipAddress);
		}

		#endregion

		#region Prefill helpers

		public static string ReportingEntityFor(int departmentId, RmsNerisProfile profile)
		{
			return string.IsNullOrWhiteSpace(profile?.NerisEntityId) ? $"department:{departmentId}" : profile.NerisEntityId;
		}

		private static RmsLocation BuildLocationFromCall(RmsIncidentReport report, Call call, DateTime now)
		{
			decimal? latitude = null, longitude = null;
			if (!string.IsNullOrWhiteSpace(call.GeoLocationData) && call.GeoLocationData.Contains(","))
			{
				var parts = call.GeoLocationData.Split(',');
				if (parts.Length == 2 && decimal.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) && decimal.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
				{
					latitude = lat;
					longitude = lon;
				}
			}

			if (string.IsNullOrWhiteSpace(call.Address) && latitude == null)
				return null;

			return new RmsLocation
			{
				RmsLocationId = Guid.NewGuid().ToString(),
				DepartmentId = report.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = report.RmsIncidentReportId,
				AddressText = string.IsNullOrWhiteSpace(call.Address) ? null : call.Address.Trim(),
				Latitude = latitude,
				Longitude = longitude,
				SourceKind = (int)RmsSourceKind.Dispatch,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};
		}

		/// <summary>One unit response per dispatched unit; times come from the unit state log (App) and the dispatch row (Dispatch), each with a provenance fact.</summary>
		private async Task<List<RmsUnitResponse>> BuildUnitsFromCallAsync(RmsIncidentReport report, Call call, List<RmsSourceFact> facts, DateTime now)
		{
			var result = new List<RmsUnitResponse>();
			var dispatches = (call.UnitDispatches ?? new List<CallDispatchUnit>()).GroupBy(d => d.UnitId).Select(g => g.OrderBy(d => d.DispatchedOn).First()).ToList();
			if (dispatches.Count == 0)
				return result;

			var states = (await _unitsService.GetUnitStatesForCallAsync(report.DepartmentId, call.CallId) ?? new List<UnitState>()).OrderBy(s => s.Timestamp).ToList();
			var ordinal = 0;
			foreach (var dispatch in dispatches)
			{
				var unit = await _unitsService.GetUnitByIdAsync(dispatch.UnitId);
				if (unit == null || unit.DepartmentId != report.DepartmentId)
					continue;

				var unitStates = states.Where(s => s.UnitId == dispatch.UnitId).ToList();
				DateTime? At(params UnitStateTypes[] kinds) => unitStates.FirstOrDefault(s => kinds.Contains((UnitStateTypes)s.State))?.Timestamp;
				var onScene = At(UnitStateTypes.OnScene);
				var cleared = unitStates.Where(s => onScene.HasValue && s.Timestamp >= onScene.Value && ((UnitStateTypes)s.State == UnitStateTypes.Released || (UnitStateTypes)s.State == UnitStateTypes.Returning || (UnitStateTypes)s.State == UnitStateTypes.Available)).Select(s => (DateTime?)s.Timestamp).FirstOrDefault();

				var response = new RmsUnitResponse
				{
					RmsUnitResponseId = Guid.NewGuid().ToString(),
					DepartmentId = report.DepartmentId,
					ProtectionId = Guid.NewGuid().ToString(),
					RecordId = report.RmsIncidentReportId,
					UnitId = unit.UnitId,
					UnitNameSnapshot = unit.Name,
					UnitTypeSnapshot = unit.Type,
					StationGroupIdSnapshot = unit.StationGroupId,
					DispatchedOn = dispatch.DispatchedOn,
					EnrouteOn = At(UnitStateTypes.Responding, UnitStateTypes.Enroute),
					OnSceneOn = onScene,
					StagingOn = At(UnitStateTypes.Staging),
					CanceledEnrouteOn = onScene.HasValue ? null : At(UnitStateTypes.Cancelled),
					ClearedOn = cleared,
					ResponseMode = "EMERGENT",
					TimesSourceKind = (int)RmsSourceKind.App,
					Ordinal = ordinal++,
					CreatedOn = now,
					ModifiedOn = now,
					RowVersion = 1
				};
				result.Add(response);

				facts.Add(Fact(report, NerisFactKeys.UnitTime(unit.UnitId, "dispatch"), RmsSourceKind.Dispatch, "Calls", "CallDispatchUnit", dispatch.CallDispatchUnitId.ToString(), Iso(dispatch.DispatchedOn), dispatch.DispatchedOn, now));
				foreach (var (field, value) in new[] { ("enroute_to_scene", response.EnrouteOn), ("on_scene", response.OnSceneOn), ("staging", response.StagingOn), ("canceled_enroute", response.CanceledEnrouteOn), ("unit_clear", response.ClearedOn) })
				{
					if (value.HasValue)
						facts.Add(Fact(report, NerisFactKeys.UnitTime(unit.UnitId, field), RmsSourceKind.App, "UnitStates", "Unit", unit.UnitId.ToString(), Iso(value), value, now));
				}
			}

			return result;
		}

		private static RmsSourceFact Fact(RmsIncidentReport report, string key, RmsSourceKind kind, string system, string entityType, string entityId, string value, DateTime? sourceTime, DateTime now)
		{
			return new RmsSourceFact
			{
				RmsSourceFactId = Guid.NewGuid().ToString(),
				DepartmentId = report.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = report.RmsIncidentReportId,
				FactKey = key,
				SourceKind = (int)kind,
				SourceSystem = system,
				SourceEntityType = entityType,
				SourceEntityId = entityId,
				SourceValue = value,
				CurrentValue = value,
				SourceTime = sourceTime,
				ImportedOn = now,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};
		}

		/// <summary>An edit to a prefilled value keeps the original and records the correction on its provenance row (plan 4.2).</summary>
		private static void Correct(List<RmsSourceFact> facts, string key, string newValue, string userId, DateTime now)
		{
			var fact = facts.FirstOrDefault(f => string.Equals(f.FactKey, key, StringComparison.Ordinal));
			if (fact == null || string.Equals(fact.CurrentValue, newValue, StringComparison.Ordinal))
				return;

			fact.CurrentValue = newValue;
			fact.CorrectedOn = now;
			fact.CorrectedByUserId = userId;
			fact.ModifiedOn = now;
			fact.RowVersion += 1;
		}

		public static string Iso(DateTime? value)
		{
			return value.HasValue ? value.Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null;
		}

		#endregion

		#region Draft replacement

		private static void ApplyHeader(RmsIncidentReport report, IncidentReportDraftInput input, List<RmsSourceFact> facts, string userId, DateTime now)
		{
			Correct(facts, NerisFactKeys.IncidentNumber, input.IncidentNumber, userId, now);
			Correct(facts, NerisFactKeys.IncidentCode, input.DispatchIncidentCode, userId, now);
			Correct(facts, NerisFactKeys.CallCreate, Iso(input.CallCreatedOn), userId, now);
			Correct(facts, NerisFactKeys.CallAnswered, Iso(input.CallAnsweredOn), userId, now);
			Correct(facts, NerisFactKeys.CallArrival, Iso(input.CallArrivalOn), userId, now);
			Correct(facts, NerisFactKeys.IncidentClear, Iso(input.IncidentClearedOn), userId, now);

			report.IncidentNumber = Trim(input.IncidentNumber);
			report.DispatchIncidentCode = Trim(input.DispatchIncidentCode);
			report.CallCreatedOn = input.CallCreatedOn;
			report.CallAnsweredOn = input.CallAnsweredOn;
			report.CallArrivalOn = input.CallArrivalOn;
			report.IncidentClearedOn = input.IncidentClearedOn;
			report.DispatchCenterId = Trim(input.DispatchCenterId);
			report.DeterminantCode = Trim(input.DeterminantCode);
			report.Disposition = Trim(input.Disposition);
			report.PeoplePresent = input.PeoplePresent;
			report.DisplacementCount = input.DisplacementCount;
			report.AnimalsRescued = input.AnimalsRescued;
			report.SpecialModifiersCsv = input.SpecialModifiers == null || input.SpecialModifiers.Count == 0 ? null : string.Join(",", input.SpecialModifiers.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct());
			if (input.StationGroupId.HasValue)
				report.StationGroupId = input.StationGroupId;
		}

		private async Task<RmsLocation> ReplaceLocationAsync(RmsIncidentReport report, IncidentLocationInput input, List<RmsSourceFact> facts, string userId, DateTime now, CancellationToken cancellationToken)
		{
			await _locations.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			if (input == null)
				return null;

			var location = new RmsLocation
			{
				RmsLocationId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = report.RmsIncidentReportId,
				AddressText = Trim(input.AddressText), Number = Trim(input.Number), NumberPrefix = Trim(input.NumberPrefix), NumberSuffix = Trim(input.NumberSuffix), Street = Trim(input.Street),
				UnitValue = Trim(input.UnitValue), Municipality = Trim(input.Municipality), County = Trim(input.County), State = Trim(input.State)?.ToUpperInvariant(), PostalCode = Trim(input.PostalCode),
				Country = Trim(input.Country)?.ToUpperInvariant(), PlaceType = Trim(input.PlaceType), LocationUse = Trim(input.LocationUse), CrossStreet1 = Trim(input.CrossStreet1), CrossStreet2 = Trim(input.CrossStreet2),
				Latitude = input.Latitude, Longitude = input.Longitude, Jurisdiction = Trim(input.Jurisdiction),
				SourceKind = (int)RmsSourceKind.None, CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			Correct(facts, NerisFactKeys.Location, location.AddressText, userId, now);
			Correct(facts, NerisFactKeys.Point, location.Latitude.HasValue ? $"{location.Latitude},{location.Longitude}" : null, userId, now);
			await _locations.InsertAsync(location, cancellationToken, true);
			return location;
		}

		private async Task<List<RmsIncidentType>> ReplaceTypesAsync(RmsIncidentReport report, List<IncidentTypeInput> inputs, DateTime now, CancellationToken cancellationToken)
		{
			await _types.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			var result = new List<RmsIncidentType>();
			var ordinal = 0;
			foreach (var input in (inputs ?? new List<IncidentTypeInput>()).Where(i => !string.IsNullOrWhiteSpace(i.TypeCode)))
			{
				var row = new RmsIncidentType
				{
					RmsIncidentTypeId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = report.RmsIncidentReportId,
					TypeCode = input.TypeCode.Trim(), IsPrimary = input.IsPrimary, LocalCode = ordinal == 0 ? report.DispatchIncidentCode : null, ValueSetVersion = _neris.ContractVersion,
					Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};
				await _types.InsertAsync(row, cancellationToken, true);
				result.Add(row);
			}
			if (result.Count > 0 && !result.Any(t => t.IsPrimary))
				result[0].IsPrimary = true;
			return result;
		}

		private async Task<List<RmsUnitResponse>> ReplaceUnitsAsync(RmsIncidentReport report, List<IncidentUnitResponseInput> inputs, List<RmsSourceFact> facts, string userId, DateTime now, CancellationToken cancellationToken)
		{
			await _units.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			var result = new List<RmsUnitResponse>();
			var ordinal = 0;
			foreach (var input in inputs ?? new List<IncidentUnitResponseInput>())
			{
				Unit unit = null;
				if (input.UnitId.HasValue && input.UnitId > 0)
				{
					unit = await _unitsService.GetUnitByIdAsync(input.UnitId.Value);
					if (unit == null || unit.DepartmentId != report.DepartmentId)
						throw new ArgumentException($"Unit {input.UnitId} does not belong to this department.");
				}
				if (unit == null && string.IsNullOrWhiteSpace(input.ReportedUnitId) && string.IsNullOrWhiteSpace(input.UnitNerisId))
					continue;

				var row = new RmsUnitResponse
				{
					RmsUnitResponseId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = report.RmsIncidentReportId,
					UnitId = unit?.UnitId, UnitNameSnapshot = unit?.Name ?? Trim(input.ReportedUnitId), UnitTypeSnapshot = unit?.Type, StationGroupIdSnapshot = unit?.StationGroupId,
					UnitNerisId = Trim(input.UnitNerisId)?.ToUpperInvariant(), Staffing = input.Staffing, UnableToDispatch = input.UnableToDispatch,
					DispatchedOn = input.DispatchedOn, EnrouteOn = input.EnrouteOn, OnSceneOn = input.OnSceneOn, CanceledEnrouteOn = input.CanceledEnrouteOn, StagingOn = input.StagingOn, ClearedOn = input.ClearedOn,
					ResponseMode = Trim(input.ResponseMode)?.ToUpperInvariant(), TransportMode = Trim(input.TransportMode)?.ToUpperInvariant(),
					TimesSourceKind = (int)RmsSourceKind.None, Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};
				if (unit != null)
				{
					Correct(facts, NerisFactKeys.UnitTime(unit.UnitId, "dispatch"), Iso(row.DispatchedOn), userId, now);
					Correct(facts, NerisFactKeys.UnitTime(unit.UnitId, "enroute_to_scene"), Iso(row.EnrouteOn), userId, now);
					Correct(facts, NerisFactKeys.UnitTime(unit.UnitId, "on_scene"), Iso(row.OnSceneOn), userId, now);
					Correct(facts, NerisFactKeys.UnitTime(unit.UnitId, "staging"), Iso(row.StagingOn), userId, now);
					Correct(facts, NerisFactKeys.UnitTime(unit.UnitId, "canceled_enroute"), Iso(row.CanceledEnrouteOn), userId, now);
					Correct(facts, NerisFactKeys.UnitTime(unit.UnitId, "unit_clear"), Iso(row.ClearedOn), userId, now);
					// Provenance survives an edit: the fact keeps its App/Dispatch origin, the row shows the source that still applies.
					row.TimesSourceKind = facts.Any(f => f.FactKey.StartsWith($"unit.{unit.UnitId}.", StringComparison.Ordinal) && f.CorrectedOn == null) ? (int)RmsSourceKind.App : (int)RmsSourceKind.None;
				}
				await _units.InsertAsync(row, cancellationToken, true);
				result.Add(row);
			}
			return result;
		}

		private async Task<List<RmsAid>> ReplaceAidsAsync(RmsIncidentReport report, List<IncidentAidInput> inputs, DateTime now, CancellationToken cancellationToken)
		{
			await _aids.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			var result = new List<RmsAid>();
			var ordinal = 0;
			foreach (var input in inputs ?? new List<IncidentAidInput>())
			{
				if (!input.IsNonFireDepartment && string.IsNullOrWhiteSpace(input.AidType) && string.IsNullOrWhiteSpace(input.CounterpartNerisId))
					continue;
				if (input.IsNonFireDepartment && string.IsNullOrWhiteSpace(input.NonFdType))
					continue;

				var row = new RmsAid
				{
					RmsAidId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = report.RmsIncidentReportId,
					Direction = Trim(input.Direction)?.ToUpperInvariant() ?? "RECEIVED", AidType = Trim(input.AidType)?.ToUpperInvariant() ?? string.Empty,
					CounterpartNerisId = Trim(input.CounterpartNerisId)?.ToUpperInvariant(), CounterpartName = Trim(input.CounterpartName),
					IsNonFireDepartment = input.IsNonFireDepartment, NonFdType = Trim(input.NonFdType)?.ToUpperInvariant(),
					Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};
				await _aids.InsertAsync(row, cancellationToken, true);
				result.Add(row);
			}
			return result;
		}

		private async Task<List<RmsActionTactic>> ReplaceTacticsAsync(RmsIncidentReport report, List<IncidentTacticInput> inputs, DateTime now, CancellationToken cancellationToken)
		{
			await _tactics.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			var result = new List<RmsActionTactic>();
			var ordinal = 0;
			foreach (var input in (inputs ?? new List<IncidentTacticInput>()).Where(i => !string.IsNullOrWhiteSpace(i.TacticCode)))
			{
				var row = new RmsActionTactic
				{
					RmsActionTacticId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = report.RmsIncidentReportId,
					TacticCode = input.TacticCode.Trim(), ActorUnitId = input.ActorUnitId, OccurredOn = input.OccurredOn, SourceKind = (int)RmsSourceKind.None,
					Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};
				await _tactics.InsertAsync(row, cancellationToken, true);
				result.Add(row);
			}
			return result;
		}

		private async Task<RmsNarrative> ReplaceNarrativeAsync(RmsIncidentReport report, IncidentReportDraftInput input, DateTime now, CancellationToken cancellationToken)
		{
			await _narratives.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			var row = new RmsNarrative
			{
				RmsNarrativeId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = report.RmsIncidentReportId,
				Narrative = input.Narrative, ImpedimentNarrative = input.ImpedimentNarrative, OutcomeNarrative = input.OutcomeNarrative, SupplementalJson = input.SupplementalJson,
				CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			await _narratives.InsertAsync(row, cancellationToken, true);
			return row;
		}

		private async Task ReplaceDraftRowsFromAsync(RmsIncidentReport report, IncidentReportAggregate source, DateTime now, CancellationToken cancellationToken)
		{
			await _locations.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			await _types.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			await _units.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			await _aids.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			await _tactics.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			await _narratives.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);
			await _facts.DeleteDraftForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, cancellationToken);

			if (source.Location != null) await _locations.InsertAsync(Copy(source.Location, l => l.RmsLocationId = Guid.NewGuid().ToString(), null, now), cancellationToken, true);
			foreach (var t in source.Types) await _types.InsertAsync(Copy(t, x => x.RmsIncidentTypeId = Guid.NewGuid().ToString(), null, now), cancellationToken, true);
			foreach (var u in source.Units) await _units.InsertAsync(Copy(u, x => x.RmsUnitResponseId = Guid.NewGuid().ToString(), null, now), cancellationToken, true);
			foreach (var a in source.Aids) await _aids.InsertAsync(Copy(a, x => x.RmsAidId = Guid.NewGuid().ToString(), null, now), cancellationToken, true);
			foreach (var t in source.Tactics) await _tactics.InsertAsync(Copy(t, x => x.RmsActionTacticId = Guid.NewGuid().ToString(), null, now), cancellationToken, true);
			if (source.Narrative != null) await _narratives.InsertAsync(Copy(source.Narrative, n => n.RmsNarrativeId = Guid.NewGuid().ToString(), null, now), cancellationToken, true);
			foreach (var f in source.Facts) await _facts.InsertAsync(Copy(f, x => x.RmsSourceFactId = Guid.NewGuid().ToString(), null, now), cancellationToken, true);
		}

		#endregion

		#region Revision / signature / number

		private async Task<RmsRevision> WriteRevisionAsync(RmsIncidentReport report, IncidentReportAggregate draft, RmsRevisionTransition transition, string userId, string reasonCode, string reasonText, string attestationVersion, DateTime now, CancellationToken cancellationToken)
		{
			var json = SerializeSnapshot(draft);
			var revision = new RmsRevision
			{
				RmsRevisionId = Guid.NewGuid().ToString(),
				DepartmentId = report.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = report.RmsIncidentReportId,
				RecordKind = (int)RmsRecordKind.IncidentReport,
				RevisionNumber = report.RevisionCount + 1,
				Transition = (int)transition,
				PriorRevisionId = report.CurrentRevisionId,
				DefinitionKey = report.DefinitionKey,
				DefinitionVersion = report.DefinitionVersion,
				SnapshotJson = json,
				Checksum = RecordSnapshotSerializer.Checksum(json),
				ActorUserId = userId,
				ReasonCode = reasonCode,
				ReasonText = reasonText,
				AttestationStatementVersion = transition == RmsRevisionTransition.Voided ? null : attestationVersion,
				AttestedOn = transition == RmsRevisionTransition.Voided ? (DateTime?)null : now,
				OriginClient = report.OriginClient,
				CreatedOn = now
			};
			await _revisions.InsertAsync(revision, cancellationToken, true);

			// Revision-bound copies keep finalized data queryable without touching the draft rows.
			var id = revision.RmsRevisionId;
			if (draft.Location != null) await _locations.InsertAsync(Copy(draft.Location, l => l.RmsLocationId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			foreach (var t in draft.Types) await _types.InsertAsync(Copy(t, x => x.RmsIncidentTypeId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			foreach (var u in draft.Units) await _units.InsertAsync(Copy(u, x => x.RmsUnitResponseId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			foreach (var a in draft.Aids) await _aids.InsertAsync(Copy(a, x => x.RmsAidId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			foreach (var t in draft.Tactics) await _tactics.InsertAsync(Copy(t, x => x.RmsActionTacticId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			if (draft.Narrative != null) await _narratives.InsertAsync(Copy(draft.Narrative, n => n.RmsNarrativeId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);
			foreach (var f in draft.Facts) await _facts.InsertAsync(Copy(f, x => x.RmsSourceFactId = Guid.NewGuid().ToString(), id, now), cancellationToken, true);

			return revision;
		}

		private async Task WriteSignatureAsync(RmsIncidentReport report, RmsRevision revision, string userId, string statementVersion, string ipAddress, DateTime now, CancellationToken cancellationToken)
		{
			var profile = await _profiles.GetProfileByUserIdAsync(userId, false);
			var roles = await _roles.GetRolesForUserAsync(userId, report.DepartmentId);
			await _signatures.InsertAsync(new RmsSignature
			{
				RmsSignatureId = Guid.NewGuid().ToString(),
				DepartmentId = report.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = report.RmsIncidentReportId,
				RecordKind = (int)RmsRecordKind.IncidentReport,
				RevisionId = revision.RmsRevisionId,
				SignerUserId = userId,
				SignerNameSnapshot = profile == null ? null : $"{profile.FirstName} {profile.LastName}".Trim(),
				SignerRoleSnapshot = roles == null || roles.Count == 0 ? null : string.Join(", ", roles.Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n))),
				Intent = (int)RmsSignatureIntent.Attestation,
				StatementVersion = statementVersion,
				StatementText = AttestationStatement(statementVersion),
				Method = (int)RmsSignatureMethod.WebAttestation,
				SignedOn = now,
				IpAddress = ipAddress,
				ArtifactChecksum = revision.Checksum,
				CreatedOn = now,
				RowVersion = 1
			}, cancellationToken, true);
		}

		public static string AttestationStatement(string version)
		{
			return "I attest that this incident report is accurate and complete to the best of my knowledge.";
		}

		private async Task<string> AllocateRecordNumberAsync(RmsIncidentReport report, CancellationToken cancellationToken)
		{
			var config = await _settings.GetRecordsNumberingConfigAsync(report.DepartmentId);
			var year = (report.CallCreatedOn ?? DateTime.UtcNow).Year;
			var prefix = NumberPrefix + "-";
			if (config.PerGroupSequence && report.StationGroupId.HasValue)
				prefix += "G" + report.StationGroupId.Value + "-";
			if (config.IncludeYear)
				prefix += year + "-";
			var width = Math.Max(3, Math.Min(8, config.SequenceWidth <= 0 ? 4 : config.SequenceWidth));
			var sequence = await _reports.GetMaxRecordNumberSequenceAsync(report.DepartmentId, prefix) + 1;
			return prefix + sequence.ToString("D" + width);
		}

		/// <summary>Scoped idempotency key (plan 5.3): stable for a revision, new for every new revision.</summary>
		public static string IdempotencyKey(int departmentId, string reportId, string revisionId)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"neris:{departmentId}:{reportId}:{revisionId}"))).ToLowerInvariant();
		}

		#endregion

		#region Hydration / projection / scope / events

		private async Task<IncidentReportAggregate> HydrateAsync(RmsIncidentReport report, string revisionId, bool includeHistory)
		{
			var dept = report.DepartmentId;
			var id = report.RmsIncidentReportId;
			var aggregate = new IncidentReportAggregate
			{
				Report = report,
				Location = (await _locations.GetForRecordAsync(dept, id, revisionId))?.FirstOrDefault(),
				Types = (await _types.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsIncidentType>(),
				Units = (await _units.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsUnitResponse>(),
				Aids = (await _aids.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsAid>(),
				Tactics = (await _tactics.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsActionTactic>(),
				Narrative = (await _narratives.GetForRecordAsync(dept, id, revisionId))?.FirstOrDefault(),
				Facts = (await _facts.GetForRecordAsync(dept, id, revisionId))?.ToList() ?? new List<RmsSourceFact>(),
				Issues = (await _issues.GetForRecordAsync(dept, id))?.ToList() ?? new List<RmsValidationIssue>(),
				GroupScope = (await _scopes.GetForRecordAsync(dept, id))?.ToList() ?? new List<RmsRecordGroupScope>()
			};
			if (includeHistory)
			{
				aggregate.Submissions = (await _submissions.GetForRecordAsync(dept, id))?.ToList() ?? new List<RmsSubmission>();
				aggregate.Signatures = (await _signatures.GetForRecordAsync(dept, id))?.ToList() ?? new List<RmsSignature>();
				aggregate.Revisions = (await _revisions.GetForRecordAsync(dept, id))?.ToList() ?? new List<RmsRevision>();
			}
			return aggregate;
		}

		private async Task RefreshProjectionAsync(RmsIncidentReport report, CancellationToken cancellationToken)
		{
			await UpsertProjectionAsync(await HydrateAsync(report, null, false), cancellationToken);
		}

		private async Task UpsertProjectionAsync(IncidentReportAggregate aggregate, CancellationToken cancellationToken)
		{
			var report = aggregate.Report;
			var existing = await _projections.GetByRecordIdAsync(report.DepartmentId, report.RmsIncidentReportId);
			var projection = existing ?? new RmsRecordSearchProjection
			{
				RmsRecordSearchProjectionId = report.RmsIncidentReportId,
				DepartmentId = report.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				CreatedOn = DateTime.UtcNow,
				RowVersion = 0
			};

			projection.SourceType = (int)RmsSearchSourceType.Record;
			projection.SourceId = report.RmsIncidentReportId;
			projection.RecordKind = (int)RmsRecordKind.IncidentReport;
			projection.RecordNumber = report.RecordNumber;
			projection.DraftReference = report.DraftReference;
			projection.DefinitionKey = report.DefinitionKey;
			projection.DefinitionVersion = report.DefinitionVersion;
			projection.RecordType = null;
			projection.State = report.State;
			projection.OccurredOn = report.CallCreatedOn;
			projection.RecordCreatedOn = report.CreatedOn;
			projection.FinalizedOn = report.FinalizedOn;
			projection.StationGroupId = report.StationGroupId;
			projection.CallId = report.CallId;
			projection.CallNumber = report.IncidentNumber;
			projection.AuthorUserId = report.AuthorUserId;
			projection.OwnerUserId = report.OwnerUserId;
			projection.ReviewerUserId = report.ReviewerUserId;
			projection.ParticipantUserIds = string.Empty;
			projection.UnitIds = string.Join(",", aggregate.Units.Where(u => u.UnitId.HasValue).Select(u => u.UnitId.Value).Distinct());
			projection.GroupScopeIds = string.Join(",", (aggregate.GroupScope ?? new List<RmsRecordGroupScope>()).Select(s => s.DepartmentGroupId).Distinct());
			projection.DisplaySummary = report.DisplaySummary;
			// Safe fields only (plan 5.10): numbers, summary, incident number, type codes; never narrative or address detail.
			projection.SearchText = string.Join(" ", new[] { report.RecordNumber, report.DraftReference, report.DisplaySummary, report.IncidentNumber, report.NerisIncidentId }.Concat(aggregate.Types.Select(t => t.TypeCode)).Where(s => !string.IsNullOrWhiteSpace(s)));
			projection.IsLegacy = false;
			projection.ProjectionVersion = RmsRecordSearchProjection.CurrentProjectionVersion;
			projection.ProtectedCatalogVersion = await SafeCatalogVersionAsync(report.DepartmentId);
			projection.PolicyEpoch = await SafePolicyEpochAsync(report.DepartmentId);
			projection.ModifiedOn = DateTime.UtcNow;
			projection.RowVersion += 1;
			projection.DeletedOn = report.DeletedOn;

			if (existing == null)
				await _projections.InsertAsync(projection, cancellationToken, true);
			else
				await _projections.UpdateAsync(projection, cancellationToken, true);
		}

		private async Task RecomputeGroupScopeAsync(IncidentReportAggregate aggregate, int? authorGroupId, CancellationToken cancellationToken)
		{
			var report = aggregate.Report;
			var scopes = new List<RmsRecordGroupScope>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			void add(int? groupId, RmsGroupScopeAnchorType anchor)
			{
				if (!groupId.HasValue || !seen.Add(groupId.Value + ":" + (int)anchor))
					return;
				scopes.Add(new RmsRecordGroupScope { DepartmentId = report.DepartmentId, RecordId = report.RmsIncidentReportId, DepartmentGroupId = groupId.Value, AnchorType = (int)anchor, CreatedOn = DateTime.UtcNow });
			}
			add(report.StationGroupId, RmsGroupScopeAnchorType.RecordGroup);
			add(authorGroupId ?? (await _groups.GetGroupForUserAsync(report.AuthorUserId, report.DepartmentId))?.DepartmentGroupId, RmsGroupScopeAnchorType.Author);
			foreach (var unit in aggregate.Units)
				add(unit.StationGroupIdSnapshot, RmsGroupScopeAnchorType.Unit);
			var now = DateTime.UtcNow;
			foreach (var share in (await _shares.GetForRecordAsync(report.DepartmentId, report.RmsIncidentReportId) ?? Enumerable.Empty<RmsRecordShare>()).Where(s => s.IsEffective(now)))
				add(share.DepartmentGroupId, RmsGroupScopeAnchorType.Share);
			await _scopes.ReplaceForRecordAsync(report.DepartmentId, report.RmsIncidentReportId, scopes, cancellationToken);
			aggregate.GroupScope = scopes;
		}

		private async Task<DomainEventOutboxEntry> EnqueueLifecycleEventAsync(RmsIncidentReport report, RmsRevision revision, WorkflowTriggerEventType trigger, RmsRecordState from, RmsRecordState to, string reasonCode, object submission, CancellationToken cancellationToken, object extra = null)
		{
			var payload = new Dictionary<string, object>
			{
				["record"] = RecordBlock(report, revision, to),
				["record_change"] = new
				{
					previous_state = from.ToString(),
					current_state = to.ToString(),
					prior_revision_id = revision?.PriorRevisionId,
					current_revision_id = revision?.RmsRevisionId ?? report.CurrentRevisionId,
					reason_code = reasonCode
				}
			};
			if (submission != null)
				payload["submission"] = submission;
			if (extra != null)
				payload["extra"] = extra;

			return await _outbox.EnqueueAsync(report.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = trigger.ToString(),
				SchemaVersion = 1,
				AggregateType = IncidentAggregate,
				AggregateId = report.RmsIncidentReportId,
				AggregateVersion = revision?.RevisionNumber ?? report.RevisionCount,
				Trigger = trigger,
				Payload = payload,
				CorrelationId = report.RmsIncidentReportId,
				OriginClient = (RmsOriginClient)report.OriginClient
			}, cancellationToken);
		}

		/// <summary>The record.* block for incident reports; kind and the NERIS ID distinguish it from operational records.</summary>
		public static object RecordBlock(RmsIncidentReport report, RmsRevision revision, RmsRecordState state)
		{
			return new
			{
				id = report.RmsIncidentReportId,
				kind = "IncidentReport",
				record_number = report.RecordNumber,
				draft_reference = report.DraftReference,
				definition_key = report.DefinitionKey,
				definition_version = report.DefinitionVersion,
				type_key = "NerisIncident",
				state = state.ToString(),
				lifecycle_preset = ((RmsLifecyclePreset)report.LifecyclePreset).ToString(),
				department_id = report.DepartmentId,
				station_group_id = report.StationGroupId,
				call_id = report.CallId,
				external_id = report.NerisIncidentId,
				author_user_id = report.AuthorUserId,
				owner_user_id = report.OwnerUserId,
				started_on = report.CallCreatedOn,
				ended_on = report.IncidentClearedOn,
				created_on = report.CreatedOn,
				finalized_on = report.FinalizedOn,
				revision_id = revision?.RmsRevisionId ?? report.CurrentRevisionId,
				revision_number = revision?.RevisionNumber ?? report.RevisionCount,
				checksum = revision?.Checksum,
				summary = report.DisplaySummary,
				incident_number = report.IncidentNumber,
				neris_incident_id = report.NerisIncidentId
			};
		}

		/// <summary>The sanitized submission.* block (plan 5.6): state, external status, attempts, codes and paths; never payload or response bodies.</summary>
		public static object SubmissionBlock(RmsSubmission submission)
		{
			return new
			{
				id = submission.RmsSubmissionId,
				destination = submission.Destination,
				destination_version = submission.DestinationVersion,
				state = ((RmsSubmissionState)submission.State).ToString(),
				external_id = submission.ExternalId,
				external_status = submission.ExternalStatus,
				attempts = submission.Attempts,
				max_attempts = submission.MaxAttempts,
				error_summary = submission.ErrorSummary,
				queued_on = submission.QueuedOn,
				sent_on = submission.SentOn,
				completed_on = submission.CompletedOn
			};
		}

		#endregion

		#region Small helpers

		private async Task<RmsIncidentReport> LoadAsync(int departmentId, string reportId)
		{
			var report = string.IsNullOrWhiteSpace(reportId) ? null : await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			if (report == null || report.DeletedOn.HasValue)
				throw new ArgumentException($"Incident report {reportId} was not found.", nameof(reportId));
			return report;
		}

		private static void RequireEditable(RmsIncidentReport report)
		{
			var state = (RmsRecordState)report.State;
			if (RmsLifecycle.IsEditable(state) || state == RmsRecordState.Rejected || report.AmendsRevisionId != null)
				return;
			throw new RecordTransitionException(report.RmsIncidentReportId, state, state, "the report is not editable in its current state");
		}

		private static void RequireTransition(RmsIncidentReport report, RmsRecordState from, RmsRecordState to)
		{
			if (!RmsLifecycle.CanTransition((RmsLifecyclePreset)report.LifecyclePreset, from, to))
				throw new RecordTransitionException(report.RmsIncidentReportId, from, to);
		}

		private async Task GuardVersionAsync(RmsIncidentReport report, long expectedRowVersion, CancellationToken cancellationToken)
		{
			if (!await _reports.TryBumpRowVersionAsync(report.DepartmentId, report.RmsIncidentReportId, expectedRowVersion, cancellationToken))
			{
				var current = await _reports.GetByIdForDepartmentAsync(report.DepartmentId, report.RmsIncidentReportId);
				throw new RecordConcurrencyException(report.RmsIncidentReportId, expectedRowVersion, current?.RowVersion ?? -1);
			}
			report.RowVersion = expectedRowVersion + 1;
		}

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await work();
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}

		private Task AuditAsync(int departmentId, string userId, string recordId, string revisionId, RmsAccessAuditAction action, string purpose, RmsOriginClient origin, CancellationToken cancellationToken, object detail = null, string ipAddress = null)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = recordId,
				RevisionId = revisionId,
				Action = (int)action,
				ActorUserId = userId,
				Purpose = purpose,
				OriginClient = (int)origin,
				IpAddress = ipAddress,
				Successful = true,
				OccurredOn = DateTime.UtcNow,
				DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);
		}

		private async Task<int> SafeCatalogVersionAsync(int departmentId)
		{
			try { return await _dataProtection.GetPinnedCatalogVersionAsync(departmentId); }
			catch (Exception ex) { Logging.LogException(ex); return 0; }
		}

		private async Task<long> SafePolicyEpochAsync(int departmentId)
		{
			try { return (await _dataProtection.GetPolicyByDepartmentIdAsync(departmentId))?.PolicyEpoch ?? 0; }
			catch (Exception ex) { Logging.LogException(ex); return 0; }
		}

		private static string BuildSummary(RmsIncidentReport report, List<RmsIncidentType> types, string callName)
		{
			var primary = types?.FirstOrDefault(t => t.IsPrimary)?.TypeCode ?? types?.FirstOrDefault()?.TypeCode;
			var label = primary == null ? (string.IsNullOrWhiteSpace(report.DispatchIncidentCode) ? "Incident" : report.DispatchIncidentCode) : primary.Split(new[] { "||" }, StringSplitOptions.None).Last().Replace('_', ' ');
			var number = report.IncidentNumber ?? report.DraftReference;
			var text = $"{label} - {number}";
			if (!string.IsNullOrWhiteSpace(callName))
				text += " - " + callName.Trim();
			return text.Length > 400 ? text.Substring(0, 400) : text;
		}

		private static T Copy<T>(T source, Action<T> assignId, string revisionId, DateTime now) where T : class
		{
			var copy = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source));
			assignId(copy);
			var type = typeof(T);
			type.GetProperty("RevisionId")?.SetValue(copy, revisionId);
			type.GetProperty("CreatedOn")?.SetValue(copy, now);
			type.GetProperty("ModifiedOn")?.SetValue(copy, now);
			type.GetProperty("RowVersion")?.SetValue(copy, 1L);
			return copy;
		}

		private static string SerializeSnapshot(IncidentReportAggregate aggregate)
		{
			var snapshot = new
			{
				SnapshotVersion = 1,
				aggregate.Report,
				aggregate.Location,
				aggregate.Types,
				aggregate.Units,
				aggregate.Aids,
				aggregate.Tactics,
				aggregate.Narrative,
				aggregate.Facts
			};
			return JsonConvert.SerializeObject(snapshot, Formatting.None);
		}

		private static List<string> SplitCsv(string csv)
		{
			return string.IsNullOrWhiteSpace(csv) ? new List<string>() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		private static string NewDraftReference()
		{
			const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
			var bytes = new byte[5];
			using (var rng = RandomNumberGenerator.Create())
				rng.GetBytes(bytes);
			var chars = new char[5];
			for (var i = 0; i < 5; i++)
				chars[i] = alphabet[bytes[i] % alphabet.Length];
			return "I-" + new string(chars);
		}

		#endregion
	}
}
