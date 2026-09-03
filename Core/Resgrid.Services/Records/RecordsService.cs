using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The Records (RMS) operational aggregate for the locked Logs-parity definitions (RMS plan sections
	/// 4.1, 4.8, 5.3, 5.7). Every mutation runs in one unit of work: header, typed detail, participants,
	/// units, group scope, search projection and the outbox row commit together, and the outbox rows are
	/// dispatched in-process after commit. Draft rows always mirror the latest revision when no amendment
	/// is open; finalize copies them under the new revision rather than moving them.
	/// </summary>
	public class RecordsService : IRecordsService
	{
		private const string AttestationStatementVersion = "1";
		private const int NumberAllocationRetries = 3;

		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordValueService _details;
		private readonly IRmsRecordParticipantsRepository _participants;
		private readonly IRmsRecordUnitResponsesRepository _units;
		private readonly IRmsRecordAttachmentsRepository _attachments;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsRecordGroupScopesRepository _scopes;
		private readonly IRmsRecordSharesRepository _shares;
		private readonly IRmsRecordSearchProjectionsRepository _projections;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IRecordsCutoverService _cutover;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUserProfileService _profiles;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _calls;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IOutboundQueueProvider _outboundQueue;
		private readonly IRecordAttachmentScanner _attachmentScanner;

		public RecordsService(IRmsOperationalRecordsRepository records, IRmsRecordValueService details,
			IRmsRecordParticipantsRepository participants, IRmsRecordUnitResponsesRepository units, IRmsRecordAttachmentsRepository attachments,
			IRmsRevisionsRepository revisions, IRmsRecordGroupScopesRepository scopes, IRmsRecordSharesRepository shares,
			IRmsRecordSearchProjectionsRepository projections, IRmsAccessAuditsRepository audits, IDomainEventOutboxService outbox,
			IRecordsCutoverService cutover, IDepartmentSettingsService settings, IDepartmentGroupsService groups, IUserProfileService profiles,
			IUnitsService unitsService, ICallsService calls, IDepartmentDataProtectionService dataProtection, IUnitOfWork unitOfWork,
			IOutboundQueueProvider outboundQueue, IRecordAttachmentScanner attachmentScanner)
		{
			_records = records;
			_details = details;
			_participants = participants;
			_units = units;
			_attachments = attachments;
			_revisions = revisions;
			_scopes = scopes;
			_shares = shares;
			_projections = projections;
			_audits = audits;
			_outbox = outbox;
			_cutover = cutover;
			_settings = settings;
			_groups = groups;
			_profiles = profiles;
			_unitsService = unitsService;
			_calls = calls;
			_dataProtection = dataProtection;
			_unitOfWork = unitOfWork;
			_outboundQueue = outboundQueue;
			_attachmentScanner = attachmentScanner;
		}

		#region Create / save

		public async Task<RecordAggregate> CreateDraftAsync(int departmentId, string userId, RecordDraftInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("An acting user is required.", nameof(userId));
			if (!RmsDefinitionKeys.LockedTypes.TryGetValue(input.DefinitionKey ?? string.Empty, out var recordType))
				throw new ArgumentException($"'{input.DefinitionKey}' is not a published definition.", nameof(input));

			await EnsureRecordsUsableAsync(departmentId);

			if (!string.IsNullOrWhiteSpace(input.IdempotencyKey))
			{
				var existing = await _records.GetByIdempotencyKeyAsync(departmentId, input.IdempotencyKey);
				if (existing != null)
					return await HydrateAsync(existing, false);
			}

			var now = DateTime.UtcNow;
			var recordId = ResolveClientRecordId(input.ClientRecordId);
			var authorGroup = await _groups.GetGroupForUserAsync(userId, departmentId);

			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = recordId,
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = input.DefinitionKey,
				DefinitionVersion = RmsDefinitionKeys.LockedDefinitionVersion,
				RecordType = (int)recordType,
				LifecyclePreset = (int)RmsDefinitionKeys.LockedDefaultPreset,
				State = (int)RmsRecordState.Draft,
				DraftReference = NewDraftReference(),
				StationGroupId = input.StationGroupId ?? authorGroup?.DepartmentGroupId,
				CallId = input.CallId,
				ExternalId = input.ExternalId,
				AuthorUserId = userId,
				AuthorGroupIdSnapshot = authorGroup?.DepartmentGroupId,
				OwnerUserId = userId,
				StartedOn = input.StartedOn,
				EndedOn = input.EndedOn,
				IdempotencyKey = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? null : input.IdempotencyKey,
				OriginClient = (int)input.OriginClient,
				CreatedOn = now,
				CreatedByUserId = userId,
				ModifiedOn = now,
				ModifiedByUserId = userId,
				RowVersion = 1
			};

			var details = new RmsOperationalRecordDetail
			{
				RmsOperationalRecordDetailId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = recordId,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};
			ApplyDetails(details, input.Details);
			await ApplyCallSnapshotAsync(departmentId, details, input.CallId);
			ValidateDefinitionRequirements(recordType, details);

			var participants = await BuildParticipantsAsync(departmentId, recordId, input.Participants, now);
			var units = await BuildUnitsAsync(departmentId, recordId, input.Units, now);
			record.DisplaySummary = BuildDisplaySummary(recordType, record, details, units);

			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await _records.InsertAsync(record, cancellationToken, true);
				await _details.InsertAsync(details, cancellationToken);
				foreach (var participant in participants)
					await _participants.InsertAsync(participant, cancellationToken, true);
				foreach (var unit in units)
					await _units.InsertAsync(unit, cancellationToken, true);

				var aggregate = new RecordAggregate { Record = record, Details = details, Participants = participants, Units = units };
				await RecomputeGroupScopeAsync(aggregate, cancellationToken);
				await UpsertProjectionAsync(aggregate, cancellationToken);

				outboxIds.Add((await EnqueueLifecycleEventAsync(record, null, WorkflowTriggerEventType.RecordCreated, RmsRecordState.Draft, RmsRecordState.Draft, null, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Change, "Create draft", input.OriginClient, cancellationToken,
					string.IsNullOrWhiteSpace(input.DuplicateContinueReason) ? null : new { duplicateContinueReason = input.DuplicateContinueReason });
			});

			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, recordId, false);
		}

		public async Task<RecordAggregate> SaveDraftAsync(int departmentId, string userId, string recordId, long expectedRowVersion, RecordDraftInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var record = await LoadRecordAsync(departmentId, recordId);
			var state = (RmsRecordState)record.State;

			if (!RmsLifecycle.IsEditable(state) && record.AmendsRevisionId == null)
				throw new RecordTransitionException(recordId, state, state, "the Record is not editable in this state");

			var recordType = (RmsOperationalRecordType)record.RecordType.GetValueOrDefault();
			var now = DateTime.UtcNow;

			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, expectedRowVersion, cancellationToken);

				var details = await _details.GetDraftAsync(departmentId, recordId) ?? new RmsOperationalRecordDetail
				{
					RmsOperationalRecordDetailId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					ProtectionId = Guid.NewGuid().ToString(),
					RecordId = recordId,
					CreatedOn = now,
					RowVersion = 0
				};
				ApplyDetails(details, input.Details);
				if (input.CallId != record.CallId)
					await ApplyCallSnapshotAsync(departmentId, details, input.CallId);
				ValidateDefinitionRequirements(recordType, details);
				details.ModifiedOn = now;
				details.RowVersion += 1;
				await _details.SaveOrUpdateAsync(details, cancellationToken);

				await _participants.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
				await _units.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
				var participants = await BuildParticipantsAsync(departmentId, recordId, input.Participants, now);
				var units = await BuildUnitsAsync(departmentId, recordId, input.Units, now);
				foreach (var participant in participants)
					await _participants.InsertAsync(participant, cancellationToken, true);
				foreach (var unit in units)
					await _units.InsertAsync(unit, cancellationToken, true);

				record.CallId = input.CallId;
				record.StationGroupId = input.StationGroupId ?? record.StationGroupId;
				record.ExternalId = input.ExternalId;
				record.StartedOn = input.StartedOn;
				record.EndedOn = input.EndedOn;
				record.DisplaySummary = BuildDisplaySummary(recordType, record, details, units);
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				if (state == RmsRecordState.Returned)
					record.State = (int)RmsRecordState.Draft;
				await _records.UpdateAsync(record, cancellationToken, true);

				var aggregate = new RecordAggregate { Record = record, Details = details, Participants = participants, Units = units };
				await RecomputeGroupScopeAsync(aggregate, cancellationToken);
				await UpsertProjectionAsync(aggregate, cancellationToken);
				// Draft autosaves emit no Workflow event (RMS plan section 5.6).
			});

			return await GetAsync(departmentId, recordId, false);
		}

		#endregion

		#region Lifecycle transitions

		public async Task<RecordAggregate> SubmitForReviewAsync(int departmentId, string userId, string recordId, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			var record = await LoadRecordAsync(departmentId, recordId);
			var from = (RmsRecordState)record.State;
			RequireTransition(record, from, RmsRecordState.ReadyForReview);

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, expectedRowVersion, cancellationToken);
				record.State = (int)RmsRecordState.ReadyForReview;
				record.SubmittedForReviewOn = now;
				record.ReviewDueOn = now.AddHours(await _settings.GetRecordsReviewDueHoursAsync(departmentId));
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				await RefreshProjectionAsync(record, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(record, null, WorkflowTriggerEventType.RecordSubmittedForReview, from, RmsRecordState.ReadyForReview, null, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Change, "Submit for review", (RmsOriginClient)record.OriginClient, cancellationToken);
			});

			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, recordId, false);
		}

		public async Task<RecordAggregate> ReturnForCorrectionAsync(int departmentId, string userId, string recordId, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("A reason code is required to return a Record.", nameof(reasonCode));
			var record = await LoadRecordAsync(departmentId, recordId);
			var from = (RmsRecordState)record.State;
			RequireTransition(record, from, RmsRecordState.Returned);

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, record.RowVersion, cancellationToken);
				record.State = (int)RmsRecordState.Returned;
				record.ReturnedOn = now;
				record.ReturnReasonCode = reasonCode;
				record.ReturnReasonText = reasonText;
				record.ReturnCount += 1;
				record.ReviewerUserId = userId;
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				await RefreshProjectionAsync(record, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(record, null, WorkflowTriggerEventType.RecordReturnedForCorrection, from, RmsRecordState.Returned, reasonCode, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Change, "Return for correction", RmsOriginClient.Web, cancellationToken, new { reasonCode });
			});

			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);

			// Notification EventTypes 31 (RecordReturnedForCorrection, RMS plan section 4.7): author-targeted, delivered by
			// the notification worker through IRecordsNotificationService. Enqueued after commit and never blocking.
			if (!await _outboundQueue.EnqueueNotification(new NotificationItem
			{
				DepartmentId = departmentId,
				Type = (int)EventTypes.RecordReturnedForCorrection,
				Value = recordId,
				UserId = record.AuthorUserId
			}))
				Logging.LogError($"Unable to enqueue the return-for-correction notification for record {recordId}.");

			return await GetAsync(departmentId, recordId, false);
		}

		public async Task<RecordAggregate> ApproveAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default)
		{
			var record = await LoadRecordAsync(departmentId, recordId);
			var from = (RmsRecordState)record.State;
			RequireTransition(record, from, RmsRecordState.Approved);
			if (string.Equals(record.AuthorUserId, userId, StringComparison.Ordinal))
				throw new RecordTransitionException(recordId, from, RmsRecordState.Approved, "the approver may not be the author");

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, record.RowVersion, cancellationToken);
				record.State = (int)RmsRecordState.Approved;
				record.ApprovedOn = now;
				record.ApproverUserId = userId;
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				await RefreshProjectionAsync(record, cancellationToken);
				// RecordApproved (103) is an RMS-1B trigger; no event until it is appended.
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Sign, "Approve", RmsOriginClient.Web, cancellationToken);
			});

			return await GetAsync(departmentId, recordId, false);
		}

		public async Task<RecordAggregate> FinalizeAsync(int departmentId, string userId, string recordId, long expectedRowVersion, string attestationStatementVersion, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			var record = await LoadRecordAsync(departmentId, recordId);
			var from = (RmsRecordState)record.State;
			var isAmendment = record.AmendsRevisionId != null;
			var to = isAmendment ? RmsRecordState.Amended : RmsRecordState.Finalized;
			RequireTransition(record, from, to);

			if (isAmendment && string.IsNullOrWhiteSpace(reasonCode))
				throw new ArgumentException("A reason code is required to finalize an amendment.", nameof(reasonCode));

			var now = DateTime.UtcNow;
			var recordType = (RmsOperationalRecordType)record.RecordType.GetValueOrDefault();
			var outboxIds = new List<long>();
			RmsRevision revision = null;

			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, expectedRowVersion, cancellationToken);

				var draft = await HydrateDraftAsync(record);
				ValidateDefinitionRequirements(recordType, draft.Details);
				ValidateForFinalization(recordType, draft);

				if (string.IsNullOrWhiteSpace(record.RecordNumber))
					record.RecordNumber = await AllocateRecordNumberAsync(record, cancellationToken);

				revision = await WriteRevisionAsync(record, draft, isAmendment ? RmsRevisionTransition.Amended : RmsRevisionTransition.Finalized,
					userId, reasonCode, reasonText, attestationStatementVersion ?? AttestationStatementVersion, now, cancellationToken);

				var priorState = from;
				record.State = (int)to;
				if (!isAmendment)
				{
					record.FinalizedOn = now;
					record.FinalizedByUserId = userId;
				}
				record.CurrentRevisionId = revision.RmsRevisionId;
				record.RevisionCount = revision.RevisionNumber;
				record.AmendsRevisionId = null;
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);

				draft.Record = record;
				await RecomputeGroupScopeAsync(draft, cancellationToken);
				await UpsertProjectionAsync(draft, cancellationToken);

				var lifecycle = await EnqueueLifecycleEventAsync(record, revision, isAmendment ? WorkflowTriggerEventType.RecordAmended : WorkflowTriggerEventType.RecordFinalized, priorState, to, reasonCode, cancellationToken);
				outboxIds.Add(lifecycle.DomainEventOutboxId);

				// Legacy LogAdded compatibility (plan section 5.6): eligible Logs-parity records also project the exact
				// log.* contract once, at finalize, through the same outbox. Unit Activity never emitted LogAdded and
				// does not start now; amendments never re-fire it.
				if (!isAmendment && LogAddedCompatibility.IsEligible(record))
					outboxIds.Add((await EnqueueLogAddedCompatibilityAsync(record, revision, draft.Details, lifecycle.EventId, now, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, recordId, revision.RmsRevisionId, RmsAccessAuditAction.Sign, isAmendment ? "Finalize amendment" : "Finalize", (RmsOriginClient)record.OriginClient, cancellationToken, new { revision.RevisionNumber, revision.Checksum, reasonCode });
			});

			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, recordId, true);
		}

		public async Task<RecordAggregate> OpenAmendmentAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default)
		{
			var record = await LoadRecordAsync(departmentId, recordId);
			var state = (RmsRecordState)record.State;
			if (!RmsLifecycle.CanTransition((RmsLifecyclePreset)record.LifecyclePreset, state, RmsRecordState.Amended))
				throw new RecordTransitionException(recordId, state, RmsRecordState.Amended);
			if (record.AmendsRevisionId != null)
				throw new RecordTransitionException(recordId, state, RmsRecordState.Amended, "an amendment draft is already open");

			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, record.RowVersion, cancellationToken);
				record.AmendsRevisionId = record.CurrentRevisionId;
				record.OwnerUserId = userId;
				record.ModifiedOn = DateTime.UtcNow;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				await AuditAsync(departmentId, userId, recordId, record.CurrentRevisionId, RmsAccessAuditAction.Change, "Open amendment", RmsOriginClient.Web, cancellationToken);
			});

			return await GetAsync(departmentId, recordId, true);
		}

		public async Task<RecordAggregate> AbandonAmendmentAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default)
		{
			var record = await LoadRecordAsync(departmentId, recordId);
			if (record.AmendsRevisionId == null)
				return await GetAsync(departmentId, recordId, true);

			var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, record.CurrentRevisionId);
			var snapshot = RecordSnapshotSerializer.Deserialize(revision?.SnapshotJson);
			if (snapshot == null)
				throw new InvalidOperationException($"Revision {record.CurrentRevisionId} has no snapshot to restore.");

			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, record.RowVersion, cancellationToken);
				await RestoreDraftFromSnapshotAsync(record, snapshot, now, cancellationToken);
				record.AmendsRevisionId = null;
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				var aggregate = await HydrateDraftAsync(record);
				await RecomputeGroupScopeAsync(aggregate, cancellationToken);
				await UpsertProjectionAsync(aggregate, cancellationToken);
				await AuditAsync(departmentId, userId, recordId, record.CurrentRevisionId, RmsAccessAuditAction.Change, "Abandon amendment", RmsOriginClient.Web, cancellationToken);
			});

			return await GetAsync(departmentId, recordId, true);
		}

		public async Task<RecordAggregate> VoidAsync(int departmentId, string userId, string recordId, string reasonCode, string reasonText, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("A reason code is required to void a Record.", nameof(reasonCode));
			var record = await LoadRecordAsync(departmentId, recordId);
			var from = (RmsRecordState)record.State;
			RequireTransition(record, from, RmsRecordState.Voided);
			if (record.AmendsRevisionId != null)
				throw new RecordTransitionException(recordId, from, RmsRecordState.Voided, "abandon the open amendment first");

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, record.RowVersion, cancellationToken);
				var draft = await HydrateDraftAsync(record);
				var revision = await WriteRevisionAsync(record, draft, RmsRevisionTransition.Voided, userId, reasonCode, reasonText, AttestationStatementVersion, now, cancellationToken);

				record.State = (int)RmsRecordState.Voided;
				record.VoidedOn = now;
				record.VoidedByUserId = userId;
				record.VoidReasonCode = reasonCode;
				record.VoidReasonText = reasonText;
				record.CurrentRevisionId = revision.RmsRevisionId;
				record.RevisionCount = revision.RevisionNumber;
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);

				draft.Record = record;
				await UpsertProjectionAsync(draft, cancellationToken);
				outboxIds.Add((await EnqueueLifecycleEventAsync(record, revision, WorkflowTriggerEventType.RecordVoided, from, RmsRecordState.Voided, reasonCode, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, recordId, revision.RmsRevisionId, RmsAccessAuditAction.Change, "Void", RmsOriginClient.Web, cancellationToken, new { reasonCode });
			});

			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, recordId, true);
		}

		public async Task<RecordAggregate> CancelAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default)
		{
			var record = await LoadRecordAsync(departmentId, recordId);
			var from = (RmsRecordState)record.State;
			RequireTransition(record, from, RmsRecordState.Cancelled);

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, record.RowVersion, cancellationToken);
				record.State = (int)RmsRecordState.Cancelled;
				record.CancelledOn = now;
				record.CancelledByUserId = userId;
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				await RefreshProjectionAsync(record, cancellationToken);
				// Under OnFinalize numbering a cancelled draft holds no number, so the disposition is "none".
				outboxIds.Add((await EnqueueLifecycleEventAsync(record, null, WorkflowTriggerEventType.RecordCancelled, from, RmsRecordState.Cancelled, null, cancellationToken,
					new { number_disposition = record.RecordNumber == null ? "none" : "voided", record.RecordNumber })).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Change, "Cancel", RmsOriginClient.Web, cancellationToken);
			});

			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return await GetAsync(departmentId, recordId, false);
		}

		public async Task<RecordAggregate> ReassignDraftAsync(int departmentId, string userId, string recordId, string newOwnerUserId, string reason, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(newOwnerUserId)) throw new ArgumentException("A new owner is required.", nameof(newOwnerUserId));
			var record = await LoadRecordAsync(departmentId, recordId);
			var state = (RmsRecordState)record.State;
			if (RmsLifecycle.IsFinalizedFamily(state) && record.AmendsRevisionId == null || RmsLifecycle.IsTerminal(state))
				throw new RecordTransitionException(recordId, state, state, "only an unfinalized Record or an open amendment can be reassigned");

			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(record, record.RowVersion, cancellationToken);
				var previousOwner = record.OwnerUserId;
				record.OwnerUserId = newOwnerUserId;
				record.ModifiedOn = DateTime.UtcNow;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				await RefreshProjectionAsync(record, cancellationToken);
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Admin, "Reassign draft", RmsOriginClient.Web, cancellationToken, new { previousOwner, newOwnerUserId, reason });
			});

			return await GetAsync(departmentId, recordId, false);
		}

		#endregion

		#region Reads

		public async Task<RecordAggregate> GetAsync(int departmentId, string recordId, bool includeRevisions = false)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
			if (record == null || record.DeletedOn.HasValue)
				return null;

			return await HydrateAsync(record, includeRevisions);
		}

		public async Task<List<RmsOperationalRecord>> GetOutstandingForOwnerAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return new List<RmsOperationalRecord>();

			var open = new[] { (int)RmsRecordState.Draft, (int)RmsRecordState.ReadyForReview, (int)RmsRecordState.Returned, (int)RmsRecordState.Approved };
			var records = await _records.GetByOwnerAndStatesAsync(departmentId, userId, open) ?? Enumerable.Empty<RmsOperationalRecord>();
			return records.Where(r => r != null && !r.DeletedOn.HasValue).OrderBy(r => r.CreatedOn).ToList();
		}

		public async Task<List<RmsOperationalRecord>> GetDuplicateCandidatesAsync(int departmentId, string definitionKey, int callId)
		{
			var records = await _records.GetByCallAsync(departmentId, callId);
			return (records ?? Enumerable.Empty<RmsOperationalRecord>())
				.Where(r => string.Equals(r.DefinitionKey, definitionKey, StringComparison.Ordinal) && !RmsLifecycle.IsTerminal((RmsRecordState)r.State))
				.ToList();
		}

		public async Task<List<RmsRecordSearchProjection>> QueryAsync(int departmentId, RmsRecordQuery query)
		{
			return (await _projections.QueryAsync(departmentId, query ?? new RmsRecordQuery()))?.ToList() ?? new List<RmsRecordSearchProjection>();
		}

		public Task<int> CountAsync(int departmentId, RmsRecordQuery query)
		{
			return _projections.CountAsync(departmentId, query ?? new RmsRecordQuery());
		}

		public async Task<List<int>> GetYearsAsync(int departmentId)
		{
			return (await _projections.GetYearsAsync(departmentId))?.ToList() ?? new List<int>();
		}

		public async Task<List<RmsRevision>> GetRevisionsAsync(int departmentId, string recordId)
		{
			return (await _revisions.GetForRecordAsync(departmentId, recordId))?.ToList() ?? new List<RmsRevision>();
		}

		public async Task<RecordSnapshot> GetRevisionSnapshotAsync(int departmentId, string revisionId)
		{
			var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, revisionId);
			return revision == null ? null : RecordSnapshotSerializer.Deserialize(revision.SnapshotJson);
		}

		public async Task<List<RecordFieldDiff>> DiffRevisionsAsync(int departmentId, string fromRevisionId, string toRevisionId, bool canViewRestricted)
		{
			var from = await GetRevisionSnapshotAsync(departmentId, fromRevisionId);
			var to = await GetRevisionSnapshotAsync(departmentId, toRevisionId);
			if (from == null || to == null)
				throw new KeyNotFoundException("One of the revisions does not exist in this department.");

			return RecordSnapshotSerializer.Diff(from, to, canViewRestricted);
		}

		public Task RecordAccessAsync(int departmentId, string userId, string recordId, string revisionId, RmsAccessAuditAction action, string purpose = null, string ipAddress = null, RmsOriginClient originClient = RmsOriginClient.Web)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = recordId,
				RevisionId = revisionId,
				Action = (int)action,
				ActorUserId = userId,
				Purpose = purpose,
				OriginClient = (int)originClient,
				IpAddress = ipAddress,
				Successful = true,
				OccurredOn = DateTime.UtcNow
			}, CancellationToken.None, true);
		}

		#endregion

		public async Task<List<RmsRecordSearchProjection>> GetProjectionsByIdsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			return (await _projections.GetByIdsAsync(departmentId, recordIds))?.ToList() ?? new List<RmsRecordSearchProjection>();
		}

		#region Attachments

		public async Task<RmsRecordAttachment> AddAttachmentAsync(int departmentId, string userId, string recordId, string fileName, string contentType, byte[] data, string description, CancellationToken cancellationToken = default)
		{
			if (data == null || data.Length == 0) throw new ArgumentException("Attachment content is required.", nameof(data));
			var record = await LoadRecordAsync(departmentId, recordId);
			if (RmsLifecycle.IsTerminal((RmsRecordState)record.State))
				throw new RecordTransitionException(recordId, (RmsRecordState)record.State, (RmsRecordState)record.State, "attachments cannot be added to a voided or cancelled Record");

			// Media hygiene (plan section 4.7): images are re-encoded so EXIF/XMP/IPTC never reach storage, active
			// content is refused, and the bytes pass the configured scanner before they are stored.
			var hygiene = RecordAttachmentHygiene.Sanitize(fileName, contentType, data);
			var scan = await _attachmentScanner.ScanAsync(hygiene.FileName, hygiene.ContentType, hygiene.Data, cancellationToken) ?? new RecordAttachmentScanResult();
			if (scan.State == RmsAttachmentScanState.Rejected)
				throw new RecordAttachmentRejectedException($"Attachment '{hygiene.FileName}' was rejected by the scanner: {scan.Detail}");

			var now = DateTime.UtcNow;
			var attachment = new RmsRecordAttachment
			{
				RmsRecordAttachmentId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = recordId,
				FileName = hygiene.FileName,
				ContentType = hygiene.ContentType,
				ByteSize = hygiene.Data.LongLength,
				Checksum = RecordSnapshotSerializer.Checksum(hygiene.Data),
				Data = hygiene.Data,
				Description = description,
				UploadedByUserId = userId,
				UploadedOn = now,
				ScanState = (int)scan.State,
				MetadataStripped = hygiene.MetadataStripped,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};

			await InTransactionAsync(async () =>
			{
				await _attachments.InsertAsync(attachment, cancellationToken, true);
				record.ModifiedOn = now;
				record.ModifiedByUserId = userId;
				await _records.UpdateAsync(record, cancellationToken, true);
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Change, "Add attachment", RmsOriginClient.Web, cancellationToken, new { attachment.RmsRecordAttachmentId, attachment.ByteSize, attachment.Checksum });
			});

			attachment.Data = null;
			return attachment;
		}

		public Task<RmsRecordAttachment> GetAttachmentAsync(int departmentId, string attachmentId)
		{
			return _attachments.GetByIdForDepartmentAsync(departmentId, attachmentId);
		}

		public async Task<bool> RemoveAttachmentAsync(int departmentId, string userId, string recordId, string attachmentId, CancellationToken cancellationToken = default)
		{
			var record = await LoadRecordAsync(departmentId, recordId);
			if (RmsLifecycle.IsFinalizedFamily((RmsRecordState)record.State) && record.AmendsRevisionId == null)
				throw new RecordTransitionException(recordId, (RmsRecordState)record.State, (RmsRecordState)record.State, "attachments on a finalized Record are removed through an amendment");

			var attachment = await _attachments.GetByIdForDepartmentAsync(departmentId, attachmentId);
			if (attachment == null || !string.Equals(attachment.RecordId, recordId, StringComparison.Ordinal))
				return false;

			await InTransactionAsync(async () =>
			{
				attachment.DeletedOn = DateTime.UtcNow;
				attachment.ModifiedOn = attachment.DeletedOn.Value;
				attachment.RowVersion += 1;
				await _attachments.UpdateAsync(attachment, cancellationToken, true);
				await AuditAsync(departmentId, userId, recordId, null, RmsAccessAuditAction.Change, "Remove attachment", RmsOriginClient.Web, cancellationToken, new { attachmentId });
			});

			return true;
		}

		#endregion

		#region Internals

		private async Task EnsureRecordsUsableAsync(int departmentId)
		{
			var state = await _cutover.GetModuleStateAsync(departmentId);
			if (!state.RecordsUsable)
				throw new InvalidOperationException("Records is not active for this department.");
		}

		private async Task<RmsOperationalRecord> LoadRecordAsync(int departmentId, string recordId)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
			if (record == null || record.DeletedOn.HasValue)
				throw new KeyNotFoundException($"Record {recordId} does not exist in this department.");
			return record;
		}

		private static void RequireTransition(RmsOperationalRecord record, RmsRecordState from, RmsRecordState to)
		{
			if (!RmsLifecycle.CanTransition((RmsLifecyclePreset)record.LifecyclePreset, from, to))
				throw new RecordTransitionException(record.RmsOperationalRecordId, from, to);
		}

		/// <summary>Serializes writers on the row and rejects stale ETags; bumps the in-memory version to match.</summary>
		private async Task GuardVersionAsync(RmsOperationalRecord record, long expectedRowVersion, CancellationToken cancellationToken)
		{
			if (!await _records.TryBumpRowVersionAsync(record.DepartmentId, record.RmsOperationalRecordId, expectedRowVersion, cancellationToken))
			{
				var current = await _records.GetByIdForDepartmentAsync(record.DepartmentId, record.RmsOperationalRecordId);
				throw new RecordConcurrencyException(record.RmsOperationalRecordId, expectedRowVersion, current?.RowVersion ?? -1);
			}

			record.RowVersion = expectedRowVersion + 1;
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

		private async Task<RecordAggregate> HydrateAsync(RmsOperationalRecord record, bool includeRevisions)
		{
			var aggregate = await HydrateDraftAsync(record);
			aggregate.GroupScope = (await _scopes.GetForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId))?.ToList() ?? new List<RmsRecordGroupScope>();
			if (includeRevisions)
				aggregate.Revisions = (await _revisions.GetForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId))?.ToList() ?? new List<RmsRevision>();
			return aggregate;
		}

		private async Task<RecordAggregate> HydrateDraftAsync(RmsOperationalRecord record)
		{
			return new RecordAggregate
			{
				Record = record,
				Details = await _details.GetDraftAsync(record.DepartmentId, record.RmsOperationalRecordId),
				Participants = (await _participants.GetForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId, null))?.ToList() ?? new List<RmsRecordParticipant>(),
				Units = (await _units.GetForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId, null))?.ToList() ?? new List<RmsRecordUnitResponse>(),
				Attachments = (await _attachments.GetMetadataForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId))?.ToList() ?? new List<RmsRecordAttachment>()
			};
		}

		private async Task<RmsRevision> WriteRevisionAsync(RmsOperationalRecord record, RecordAggregate draft, RmsRevisionTransition transition, string userId,
			string reasonCode, string reasonText, string attestationVersion, DateTime now, CancellationToken cancellationToken)
		{
			var snapshot = RecordSnapshotSerializer.Build(draft);
			snapshot.RecordNumber = record.RecordNumber;
			var json = RecordSnapshotSerializer.Serialize(snapshot);

			var revision = new RmsRevision
			{
				RmsRevisionId = Guid.NewGuid().ToString(),
				DepartmentId = record.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = record.RmsOperationalRecordId,
				RecordKind = (int)RmsRecordKind.Operational,
				RevisionNumber = record.RevisionCount + 1,
				Transition = (int)transition,
				PriorRevisionId = record.CurrentRevisionId,
				DefinitionKey = record.DefinitionKey,
				DefinitionVersion = record.DefinitionVersion,
				SnapshotJson = json,
				Checksum = RecordSnapshotSerializer.Checksum(json),
				ActorUserId = userId,
				ReasonCode = reasonCode,
				ReasonText = reasonText,
				AttestationStatementVersion = transition == RmsRevisionTransition.Voided ? null : attestationVersion,
				AttestedOn = transition == RmsRevisionTransition.Voided ? (DateTime?)null : now,
				OriginClient = record.OriginClient,
				CreatedOn = now
			};
			await _revisions.InsertAsync(revision, cancellationToken, true);

			// Revision-bound copies of the typed rows keep finalized data queryable without touching the draft rows.
			if (draft.Details != null)
			{
				var copy = CloneDetails(draft.Details);
				copy.RmsOperationalRecordDetailId = Guid.NewGuid().ToString();
				copy.RevisionId = revision.RmsRevisionId;
				copy.CreatedOn = now;
				copy.ModifiedOn = now;
				copy.RowVersion = 1;
				await _details.InsertAsync(copy, cancellationToken);
			}

			foreach (var participant in draft.Participants)
			{
				var copy = CloneParticipant(participant);
				copy.RmsRecordParticipantId = Guid.NewGuid().ToString();
				copy.RevisionId = revision.RmsRevisionId;
				await _participants.InsertAsync(copy, cancellationToken, true);
			}

			foreach (var unit in draft.Units)
			{
				var copy = CloneUnit(unit);
				copy.RmsRecordUnitResponseId = Guid.NewGuid().ToString();
				copy.RevisionId = revision.RmsRevisionId;
				await _units.InsertAsync(copy, cancellationToken, true);
			}

			return revision;
		}

		private async Task RestoreDraftFromSnapshotAsync(RmsOperationalRecord record, RecordSnapshot snapshot, DateTime now, CancellationToken cancellationToken)
		{
			var details = await _details.GetDraftAsync(record.DepartmentId, record.RmsOperationalRecordId);
			if (details != null && snapshot.Details != null)
			{
				var restored = CloneDetails(snapshot.Details);
				restored.RmsOperationalRecordDetailId = details.RmsOperationalRecordDetailId;
				restored.ProtectionId = details.ProtectionId;
				restored.RevisionId = null;
				restored.CreatedOn = details.CreatedOn;
				restored.ModifiedOn = now;
				restored.RowVersion = details.RowVersion + 1;
				await _details.UpdateAsync(restored, cancellationToken);
			}

			await _participants.DeleteDraftForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId, cancellationToken);
			await _units.DeleteDraftForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId, cancellationToken);
			foreach (var participant in snapshot.Participants ?? new List<RmsRecordParticipant>())
			{
				var copy = CloneParticipant(participant);
				copy.RmsRecordParticipantId = Guid.NewGuid().ToString();
				copy.RevisionId = null;
				copy.ModifiedOn = now;
				await _participants.InsertAsync(copy, cancellationToken, true);
			}
			foreach (var unit in snapshot.Units ?? new List<RmsRecordUnitResponse>())
			{
				var copy = CloneUnit(unit);
				copy.RmsRecordUnitResponseId = Guid.NewGuid().ToString();
				copy.RevisionId = null;
				copy.ModifiedOn = now;
				await _units.InsertAsync(copy, cancellationToken, true);
			}

			record.StationGroupId = snapshot.StationGroupId;
			record.CallId = snapshot.CallId;
			record.ExternalId = snapshot.ExternalId;
			record.StartedOn = snapshot.StartedOn;
			record.EndedOn = snapshot.EndedOn;
		}

		private async Task<string> AllocateRecordNumberAsync(RmsOperationalRecord record, CancellationToken cancellationToken)
		{
			var config = await _settings.GetRecordsNumberingConfigAsync(record.DepartmentId);
			var prefixBase = RmsDefinitionKeys.DefaultNumberPrefix(record.DefinitionKey);
			var year = (record.StartedOn ?? DateTime.UtcNow).Year;
			var prefix = prefixBase + "-";
			if (config.PerGroupSequence && record.StationGroupId.HasValue)
				prefix += "G" + record.StationGroupId.Value + "-";
			if (config.IncludeYear)
				prefix += year + "-";

			var width = Math.Max(3, Math.Min(8, config.SequenceWidth <= 0 ? 4 : config.SequenceWidth));
			var sequence = await _records.GetMaxRecordNumberSequenceAsync(record.DepartmentId, prefix) + 1;
			return prefix + sequence.ToString("D" + width);
		}

		private async Task RecomputeGroupScopeAsync(RecordAggregate aggregate, CancellationToken cancellationToken)
		{
			var record = aggregate.Record;
			var scopes = new List<RmsRecordGroupScope>();
			var seen = new HashSet<string>(StringComparer.Ordinal);

			void add(int? groupId, RmsGroupScopeAnchorType anchor)
			{
				if (!groupId.HasValue || !seen.Add(groupId.Value + ":" + (int)anchor))
					return;
				scopes.Add(new RmsRecordGroupScope { DepartmentId = record.DepartmentId, RecordId = record.RmsOperationalRecordId, DepartmentGroupId = groupId.Value, AnchorType = (int)anchor, CreatedOn = DateTime.UtcNow });
			}

			add(record.StationGroupId, RmsGroupScopeAnchorType.RecordGroup);
			add(record.AuthorGroupIdSnapshot, RmsGroupScopeAnchorType.Author);
			foreach (var participant in aggregate.Participants)
				add(participant.GroupIdSnapshot, RmsGroupScopeAnchorType.Participant);
			foreach (var unit in aggregate.Units)
				add(unit.StationGroupIdSnapshot, RmsGroupScopeAnchorType.Unit);

			var shares = await _shares.GetForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId);
			var now = DateTime.UtcNow;
			foreach (var share in (shares ?? Enumerable.Empty<RmsRecordShare>()).Where(s => s.IsEffective(now)))
				add(share.DepartmentGroupId, RmsGroupScopeAnchorType.Share);

			await _scopes.ReplaceForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId, scopes, cancellationToken);
			aggregate.GroupScope = scopes;
		}

		private async Task RefreshProjectionAsync(RmsOperationalRecord record, CancellationToken cancellationToken)
		{
			var aggregate = await HydrateDraftAsync(record);
			aggregate.GroupScope = (await _scopes.GetForRecordAsync(record.DepartmentId, record.RmsOperationalRecordId))?.ToList() ?? new List<RmsRecordGroupScope>();
			await UpsertProjectionAsync(aggregate, cancellationToken);
		}

		private async Task UpsertProjectionAsync(RecordAggregate aggregate, CancellationToken cancellationToken)
		{
			var record = aggregate.Record;
			var details = aggregate.Details ?? new RmsOperationalRecordDetail();
			var existing = await _projections.GetByRecordIdAsync(record.DepartmentId, record.RmsOperationalRecordId);
			var projection = existing ?? new RmsRecordSearchProjection
			{
				RmsRecordSearchProjectionId = record.RmsOperationalRecordId,
				DepartmentId = record.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				CreatedOn = DateTime.UtcNow,
				RowVersion = 0
			};

			projection.SourceType = (int)RmsSearchSourceType.Record;
			projection.SourceId = record.RmsOperationalRecordId;
			projection.RecordKind = (int)RmsRecordKind.Operational;
			projection.RecordNumber = record.RecordNumber;
			projection.DraftReference = record.DraftReference;
			projection.DefinitionKey = record.DefinitionKey;
			projection.DefinitionVersion = record.DefinitionVersion;
			projection.RecordType = record.RecordType;
			projection.State = record.State;
			projection.OccurredOn = record.StartedOn ?? details.ActivityOn;
			projection.RecordCreatedOn = record.CreatedOn;
			projection.FinalizedOn = record.FinalizedOn;
			projection.StationGroupId = record.StationGroupId;
			projection.CallId = record.CallId;
			projection.CallNumber = details.CallNumber;
			projection.AuthorUserId = record.AuthorUserId;
			projection.OwnerUserId = record.OwnerUserId;
			projection.ReviewerUserId = record.ReviewerUserId;
			projection.ParticipantUserIds = string.Join(",", aggregate.Participants.Select(p => p.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct());
			projection.UnitIds = string.Join(",", aggregate.Units.Select(u => u.UnitId).Distinct());
			projection.GroupScopeIds = string.Join(",", (aggregate.GroupScope ?? new List<RmsRecordGroupScope>()).Select(s => s.DepartmentGroupId).Distinct());
			projection.DisplaySummary = record.DisplaySummary;
			// Safe fields only: never narrative, address detail, contact or restricted sections (plan section 5.10).
			projection.SearchText = string.Join(" ", new[] { record.RecordNumber, record.DraftReference, record.DisplaySummary, details.Course, details.CourseCode, details.CallNumber, details.CallName, details.Type, record.ExternalId }.Where(s => !string.IsNullOrWhiteSpace(s)));
			projection.IsLegacy = false;
			projection.ProjectionVersion = RmsRecordSearchProjection.CurrentProjectionVersion;
			projection.ProtectedCatalogVersion = await SafeCatalogVersionAsync(record.DepartmentId);
			projection.PolicyEpoch = await SafePolicyEpochAsync(record.DepartmentId);
			projection.ModifiedOn = DateTime.UtcNow;
			projection.RowVersion += 1;
			projection.DeletedOn = record.DeletedOn;

			if (existing == null)
				await _projections.InsertAsync(projection, cancellationToken, true);
			else
				await _projections.UpdateAsync(projection, cancellationToken, true);
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

		private async Task<DomainEventOutboxEntry> EnqueueLifecycleEventAsync(RmsOperationalRecord record, RmsRevision revision, WorkflowTriggerEventType trigger, RmsRecordState from, RmsRecordState to, string reasonCode, CancellationToken cancellationToken, object extra = null)
		{
			var payload = new Dictionary<string, object>
			{
				["record"] = new
				{
					id = record.RmsOperationalRecordId,
					record_number = record.RecordNumber,
					draft_reference = record.DraftReference,
					definition_key = record.DefinitionKey,
					definition_version = record.DefinitionVersion,
					type_key = record.RecordType.HasValue ? ((RmsOperationalRecordType)record.RecordType.Value).ToString() : null,
					state = to.ToString(),
					lifecycle_preset = ((RmsLifecyclePreset)record.LifecyclePreset).ToString(),
					department_id = record.DepartmentId,
					station_group_id = record.StationGroupId,
					call_id = record.CallId,
					external_id = record.ExternalId,
					author_user_id = record.AuthorUserId,
					owner_user_id = record.OwnerUserId,
					started_on = record.StartedOn,
					ended_on = record.EndedOn,
					created_on = record.CreatedOn,
					finalized_on = record.FinalizedOn,
					revision_id = revision?.RmsRevisionId ?? record.CurrentRevisionId,
					revision_number = revision?.RevisionNumber ?? record.RevisionCount,
					checksum = revision?.Checksum,
					summary = record.DisplaySummary
				},
				["record_change"] = new
				{
					previous_state = from.ToString(),
					current_state = to.ToString(),
					prior_revision_id = revision?.PriorRevisionId,
					current_revision_id = revision?.RmsRevisionId ?? record.CurrentRevisionId,
					reason_code = reasonCode
				}
			};
			if (extra != null)
				payload["extra"] = extra;

			// review.* (plan section 5.6): who reviewed, when it was due, how often it came back. Only the two
			// review-path triggers carry it; the fields are review bookkeeping, never record content.
			if (trigger == WorkflowTriggerEventType.RecordSubmittedForReview || trigger == WorkflowTriggerEventType.RecordReturnedForCorrection)
			{
				payload["review"] = new
				{
					reviewer_user_id = record.ReviewerUserId,
					submitted_for_review_on = record.SubmittedForReviewOn,
					review_due_on = record.ReviewDueOn,
					returned_on = record.ReturnedOn,
					return_count = record.ReturnCount,
					reason_code = record.ReturnReasonCode,
					reason_text = record.ReturnReasonText
				};
			}

			var entry = await _outbox.EnqueueAsync(record.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = trigger.ToString(),
				SchemaVersion = 1,
				AggregateType = DomainEventProducers.RecordsAggregate,
				AggregateId = record.RmsOperationalRecordId,
				AggregateVersion = revision?.RevisionNumber ?? record.RevisionCount,
				Trigger = trigger,
				Payload = payload,
				CorrelationId = record.RmsOperationalRecordId,
				OriginClient = (RmsOriginClient)record.OriginClient
			}, cancellationToken);

			return entry;
		}

		/// <summary>
		/// Legacy LogAdded compatibility (plan section 5.6): the exact pre-existing log.* contract, projected once at
		/// finalize for eligible Logs-parity records through the same outbox, caused by the RecordFinalized event.
		/// </summary>
		private Task<DomainEventOutboxEntry> EnqueueLogAddedCompatibilityAsync(RmsOperationalRecord record, RmsRevision revision, RmsOperationalRecordDetail details, string causationEventId, DateTime now, CancellationToken cancellationToken)
		{
			return _outbox.EnqueueAsync(record.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = WorkflowTriggerEventType.LogAdded.ToString(),
				SchemaVersion = 1,
				AggregateType = DomainEventProducers.RecordsAggregate,
				AggregateId = record.RmsOperationalRecordId,
				AggregateVersion = revision?.RevisionNumber,
				Trigger = WorkflowTriggerEventType.LogAdded,
				Payload = LogAddedCompatibility.Build(record, details, now),
				CorrelationId = record.RmsOperationalRecordId,
				CausationId = causationEventId,
				OriginClient = (RmsOriginClient)record.OriginClient
			}, cancellationToken);
		}

		private Task AuditAsync(int departmentId, string userId, string recordId, string revisionId, RmsAccessAuditAction action, string purpose, RmsOriginClient origin, CancellationToken cancellationToken, object detail = null)
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
				Successful = true,
				OccurredOn = DateTime.UtcNow,
				DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);
		}

		private async Task<List<RmsRecordParticipant>> BuildParticipantsAsync(int departmentId, string recordId, IEnumerable<RecordParticipantInput> inputs, DateTime now)
		{
			var result = new List<RmsRecordParticipant>();
			var ordinal = 0;
			foreach (var input in (inputs ?? Enumerable.Empty<RecordParticipantInput>()).Where(i => !string.IsNullOrWhiteSpace(i.UserId)))
			{
				var profile = await _profiles.GetProfileByUserIdAsync(input.UserId);
				var group = await _groups.GetGroupForUserAsync(input.UserId, departmentId);
				result.Add(new RmsRecordParticipant
				{
					RmsRecordParticipantId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					ProtectionId = Guid.NewGuid().ToString(),
					RecordId = recordId,
					UserId = input.UserId,
					DisplayNameSnapshot = profile == null ? null : $"{profile.FirstName} {profile.LastName}".Trim(),
					GroupIdSnapshot = group?.DepartmentGroupId,
					GroupNameSnapshot = group?.Name,
					UnitId = input.UnitId,
					Role = input.Role,
					SourceKind = (int)RmsSourceKind.None,
					Ordinal = ordinal++,
					CreatedOn = now,
					ModifiedOn = now,
					RowVersion = 1
				});
			}
			return result;
		}

		private async Task<List<RmsRecordUnitResponse>> BuildUnitsAsync(int departmentId, string recordId, IEnumerable<RecordUnitResponseInput> inputs, DateTime now)
		{
			var result = new List<RmsRecordUnitResponse>();
			var ordinal = 0;
			foreach (var input in (inputs ?? Enumerable.Empty<RecordUnitResponseInput>()).Where(i => i.UnitId > 0))
			{
				var unit = await _unitsService.GetUnitByIdAsync(input.UnitId);
				if (unit == null || unit.DepartmentId != departmentId)
					throw new ArgumentException($"Unit {input.UnitId} does not belong to this department.");

				result.Add(new RmsRecordUnitResponse
				{
					RmsRecordUnitResponseId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					ProtectionId = Guid.NewGuid().ToString(),
					RecordId = recordId,
					UnitId = unit.UnitId,
					UnitNameSnapshot = unit.Name,
					UnitTypeSnapshot = unit.Type,
					StationGroupIdSnapshot = unit.StationGroupId,
					Dispatched = input.Dispatched,
					Enroute = input.Enroute,
					OnScene = input.OnScene,
					Released = input.Released,
					InQuarters = input.InQuarters,
					TimesSourceKind = (int)RmsSourceKind.None,
					Ordinal = ordinal++,
					CreatedOn = now,
					ModifiedOn = now,
					RowVersion = 1
				});
			}
			return result;
		}

		private async Task ApplyCallSnapshotAsync(int departmentId, RmsOperationalRecordDetail details, int? callId)
		{
			if (!callId.HasValue)
			{
				details.CallNumber = null;
				details.CallName = null;
				details.CallType = null;
				details.CallPriority = null;
				details.CallLoggedOn = null;
				details.CallAddress = null;
				details.CallNature = null;
				return;
			}

			var call = await _calls.GetCallByIdAsync(callId.Value);
			if (call == null || call.DepartmentId != departmentId)
				throw new ArgumentException($"Call {callId} does not belong to this department.");

			details.CallNumber = call.Number;
			details.CallName = call.Name;
			details.CallType = call.Type;
			details.CallPriority = call.Priority;
			details.CallLoggedOn = call.LoggedOn;
			details.CallAddress = call.Address;
			details.CallNature = call.NatureOfCall;
		}

		private static void ApplyDetails(RmsOperationalRecordDetail target, RmsOperationalRecordDetail source)
		{
			if (source == null)
				return;

			target.Narrative = source.Narrative;
			target.InitialReport = source.InitialReport;
			target.Type = source.Type;
			target.Course = source.Course;
			target.CourseCode = source.CourseCode;
			target.Instructors = source.Instructors;
			target.Cause = source.Cause;
			target.InvestigatedByUserId = source.InvestigatedByUserId;
			target.ContactName = source.ContactName;
			target.ContactNumber = source.ContactNumber;
			target.OtherPersonnel = source.OtherPersonnel;
			target.Location = source.Location;
			target.OtherAgencies = source.OtherAgencies;
			target.OtherUnits = source.OtherUnits;
			target.BodyLocation = source.BodyLocation;
			target.PronouncedDeceasedBy = source.PronouncedDeceasedBy;
			target.CaseNumber = source.CaseNumber;
			target.Destination = source.Destination;
			target.Facilitator = source.Facilitator;
			target.UnitId = source.UnitId;
			target.ActivityOn = source.ActivityOn;
		}

		private static void ValidateDefinitionRequirements(RmsOperationalRecordType type, RmsOperationalRecordDetail details)
		{
			if (type == RmsOperationalRecordType.UnitActivity && (details?.UnitId).GetValueOrDefault() <= 0)
				throw new ArgumentException("A Unit Activity record requires a unit.");
		}

		/// <summary>Logs parity: a narrative is required on every legacy type; Unit Activity requires its timestamp.</summary>
		private static void ValidateForFinalization(RmsOperationalRecordType type, RecordAggregate draft)
		{
			if (draft.Details == null || string.IsNullOrWhiteSpace(draft.Details.Narrative))
				throw new ArgumentException("A narrative is required before a Record can be finalized.");
			if (type == RmsOperationalRecordType.UnitActivity && !draft.Details.ActivityOn.HasValue)
				throw new ArgumentException("A Unit Activity record requires an activity time before it can be finalized.");
		}

		private static string BuildDisplaySummary(RmsOperationalRecordType type, RmsOperationalRecord record, RmsOperationalRecordDetail details, List<RmsRecordUnitResponse> units)
		{
			string summary;
			switch (type)
			{
				case RmsOperationalRecordType.Training:
					summary = string.Join(" ", new[] { details.Course, string.IsNullOrWhiteSpace(details.CourseCode) ? null : "(" + details.CourseCode + ")" }.Where(s => !string.IsNullOrWhiteSpace(s)));
					break;
				case RmsOperationalRecordType.Run:
				case RmsOperationalRecordType.Callback:
					summary = string.Join(" ", new[] { details.CallNumber, details.CallName }.Where(s => !string.IsNullOrWhiteSpace(s)));
					break;
				case RmsOperationalRecordType.Meeting:
					summary = string.Join(" ", new[] { details.Type, details.Facilitator }.Where(s => !string.IsNullOrWhiteSpace(s)));
					break;
				case RmsOperationalRecordType.UnitActivity:
					summary = units.FirstOrDefault()?.UnitNameSnapshot;
					break;
				case RmsOperationalRecordType.Coroner:
					// Restricted section; the summary never carries case, location or person detail.
					summary = null;
					break;
				default:
					summary = details.Type;
					break;
			}

			if (string.IsNullOrWhiteSpace(summary))
				summary = type.ToString();

			return summary.Length > 400 ? summary.Substring(0, 400) : summary;
		}

		private static string ResolveClientRecordId(string clientRecordId)
		{
			if (string.IsNullOrWhiteSpace(clientRecordId))
				return Guid.NewGuid().ToString();
			if (!Guid.TryParse(clientRecordId, out var parsed))
				throw new ArgumentException("A client-supplied record id must be a GUID.", nameof(clientRecordId));
			return parsed.ToString();
		}

		private static string NewDraftReference()
		{
			const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
			var bytes = new byte[5];
			using (var rng = RandomNumberGenerator.Create())
				rng.GetBytes(bytes);
			var chars = new char[5];
			for (var i = 0; i < 5; i++)
				chars[i] = alphabet[bytes[i] % alphabet.Length];
			return "D-" + new string(chars);
		}

		private static RmsOperationalRecordDetail CloneDetails(RmsOperationalRecordDetail source)
		{
			return JsonConvert.DeserializeObject<RmsOperationalRecordDetail>(JsonConvert.SerializeObject(source));
		}

		private static RmsRecordParticipant CloneParticipant(RmsRecordParticipant source)
		{
			return JsonConvert.DeserializeObject<RmsRecordParticipant>(JsonConvert.SerializeObject(source));
		}

		private static RmsRecordUnitResponse CloneUnit(RmsRecordUnitResponse source)
		{
			return JsonConvert.DeserializeObject<RmsRecordUnitResponse>(JsonConvert.SerializeObject(source));
		}

		#endregion
	}
}
