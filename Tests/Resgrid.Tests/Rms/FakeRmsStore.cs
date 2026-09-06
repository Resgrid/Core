using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// In-memory stand-ins for the RMS repositories so service behavior (transitions, revisions, scope,
	/// projection, outbox) can be asserted without a database. Only the members the services call are
	/// wired; everything else returns Moq defaults.
	/// </summary>
	public sealed class FakeRmsStore
	{
		public List<RmsOperationalRecord> Records { get; } = new List<RmsOperationalRecord>();
		public List<RmsOperationalRecordDetail> Details { get; } = new List<RmsOperationalRecordDetail>();
		public List<RmsRecordParticipant> Participants { get; } = new List<RmsRecordParticipant>();
		public List<RmsRecordUnitResponse> Units { get; } = new List<RmsRecordUnitResponse>();
		public List<RmsRecordAttachment> Attachments { get; } = new List<RmsRecordAttachment>();
		public List<RmsRevision> Revisions { get; } = new List<RmsRevision>();
		public List<RmsRecordGroupScope> Scopes { get; } = new List<RmsRecordGroupScope>();
		public List<RmsRecordShare> Shares { get; } = new List<RmsRecordShare>();
		public List<RmsRecordSearchProjection> Projections { get; } = new List<RmsRecordSearchProjection>();
		public List<RmsAccessAudit> Audits { get; } = new List<RmsAccessAudit>();
		public List<DomainEventOutboxEntry> Outbox { get; } = new List<DomainEventOutboxEntry>();
		public List<RmsDepartmentCutover> Cutovers { get; } = new List<RmsDepartmentCutover>();
		public List<RmsDepartmentCutoverEvent> CutoverEvents { get; } = new List<RmsDepartmentCutoverEvent>();
		public List<RmsRecordDueState> DueStates { get; } = new List<RmsRecordDueState>();
		public List<RmsRecordLegalHold> LegalHolds { get; } = new List<RmsRecordLegalHold>();
		public List<RmsEvidenceArtifact> EvidenceArtifacts { get; } = new List<RmsEvidenceArtifact>();
		public List<RmsDisclosureRequest> DisclosureRequests { get; } = new List<RmsDisclosureRequest>();
		public List<RmsDisclosureProduction> DisclosureProductions { get; } = new List<RmsDisclosureProduction>();

		public Mock<IRmsOperationalRecordsRepository> RecordsRepo { get; } = new Mock<IRmsOperationalRecordsRepository>();
		public Mock<IRmsOperationalRecordDetailsRepository> DetailsRepo { get; } = new Mock<IRmsOperationalRecordDetailsRepository>();
		public Mock<IRmsRecordParticipantsRepository> ParticipantsRepo { get; } = new Mock<IRmsRecordParticipantsRepository>();
		public Mock<IRmsRecordUnitResponsesRepository> UnitsRepo { get; } = new Mock<IRmsRecordUnitResponsesRepository>();
		public Mock<IRmsRecordAttachmentsRepository> AttachmentsRepo { get; } = new Mock<IRmsRecordAttachmentsRepository>();
		public Mock<IRmsRevisionsRepository> RevisionsRepo { get; } = new Mock<IRmsRevisionsRepository>();
		public Mock<IRmsRecordGroupScopesRepository> ScopesRepo { get; } = new Mock<IRmsRecordGroupScopesRepository>();
		public Mock<IRmsRecordSharesRepository> SharesRepo { get; } = new Mock<IRmsRecordSharesRepository>();
		public Mock<IRmsRecordSearchProjectionsRepository> ProjectionsRepo { get; } = new Mock<IRmsRecordSearchProjectionsRepository>();
		public Mock<IRmsAccessAuditsRepository> AuditsRepo { get; } = new Mock<IRmsAccessAuditsRepository>();
		public Mock<IDomainEventOutboxRepository> OutboxRepo { get; } = new Mock<IDomainEventOutboxRepository>();
		public Mock<IRmsDepartmentCutoversRepository> CutoversRepo { get; } = new Mock<IRmsDepartmentCutoversRepository>();
		public Mock<IRmsDepartmentCutoverEventsRepository> CutoverEventsRepo { get; } = new Mock<IRmsDepartmentCutoverEventsRepository>();
		public Mock<IRmsRecordDueStatesRepository> DueStatesRepo { get; } = new Mock<IRmsRecordDueStatesRepository>();
		public Mock<IRmsRecordLegalHoldsRepository> LegalHoldsRepo { get; } = new Mock<IRmsRecordLegalHoldsRepository>();
		public Mock<IRmsEvidenceArtifactsRepository> EvidenceRepo { get; } = new Mock<IRmsEvidenceArtifactsRepository>();
		public Mock<IRmsDisclosureRequestsRepository> DisclosureRequestsRepo { get; } = new Mock<IRmsDisclosureRequestsRepository>();
		public Mock<IRmsDisclosureProductionsRepository> DisclosureProductionsRepo { get; } = new Mock<IRmsDisclosureProductionsRepository>();
		public Mock<IUnitOfWork> UnitOfWork { get; } = new Mock<IUnitOfWork>();

		public int Commits { get; private set; }
		public int Discards { get; private set; }

		private long _outboxSeed;

		public FakeRmsStore()
		{
			UnitOfWork.Setup(u => u.CommitChanges()).Callback(() => Commits++);
			UnitOfWork.Setup(u => u.DiscardChanges()).Callback(() => Discards++);

			// Records
			RecordsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsOperationalRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsOperationalRecord e, CancellationToken c, bool f) => { Records.Add(e); return e; });
			RecordsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsOperationalRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsOperationalRecord e, CancellationToken c, bool f) => { Records.RemoveAll(x => x.RmsOperationalRecordId == e.RmsOperationalRecordId); Records.Add(e); return e; });
			RecordsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Records.FirstOrDefault(x => x.DepartmentId == d && x.RmsOperationalRecordId == id));
			RecordsRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string k) => Records.FirstOrDefault(x => x.DepartmentId == d && x.IdempotencyKey == k));
			RecordsRepo.Setup(r => r.GetByCallAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int c) => Records.Where(x => x.DepartmentId == d && x.CallId == c).ToList());
			RecordsRepo.Setup(r => r.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long expected, CancellationToken c) =>
				{
					var row = Records.FirstOrDefault(x => x.DepartmentId == d && x.RmsOperationalRecordId == id);
					if (row == null || row.RowVersion != expected) return false;
					row.RowVersion += 1;
					return true;
				});
			RecordsRepo.Setup(r => r.GetMaxRecordNumberSequenceAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string prefix) => Records.Where(x => x.DepartmentId == d && x.RecordNumber != null && x.RecordNumber.StartsWith(prefix))
					.Select(x => int.TryParse(x.RecordNumber.Substring(prefix.Length), out var n) ? n : 0).DefaultIfEmpty(0).Max());
			RecordsRepo.Setup(r => r.CountCreatedSinceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int d, DateTime s) => Records.Count(x => x.DepartmentId == d && x.CreatedOn >= s));
			RecordsRepo.Setup(r => r.CountFinalizedSinceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int d, DateTime s) => Records.Count(x => x.DepartmentId == d && x.CreatedOn >= s && x.RevisionCount > 0));

			RecordsRepo.Setup(r => r.GetByOwnerAndStatesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int d, string owner, IEnumerable<int> states) => Records.Where(x => x.DepartmentId == d && x.OwnerUserId == owner && states.Contains(x.State)).ToList());

			RecordsRepo.Setup(r => r.GetByDepartmentAndStatesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, IEnumerable<int> states, int? year, int skip, int take) =>
				{
					var list = states?.ToList();
					return (IEnumerable<RmsOperationalRecord>)Records
						.Where(x => x.DepartmentId == d && x.DeletedOn == null
							&& (list == null || list.Count == 0 || list.Contains(x.State))
							&& (!year.HasValue || (x.StartedOn ?? x.CreatedOn).Year == year.Value))
						.OrderByDescending(x => x.CreatedOn).Skip(skip).Take(take).ToList();
				});
			RecordsRepo.Setup(r => r.CountByDepartmentAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int d, IEnumerable<int> states) =>
				{
					var list = states?.ToList();
					return Records.Count(x => x.DepartmentId == d && x.DeletedOn == null && (list == null || list.Count == 0 || list.Contains(x.State)));
				});
			RecordsRepo.Setup(r => r.CountVisibleAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<List<int>>(), It.IsAny<string>()))
				.ReturnsAsync((int d, IEnumerable<int> states, List<int> groups, string user) => Records.Count(r => r.DepartmentId == d && r.DeletedOn == null && r.PurgedOn == null
					&& states.Contains(r.State) && (groups == null || r.AuthorUserId == user || r.OwnerUserId == user || r.ReviewerUserId == user || r.ApproverUserId == user
					|| Participants.Any(p => p.DepartmentId == d && p.RecordId == r.RmsOperationalRecordId && p.RevisionId == null && p.UserId == user)
					|| Scopes.Any(s => s.DepartmentId == d && s.RecordId == r.RmsOperationalRecordId && groups.Contains(s.DepartmentGroupId)))));
			RecordsRepo.Setup(r => r.GetOpenAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Records.Where(x => x.DepartmentId == d && (x.State == 1 || x.State == 2 || x.State == 3 || x.State == 4)).ToList());
			RecordsRepo.Setup(r => r.GetFinalizedSinceAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int d, DateTime since) => Records.Where(x => x.DepartmentId == d && x.FinalizedOn.HasValue && x.FinalizedOn >= since).ToList());

			// Details
			DetailsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsOperationalRecordDetail>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsOperationalRecordDetail e, CancellationToken c, bool f) => { Details.Add(e); return e; });
			DetailsRepo.Setup(r => r.SaveOrUpdateAsync(It.IsAny<RmsOperationalRecordDetail>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsOperationalRecordDetail e, CancellationToken c, bool f) => { Details.RemoveAll(x => x.RmsOperationalRecordDetailId == e.RmsOperationalRecordDetailId); Details.Add(e); return e; });
			DetailsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsOperationalRecordDetail>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsOperationalRecordDetail e, CancellationToken c, bool f) => { Details.RemoveAll(x => x.RmsOperationalRecordDetailId == e.RmsOperationalRecordDetailId); Details.Add(e); return e; });
			DetailsRepo.Setup(r => r.GetDraftAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Details.FirstOrDefault(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == null));

			// Participants / units
			ParticipantsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordParticipant>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordParticipant e, CancellationToken c, bool f) => { Participants.Add(e); return e; });
			ParticipantsRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id, string rev) => Participants.Where(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == rev && x.DeletedOn == null).ToList());
			ParticipantsRepo.Setup(r => r.DeleteDraftForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken c) => Participants.RemoveAll(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == null));
			UnitsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordUnitResponse>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordUnitResponse e, CancellationToken c, bool f) => { Units.Add(e); return e; });
			UnitsRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id, string rev) => Units.Where(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == rev && x.DeletedOn == null).ToList());
			UnitsRepo.Setup(r => r.DeleteDraftForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken c) => Units.RemoveAll(x => x.DepartmentId == d && x.RecordId == id && x.RevisionId == null));

			// Attachments
			AttachmentsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordAttachment e, CancellationToken c, bool f) => { Attachments.Add(e); return e; });
			AttachmentsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordAttachment e, CancellationToken c, bool f) => e);
			AttachmentsRepo.Setup(r => r.GetMetadataForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Attachments.Where(x => x.DepartmentId == d && x.RecordId == id && x.DeletedOn == null).ToList());
			AttachmentsRepo.Setup(r => r.GetHistoricalByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Attachments.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordAttachmentId == id));
			AttachmentsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Attachments.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordAttachmentId == id));

			// Revisions
			RevisionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRevision>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRevision e, CancellationToken c, bool f) => { Revisions.Add(e); return e; });
			RevisionsRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Revisions.Where(x => x.DepartmentId == d && x.RecordId == id).OrderByDescending(x => x.RevisionNumber).ToList());
			RevisionsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Revisions.FirstOrDefault(x => x.DepartmentId == d && x.RmsRevisionId == id));

			// Scope / shares
			ScopesRepo.Setup(r => r.ReplaceForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IEnumerable<RmsRecordGroupScope>>(), It.IsAny<CancellationToken>()))
				.Returns((int d, string id, IEnumerable<RmsRecordGroupScope> s, CancellationToken c) => { Scopes.RemoveAll(x => x.DepartmentId == d && x.RecordId == id); Scopes.AddRange(s); return System.Threading.Tasks.Task.CompletedTask; });
			ScopesRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Scopes.Where(x => x.DepartmentId == d && x.RecordId == id).ToList());
			SharesRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Shares.Where(x => x.DepartmentId == d && x.RecordId == id).ToList());

			// Projection
			ProjectionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordSearchProjection>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordSearchProjection e, CancellationToken c, bool f) => { Projections.Add(e); return e; });
			ProjectionsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordSearchProjection>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordSearchProjection e, CancellationToken c, bool f) => { Projections.RemoveAll(x => x.RmsRecordSearchProjectionId == e.RmsRecordSearchProjectionId); Projections.Add(e); return e; });
			ProjectionsRepo.Setup(r => r.GetByRecordIdAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Projections.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordSearchProjectionId == id));

			// Audits
			AuditsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsAccessAudit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsAccessAudit e, CancellationToken c, bool f) => { Audits.Add(e); return e; });

			// Outbox
			OutboxRepo.Setup(r => r.InsertAsync(It.IsAny<DomainEventOutboxEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DomainEventOutboxEntry e, CancellationToken c, bool f) => { e.DomainEventOutboxId = ++_outboxSeed; Outbox.Add(e); return e; });
			OutboxRepo.Setup(r => r.GetNextSequenceAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string a) => Outbox.Where(x => x.DepartmentId == d && x.AggregateId == a).Select(x => x.Sequence).DefaultIfEmpty(0).Max() + 1);
			OutboxRepo.Setup(r => r.ClaimByIdAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((long id, string o, TimeSpan l, DateTime n, CancellationToken c) =>
				{
					var row = Outbox.FirstOrDefault(x => x.DomainEventOutboxId == id && x.State == (int)DomainEventOutboxState.Pending);
					if (row == null) return null;
					row.Attempts += 1;
					row.LeaseOwner = o;
					return row;
				});
			OutboxRepo.Setup(r => r.ClaimPendingBatchAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string o, TimeSpan l, int b, DateTime n, CancellationToken c) =>
				{
					var rows = Outbox.Where(x => x.State == (int)DomainEventOutboxState.Pending && (x.NextAttemptOn == null || x.NextAttemptOn <= n)).Take(b).ToList();
					foreach (var row in rows) { row.Attempts += 1; row.LeaseOwner = o; }
					return rows;
				});
			OutboxRepo.Setup(r => r.MarkDispatchedAsync(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((long id, DateTime n, CancellationToken c) =>
				{
					var row = Outbox.FirstOrDefault(x => x.DomainEventOutboxId == id);
					if (row == null) return false;
					row.State = (int)DomainEventOutboxState.Dispatched;
					row.DispatchedOn = n;
					return true;
				});
			OutboxRepo.Setup(r => r.MarkFailedAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((long id, string err, DateTime? next, bool terminal, CancellationToken c) =>
				{
					var row = Outbox.FirstOrDefault(x => x.DomainEventOutboxId == id);
					if (row == null) return false;
					row.State = terminal ? (int)DomainEventOutboxState.Failed : (int)DomainEventOutboxState.Pending;
					row.NextAttemptOn = next;
					row.LastError = err;
					return true;
				});
			OutboxRepo.Setup(r => r.CountByStateAsync(It.IsAny<int>())).ReturnsAsync((int s) => Outbox.Count(x => x.State == s));
			OutboxRepo.Setup(r => r.GetOldestPendingCreatedOnAsync()).ReturnsAsync(() => Outbox.Where(x => x.State == (int)DomainEventOutboxState.Pending).Select(x => (DateTime?)x.CreatedOn).DefaultIfEmpty(null).Min());

			// Cutover
			CutoversRepo.Setup(r => r.InsertAsync(It.IsAny<RmsDepartmentCutover>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsDepartmentCutover e, CancellationToken c, bool f) => { e.RmsDepartmentCutoverId = Cutovers.Count + 1; Cutovers.Add(e); return e; });
			CutoversRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsDepartmentCutover>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsDepartmentCutover e, CancellationToken c, bool f) => { Cutovers.RemoveAll(x => x.RmsDepartmentCutoverId == e.RmsDepartmentCutoverId); Cutovers.Add(e); return e; });
			CutoversRepo.Setup(r => r.GetByDepartmentIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Cutovers.FirstOrDefault(x => x.DepartmentId == d));
			CutoverEventsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsDepartmentCutoverEvent>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsDepartmentCutoverEvent e, CancellationToken c, bool f) => { CutoverEvents.Add(e); return e; });
			CutoversRepo.Setup(r => r.GetActiveAsync())
				.ReturnsAsync(() => Cutovers.Where(x => x.State == (int)RmsDepartmentCutoverState.Active).ToList());

			// RMS-3 retention candidates and the Pending-attachment rescan queue
			RecordsRepo.Setup(r => r.GetRetentionCandidatesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, DateTime cutoff, int take, string after) => Records
					.Where(x => x.DepartmentId == d && x.DeletedOn == null && x.PurgedOn == null && x.FinalizedOn.HasValue && x.FinalizedOn < cutoff
						&& (after == null || string.CompareOrdinal(x.RmsOperationalRecordId, after) > 0))
					.OrderBy(x => x.RmsOperationalRecordId, StringComparer.Ordinal).Take(take).ToList());
			AttachmentsRepo.Setup(r => r.GetPendingScanAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int take) => Attachments
					.Where(x => x.DepartmentId == d && x.DeletedOn == null && x.ScanState == (int)RmsAttachmentScanState.Pending)
					.OrderBy(x => x.UploadedOn).Take(take).ToList());
			AttachmentsRepo.Setup(r => r.ApplyScanResultAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<RmsAttachmentScanState>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long version, RmsAttachmentScanState state, DateTime now, CancellationToken c) =>
				{
					var row = Attachments.SingleOrDefault(a => a.DepartmentId == d && a.RmsRecordAttachmentId == id && a.RowVersion == version && a.DeletedOn == null && a.ScanState == (int)RmsAttachmentScanState.Pending);
					if (row == null) return false;
					row.ScanState = (int)state; row.ModifiedOn = now; row.RowVersion++;
					if (state == RmsAttachmentScanState.Rejected) { row.Data = null; row.StorageReference = null; row.DeletedOn = now; }
					return true;
				});

			// RMS-3 due states (M0170): one row per (record, obligation) is what the emit-once guarantee rests on.
			DueStatesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordDueState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordDueState e, CancellationToken c, bool f) => { DueStates.Add(e); return e; });
			DueStatesRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordDueState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordDueState e, CancellationToken c, bool f) => { DueStates.RemoveAll(x => x.RmsRecordDueStateId == e.RmsRecordDueStateId); DueStates.Add(e); return e; });
			DueStatesRepo.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<RmsRecordObligation>()))
				.ReturnsAsync((int d, string id, RmsRecordObligation o) => DueStates.FirstOrDefault(x => x.DepartmentId == d && x.RecordId == id && x.Obligation == (int)o));
			DueStatesRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => DueStates.Where(x => x.DepartmentId == d && x.RecordId == id).ToList());
			DueStatesRepo.Setup(r => r.GetOpenForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int take) => DueStates.Where(x => x.DepartmentId == d && x.LastEmittedState != (int)RmsDueState.Cleared).Take(take).ToList());
			DueStatesRepo.Setup(r => r.CountOverdueAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => DueStates.Count(x => x.DepartmentId == d && x.LastEmittedState == (int)RmsDueState.Overdue));
			DueStatesRepo.Setup(r => r.CountVisibleOverdueAsync(It.IsAny<int>(), It.IsAny<List<int>>(), It.IsAny<string>()))
				.ReturnsAsync((int d, List<int> groups, string user) => DueStates.Count(x => x.DepartmentId == d && x.LastEmittedState == (int)RmsDueState.Overdue
					&& Records.Any(r => r.DepartmentId == d && r.RmsOperationalRecordId == x.RecordId && r.DeletedOn == null && r.PurgedOn == null
						&& (groups == null || r.AuthorUserId == user || r.OwnerUserId == user || Scopes.Any(s => s.DepartmentId == d && s.RecordId == r.RmsOperationalRecordId && groups.Contains(s.DepartmentGroupId))))));
			DueStatesRepo.Setup(r => r.ClearForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, DateTime now, CancellationToken c) =>
				{
					var rows = DueStates.Where(x => x.DepartmentId == d && x.RecordId == id && x.LastEmittedState != (int)RmsDueState.Cleared).ToList();
					foreach (var row in rows) { row.LastEmittedState = (int)RmsDueState.Cleared; row.ModifiedOn = now; }
					return rows.Count;
				});

			// RMS-3 legal holds (M0170)
			LegalHoldsRepo.Setup(r => r.TryReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long version, string user, string reason, DateTime now, CancellationToken ct) => { var row = LegalHolds.SingleOrDefault(h => h.DepartmentId == d && h.RmsRecordLegalHoldId == id && h.RowVersion == version && h.ReleasedOn == null); if (row == null) return false; row.ReleasedByUserId = user; row.ReleasedOn = now; row.ReleaseNotes = reason; row.RowVersion++; return true; });
			LegalHoldsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRecordLegalHold>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordLegalHold e, CancellationToken c, bool f) => { LegalHolds.Add(e); return e; });
			LegalHoldsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordLegalHold>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordLegalHold e, CancellationToken c, bool f) => { LegalHolds.RemoveAll(x => x.RmsRecordLegalHoldId == e.RmsRecordLegalHoldId); LegalHolds.Add(e); return e; });
			LegalHoldsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => LegalHolds.FirstOrDefault(x => x.DepartmentId == d && x.RmsRecordLegalHoldId == id));
			LegalHoldsRepo.Setup(r => r.GetActiveForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => LegalHolds.Where(x => x.DepartmentId == d && x.ReleasedOn == null).ToList());
			LegalHoldsRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => LegalHolds.Where(x => x.DepartmentId == d && x.RecordId == id).ToList());
			LegalHoldsRepo.Setup(r => r.GetAllForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => LegalHolds.Where(x => x.DepartmentId == d).ToList());

			// RMS-3c evidence artifacts (M0169): insert and supersede only; there is no update-in-place path.
			EvidenceRepo.Setup(r => r.InsertAsync(It.IsAny<RmsEvidenceArtifact>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsEvidenceArtifact e, CancellationToken c, bool f) => { EvidenceArtifacts.Add(e); return e; });
			EvidenceRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsEvidenceArtifact>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsEvidenceArtifact e, CancellationToken c, bool f) => { EvidenceArtifacts.RemoveAll(x => x.RmsEvidenceArtifactId == e.RmsEvidenceArtifactId); EvidenceArtifacts.Add(e); return e; });
			EvidenceRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => EvidenceArtifacts.FirstOrDefault(x => x.DepartmentId == d && x.RmsEvidenceArtifactId == id));
			EvidenceRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, string rec, string rev, bool includeSuperseded) => (IEnumerable<RmsEvidenceArtifact>)EvidenceArtifacts
					.Where(x => x.DepartmentId == d && x.RecordId == rec && x.RevisionId == rev && x.DeletedOn == null && (includeSuperseded || x.SupersededOn == null))
					.OrderBy(x => x.Kind).ThenBy(x => x.CapturedOn).ToList());
			EvidenceRepo.Setup(r => r.GetCurrentDraftOfKindAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<RmsEvidenceKind>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string rec, RmsEvidenceKind kind, string sourceId) => EvidenceArtifacts.FirstOrDefault(x =>
					x.DepartmentId == d && x.RecordId == rec && x.RevisionId == null && x.Kind == (int)kind
					&& x.SupersededOn == null && x.DeletedOn == null && (sourceId == null || x.SourceEntityId == sourceId)));
			EvidenceRepo.Setup(r => r.BindDraftToRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string rec, string rev, DateTime now, CancellationToken c) =>
				{
					var rows = EvidenceArtifacts.Where(x => x.DepartmentId == d && x.RecordId == rec && x.RevisionId == null && x.DeletedOn == null).ToList();
					foreach (var row in rows) { row.RevisionId = rev; row.ModifiedOn = now; }
					return rows.Count;
				});
			EvidenceRepo.Setup(r => r.CountForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string rec) => EvidenceArtifacts.Count(x => x.DepartmentId == d && x.RecordId == rec && x.DeletedOn == null));

			// RMS-3d disclosures (M0171)
			DisclosureRequestsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsDisclosureRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsDisclosureRequest e, CancellationToken c, bool f) => { DisclosureRequests.Add(e); return e; });
			DisclosureRequestsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsDisclosureRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsDisclosureRequest e, CancellationToken c, bool f) => { DisclosureRequests.RemoveAll(x => x.RmsDisclosureRequestId == e.RmsDisclosureRequestId); DisclosureRequests.Add(e); return e; });
			DisclosureRequestsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => DisclosureRequests.FirstOrDefault(x => x.DepartmentId == d && x.RmsDisclosureRequestId == id));
			DisclosureRequestsRepo.Setup(r => r.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, string id, long version, CancellationToken ct) =>
			{ var row = DisclosureRequests.SingleOrDefault(r => r.DepartmentId == d && r.RmsDisclosureRequestId == id && r.RowVersion == version && r.ClosedOn == null && r.DeletedOn == null); if (row == null) return false; row.RowVersion++; return true; });
			DisclosureRequestsRepo.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, IEnumerable<int> states, int skip, int take) =>
				{
					var list = states?.ToList();
					return (IEnumerable<RmsDisclosureRequest>)DisclosureRequests
						.Where(x => x.DepartmentId == d && x.DeletedOn == null && (list == null || list.Count == 0 || list.Contains(x.State)))
						.OrderBy(x => x.StatutoryDueOn).Skip(skip).Take(take).ToList();
				});
			DisclosureRequestsRepo.Setup(r => r.CountByStateAsync(It.IsAny<int>(), It.IsAny<RmsDisclosureState>()))
				.ReturnsAsync((int d, RmsDisclosureState state) => DisclosureRequests.Count(x => x.DepartmentId == d && x.State == (int)state && x.DeletedOn == null));
			DisclosureRequestsRepo.Setup(r => r.CountOverdueAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int d, DateTime now) => DisclosureRequests.Count(x => x.DepartmentId == d && x.DeletedOn == null && x.ClosedOn == null && x.StatutoryDueOn.HasValue && x.StatutoryDueOn < now));
			DisclosureRequestsRepo.Setup(r => r.GetMaxRequestNumberSequenceAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string prefix) => DisclosureRequests
					.Where(x => x.DepartmentId == d && x.RequestNumber != null && x.RequestNumber.StartsWith(prefix, StringComparison.Ordinal))
					.Select(x => int.TryParse(x.RequestNumber.Substring(prefix.Length), out var n) ? n : 0)
					.DefaultIfEmpty(0).Max());

			DisclosureProductionsRepo.Setup(r => r.TryReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long version, string user, DateTime now, string method, string reference, CancellationToken ct) => { var row = DisclosureProductions.SingleOrDefault(x => x.DepartmentId == d && x.RmsDisclosureProductionId == id && x.RowVersion == version && x.ReleasedOn == null); if (row == null) return false; var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<RmsDisclosureProduction>(Newtonsoft.Json.JsonConvert.SerializeObject(row)); copy.ReleasedByUserId = user; copy.ReleasedOn = now; copy.DeliveryMethod = method; copy.DeliveryReference = reference; copy.ModifiedOn = now; copy.RowVersion++; DisclosureProductions.Remove(row); DisclosureProductions.Add(copy); return true; });
			DisclosureProductionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsDisclosureProduction>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsDisclosureProduction e, CancellationToken c, bool f) => { DisclosureProductions.Add(e); return e; });
			DisclosureProductionsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsDisclosureProduction>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsDisclosureProduction e, CancellationToken c, bool f) => { DisclosureProductions.RemoveAll(x => x.RmsDisclosureProductionId == e.RmsDisclosureProductionId); DisclosureProductions.Add(e); return e; });
			DisclosureProductionsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => DisclosureProductions.FirstOrDefault(x => x.DepartmentId == d && x.RmsDisclosureProductionId == id));
			DisclosureProductionsRepo.Setup(r => r.GetForRequestAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string requestId) => (IEnumerable<RmsDisclosureProduction>)DisclosureProductions
					.Where(x => x.DepartmentId == d && x.DisclosureRequestId == requestId).OrderBy(x => x.ProductionNumber).ToList());
			DisclosureProductionsRepo.Setup(r => r.GetMaxProductionNumberAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string requestId) => DisclosureProductions
					.Where(x => x.DepartmentId == d && x.DisclosureRequestId == requestId).Select(x => x.ProductionNumber).DefaultIfEmpty(0).Max());
		}

		public void SeedActiveCutover(int departmentId, DateTime? activatedOn = null)
		{
			Cutovers.Add(new RmsDepartmentCutover
			{
				RmsDepartmentCutoverId = Cutovers.Count + 1,
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				ActivatedOn = activatedOn ?? DateTime.UtcNow.AddDays(-1),
				ActivatedByUserId = "admin",
				State = (int)RmsDepartmentCutoverState.Active,
				CreatedOn = DateTime.UtcNow.AddDays(-1),
				ModifiedOn = DateTime.UtcNow.AddDays(-1),
				RowVersion = 1
			});
		}
	}
}
