using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>Permits, plan review cycles, conditions, expiration and the optional fee reference (RMS plan section 4.3, RMS-5).</summary>
	public class RecordsPermitsService : IRecordsPermitsService
	{
		public const string PermitAggregate = "RmsPermit";
		private const int SweepBatch = 500;

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsPermitTypesRepository _types;
		private readonly IRmsPermitsRepository _permits;
		private readonly IRmsPlanReviewsRepository _reviews;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly IRmsPreventionAttachmentsRepository _attachments;
		private readonly IRecordsProtectionService _protection;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsPermitsService(RecordsPreventionGate gate, IRmsPermitTypesRepository types, IRmsPermitsRepository permits, IRmsPlanReviewsRepository reviews, IRmsOccupanciesRepository occupancies,
			IRmsPreventionAttachmentsRepository attachments, IRecordsProtectionService protection, IDomainEventOutboxService outbox, IUnitOfWork unitOfWork)
		{
			_gate = gate; _types = types; _permits = permits; _reviews = reviews; _occupancies = occupancies; _attachments = attachments; _protection = protection; _outbox = outbox; _unitOfWork = unitOfWork;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Permits);
		private async Task RequireViewAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Permits); await _gate.RequireViewerAsync(departmentId, userId); }
		private async Task RequireAdminAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Permits); await _gate.RequireAdminAsync(departmentId, userId); }

		public async Task<List<RmsPermitType>> GetTypesAsync(int departmentId, string userId, bool includeInactive)
		{
			await RequireViewAsync(departmentId, userId);
			return (await _types.GetForDepartmentAsync(departmentId, includeInactive))?.ToList() ?? new List<RmsPermitType>();
		}

		public async Task<RmsPermitType> SaveTypeAsync(int departmentId, string userId, RmsPermitType input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var now = DateTime.UtcNow;
			var entity = string.IsNullOrWhiteSpace(input.RmsPermitTypeId) ? null : await _types.GetByIdForDepartmentAsync(departmentId, input.RmsPermitTypeId);
			var isNew = entity == null;
			if (isNew) entity = new RmsPermitType { RmsPermitTypeId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), CreatedOn = now, CreatedByUserId = userId, RowVersion = 1 };
			else entity.RowVersion++;
			entity.Name = RecordsPreventionGate.Require(input.Name, 200, "A permit type needs a name.");
			entity.Code = RecordsPreventionGate.Trim(input.Code, 32); entity.Description = RecordsPreventionGate.Trim(input.Description, 4000);
			entity.DefaultValidityDays = Math.Clamp(input.DefaultValidityDays <= 0 ? 365 : input.DefaultValidityDays, 1, 3650);
			entity.RequiresPlanReview = input.RequiresPlanReview; entity.FeeAmount = input.FeeAmount; entity.ConditionsTemplate = RecordsPreventionGate.Trim(input.ConditionsTemplate, 8000);
			entity.IsActive = input.IsActive; entity.ModifiedOn = now;
			if (isNew) await _types.InsertAsync(entity, cancellationToken, true); else await _types.UpdateAsync(entity, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, isNew ? "Permit type created" : "Permit type updated", entity.RmsPermitTypeId, new { entity.Name, entity.Code }, cancellationToken: cancellationToken);
			return entity;
		}

		public async Task<List<RmsPermit>> ListAsync(int departmentId, string userId, RmsPermitQuery query)
		{
			await RequireViewAsync(departmentId, userId);
			var rows = (await _permits.QueryAsync(departmentId, query ?? new RmsPermitQuery()))?.ToList() ?? new List<RmsPermit>();
			await _protection.RevealPermitsAsync(departmentId, rows);
			return rows;
		}

		public async Task<int> CountAsync(int departmentId, string userId, RmsPermitQuery query)
		{
			await RequireViewAsync(departmentId, userId);
			return await _permits.CountAsync(departmentId, query ?? new RmsPermitQuery());
		}

		public async Task<PermitAggregate> GetAsync(int departmentId, string userId, string permitId)
		{
			await RequireViewAsync(departmentId, userId);
			var permit = await LiveAsync(departmentId, permitId);
			if (permit == null) return null;
			var aggregate = new PermitAggregate
			{
				Permit = permit,
				Type = await _types.GetByIdForDepartmentAsync(departmentId, permit.RmsPermitTypeId),
				Occupancy = string.IsNullOrWhiteSpace(permit.RmsOccupancyId) ? null : await _occupancies.GetByIdForDepartmentAsync(departmentId, permit.RmsOccupancyId),
				PlanReviews = (await _reviews.GetForPermitAsync(departmentId, permitId))?.ToList() ?? new List<RmsPlanReview>(),
				Attachments = (await _attachments.GetMetadataForParentAsync(departmentId, RmsPreventionParentKind.Permit, permitId))?.ToList() ?? new List<RmsPreventionAttachment>()
			};
			aggregate.Protection.Merge(await _protection.RevealPermitsAsync(departmentId, new[] { permit }));
			aggregate.Protection.Merge(await _protection.RevealPlanReviewsAsync(departmentId, aggregate.PlanReviews));
			aggregate.Protection.Merge(await _protection.RevealPreventionAttachmentsAsync(departmentId, aggregate.Attachments, false));
			return aggregate;
		}

		public async Task<RmsPermit> ApplyAsync(int departmentId, string userId, RmsPermit input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var type = await _types.GetByIdForDepartmentAsync(departmentId, input.RmsPermitTypeId);
			if (type == null || type.DeletedOn != null || !type.IsActive) throw new ArgumentException("Choose an active permit type.");
			if (!string.IsNullOrWhiteSpace(input.RmsOccupancyId))
			{
				var occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, input.RmsOccupancyId);
				if (occupancy == null || occupancy.DeletedOn != null) throw new ArgumentException("The occupancy does not exist.");
			}
			var now = DateTime.UtcNow;
			var permit = new RmsPermit
			{
				RmsPermitId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsPermitTypeId = type.RmsPermitTypeId, RmsOccupancyId = RecordsPreventionGate.Trim(input.RmsOccupancyId, 36),
				PermitNumber = await _gate.NextNumberAsync(departmentId, RmsPreventionNumberKinds.Permit, now, cancellationToken),
				ApplicantContactId = RecordsPreventionGate.Trim(input.ApplicantContactId, 128), ApplicantName = RecordsPreventionGate.Trim(input.ApplicantName, 250), ApplicantPhone = RecordsPreventionGate.Trim(input.ApplicantPhone, 50), ApplicantEmail = RecordsPreventionGate.Trim(input.ApplicantEmail, 250),
				Description = RecordsPreventionGate.Trim(input.Description, 4000), State = type.RequiresPlanReview ? (int)RmsPermitState.UnderReview : (int)RmsPermitState.Applied, AppliedOn = input.AppliedOn == default ? now : input.AppliedOn,
				Conditions = RecordsPreventionGate.Trim(string.IsNullOrWhiteSpace(input.Conditions) ? type.ConditionsTemplate : input.Conditions, 8000), FeeAmount = input.FeeAmount ?? type.FeeAmount,
				CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, RowVersion = 1
			};
			var plaintext = PlaintextSnapshot<RmsPermit>.Take(permit, RmsProtectedFields.Permits);
			await _protection.ProtectPermitAsync(departmentId, permit, null, userId, cancellationToken);
			await _permits.InsertAsync(permit, cancellationToken, true);
			plaintext.Restore();
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Permit application recorded", permit.RmsPermitId, new { permit.PermitNumber, type = type.Name }, cancellationToken: cancellationToken);
			return permit;
		}

		public async Task<RmsPermit> UpdateAsync(int departmentId, string userId, RmsPermit input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var permit = await LiveAsync(departmentId, input.RmsPermitId) ?? throw new ArgumentException("The permit does not exist.");
			if (permit.State == (int)RmsPermitState.Closed) throw new InvalidOperationException("A closed permit cannot be edited.");
			if (input.RowVersion != 0 && input.RowVersion != permit.RowVersion) throw new InvalidOperationException("The permit changed since it was loaded. Reload it before saving.");
			var existing = new RmsPermit { ApplicantName = permit.ApplicantName, ApplicantPhone = permit.ApplicantPhone, ApplicantEmail = permit.ApplicantEmail, ReviewNotes = permit.ReviewNotes };
			permit.ApplicantContactId = RecordsPreventionGate.Trim(input.ApplicantContactId, 128); permit.ApplicantName = RecordsPreventionGate.Trim(input.ApplicantName, 250); permit.ApplicantPhone = RecordsPreventionGate.Trim(input.ApplicantPhone, 50); permit.ApplicantEmail = RecordsPreventionGate.Trim(input.ApplicantEmail, 250);
			permit.Description = RecordsPreventionGate.Trim(input.Description, 4000); permit.Conditions = RecordsPreventionGate.Trim(input.Conditions, 8000); permit.ReviewNotes = RecordsPreventionGate.Trim(input.ReviewNotes, 8000);
			permit.RmsOccupancyId = RecordsPreventionGate.Trim(input.RmsOccupancyId, 36); permit.FeeAmount = input.FeeAmount;
			if (input.ExpiresOn.HasValue) permit.ExpiresOn = input.ExpiresOn; if (input.EffectiveOn.HasValue) permit.EffectiveOn = input.EffectiveOn;
			permit.ModifiedOn = DateTime.UtcNow; permit.RowVersion++;
			var plaintext = PlaintextSnapshot<RmsPermit>.Take(permit, RmsProtectedFields.Permits);
			await _protection.ProtectPermitAsync(departmentId, permit, existing, userId, cancellationToken);
			await _permits.UpdateAsync(permit, cancellationToken, true);
			plaintext.Restore();
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Permit updated", permit.RmsPermitId, new { permit.PermitNumber }, cancellationToken: cancellationToken);
			return permit;
		}

		public async Task<RmsPermit> TransitionAsync(int departmentId, string userId, string permitId, RmsPermitState target, string reason, DateTime? effectiveOn, DateTime? expiresOn, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var permit = await LiveAsync(departmentId, permitId) ?? throw new ArgumentException("The permit does not exist.");
			var current = (RmsPermitState)permit.State;
			var allowed = (current, target) switch
			{
				(RmsPermitState.Applied, RmsPermitState.UnderReview) => true,
				(RmsPermitState.Applied, RmsPermitState.Approved) => true,
				(RmsPermitState.Applied, RmsPermitState.Denied) => true,
				(RmsPermitState.UnderReview, RmsPermitState.Approved) => true,
				(RmsPermitState.UnderReview, RmsPermitState.Denied) => true,
				(RmsPermitState.Approved, RmsPermitState.Issued) => true,
				(RmsPermitState.Approved, RmsPermitState.Denied) => true,
				(RmsPermitState.Issued, RmsPermitState.Revoked) => true,
				(RmsPermitState.Issued, RmsPermitState.Expired) => true,
				(RmsPermitState.Issued, RmsPermitState.Closed) => true,
				(RmsPermitState.Expired, RmsPermitState.Closed) => true,
				(RmsPermitState.Revoked, RmsPermitState.Closed) => true,
				(RmsPermitState.Denied, RmsPermitState.Closed) => true,
				_ => false
			};
			if (!allowed) throw new InvalidOperationException($"A permit cannot move from {current} to {target}.");
			if ((target == RmsPermitState.Denied || target == RmsPermitState.Revoked) && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException($"{target} needs a reason.");
			var now = DateTime.UtcNow;
			var type = await _types.GetByIdForDepartmentAsync(departmentId, permit.RmsPermitTypeId);
			switch (target)
			{
				case RmsPermitState.Approved: permit.ReviewedOn = now; permit.ReviewedByUserId = userId; break;
				case RmsPermitState.Denied: permit.ReviewedOn = now; permit.ReviewedByUserId = userId; permit.DecisionReason = RecordsPreventionGate.Trim(reason, 1000); break;
				case RmsPermitState.Issued:
					permit.IssuedOn = now; permit.IssuedByUserId = userId; permit.EffectiveOn = effectiveOn ?? permit.EffectiveOn ?? now;
					permit.ExpiresOn = expiresOn ?? permit.ExpiresOn ?? permit.EffectiveOn.Value.AddDays(type?.DefaultValidityDays ?? 365);
					if (permit.ExpiresOn <= permit.EffectiveOn) throw new ArgumentException("The expiry must fall after the effective date.");
					break;
				case RmsPermitState.Revoked: permit.DecisionReason = RecordsPreventionGate.Trim(reason, 1000); break;
			}
			permit.State = (int)target; permit.ModifiedOn = now; permit.RowVersion++;
			await _permits.UpdateAsync(permit, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, $"Permit {target}", permit.RmsPermitId, new { permit.PermitNumber, from = current.ToString(), to = target.ToString(), permit.ExpiresOn }, cancellationToken: cancellationToken);
			return permit;
		}

		public async Task<RmsPlanReview> RecordPlanReviewAsync(int departmentId, string userId, string permitId, RmsPlanReviewOutcome outcome, string comments, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var permit = await LiveAsync(departmentId, permitId) ?? throw new ArgumentException("The permit does not exist.");
			if (permit.State != (int)RmsPermitState.Applied && permit.State != (int)RmsPermitState.UnderReview) throw new InvalidOperationException("Plan review applies to a permit under review.");
			var now = DateTime.UtcNow;
			var cycle = ((await _reviews.GetForPermitAsync(departmentId, permitId))?.Count() ?? 0) + 1;
			var review = new RmsPlanReview
			{
				RmsPlanReviewId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsPermitId = permit.RmsPermitId, CycleNumber = cycle, SubmittedOn = now, ReviewerUserId = userId,
				ReviewedOn = outcome == RmsPlanReviewOutcome.Pending ? null : now, Outcome = (int)outcome, Comments = RecordsPreventionGate.Trim(comments, 8000), CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			var plaintext = PlaintextSnapshot<RmsPlanReview>.Take(review, RmsProtectedFields.PlanReviews);
			await _protection.ProtectPlanReviewAsync(departmentId, review, null, userId, cancellationToken);
			await _reviews.InsertAsync(review, cancellationToken, true);
			plaintext.Restore();
			var target = outcome switch { RmsPlanReviewOutcome.Approved => RmsPermitState.Approved, RmsPlanReviewOutcome.Rejected => RmsPermitState.Denied, _ => RmsPermitState.UnderReview };
			if ((int)target != permit.State)
			{
				permit.State = (int)target; permit.ReviewedOn = outcome == RmsPlanReviewOutcome.Pending ? permit.ReviewedOn : now; permit.ReviewedByUserId = userId;
				if (outcome == RmsPlanReviewOutcome.Rejected) permit.DecisionReason = "Plan review rejected";
				permit.ModifiedOn = now; permit.RowVersion++;
				await _permits.UpdateAsync(permit, cancellationToken, true);
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Plan review recorded", permit.RmsPermitId, new { cycle, outcome = outcome.ToString() }, cancellationToken: cancellationToken);
			return review;
		}

		public async Task<RmsPermit> RecordFeePaidAsync(int departmentId, string userId, string permitId, decimal amount, string invoiceReference, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var permit = await LiveAsync(departmentId, permitId) ?? throw new ArgumentException("The permit does not exist.");
			if (amount < 0) throw new ArgumentException("The fee cannot be negative.");
			permit.FeeAmount = amount; permit.FeePaidOn = DateTime.UtcNow; permit.InvoiceReference = RecordsPreventionGate.Trim(invoiceReference, 100); permit.ModifiedOn = permit.FeePaidOn.Value; permit.RowVersion++;
			await _permits.UpdateAsync(permit, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Permit fee recorded", permit.RmsPermitId, new { amount, permit.InvoiceReference }, cancellationToken: cancellationToken);
			return permit;
		}

		public async Task<(int Expired, int Notified)> SweepExpiryAsync(int departmentId, DateTime utcNow, int noticeDays, CancellationToken cancellationToken = default)
		{
			if (!await IsModuleEnabledAsync(departmentId)) return (0, 0);
			var expired = 0; var notified = 0;
			var horizon = utcNow.AddDays(Math.Clamp(noticeDays, 1, 365));
			foreach (var permit in (await _permits.GetExpiringAsync(departmentId, utcNow, horizon, SweepBatch)) ?? Enumerable.Empty<RmsPermit>())
			{
				if (permit.ExpiresOn <= utcNow)
				{
					permit.State = (int)RmsPermitState.Expired; permit.DecisionReason = "Expired"; permit.ModifiedOn = utcNow; permit.RowVersion++;
					await _permits.UpdateAsync(permit, cancellationToken, true);
					expired++;
					continue;
				}
				if (permit.ExpiringEmittedOn.HasValue) continue;
				long outboxId;
				_unitOfWork.CreateOrGetConnection();
				try
				{
					permit.ExpiringEmittedOn = utcNow; permit.ModifiedOn = utcNow; permit.RowVersion++;
					await _permits.UpdateAsync(permit, cancellationToken, true);
					outboxId = await EnqueueExpiringAsync(permit, utcNow, cancellationToken);
					_unitOfWork.CommitChanges();
				}
				catch { _unitOfWork.DiscardChanges(); throw; }
				await _outbox.DispatchAfterCommitAsync(new[] { outboxId }, cancellationToken);
				notified++;
			}
			return (expired, notified);
		}

		/// <summary>permit.* (trigger 163): identity, type, occupancy, dates — never the applicant identity or review notes.</summary>
		private async Task<long> EnqueueExpiringAsync(RmsPermit permit, DateTime utcNow, CancellationToken cancellationToken)
		{
			var type = await _types.GetByIdForDepartmentAsync(permit.DepartmentId, permit.RmsPermitTypeId);
			var occupancy = string.IsNullOrWhiteSpace(permit.RmsOccupancyId) ? null : await _occupancies.GetByIdForDepartmentAsync(permit.DepartmentId, permit.RmsOccupancyId);
			var entry = await _outbox.EnqueueAsync(permit.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = WorkflowTriggerEventType.RecordPermitExpiring.ToString(), SchemaVersion = 1, AggregateType = PermitAggregate, AggregateId = permit.RmsPermitId, AggregateVersion = (int)permit.RowVersion,
				Trigger = WorkflowTriggerEventType.RecordPermitExpiring,
				Payload = new Dictionary<string, object>
				{
					["record"] = RecordsInspectionsService.PreventionRecordBlock(permit.DepartmentId),
					["permit"] = new
					{
						id = permit.RmsPermitId, number = permit.PermitNumber, type_id = permit.RmsPermitTypeId, type_name = type?.Name, type_code = type?.Code,
						occupancy_id = permit.RmsOccupancyId, occupancy_number = occupancy?.OccupancyNumber, occupancy_name = occupancy?.Name, state = ((RmsPermitState)permit.State).ToString(),
						issued_on = permit.IssuedOn, effective_on = permit.EffectiveOn, expires_on = permit.ExpiresOn, days_until_expiry = permit.ExpiresOn.HasValue ? (int)Math.Ceiling((permit.ExpiresOn.Value - utcNow).TotalDays) : 0,
						fee_paid = permit.FeePaidOn.HasValue
					},
					["protection"] = IncidentReportsService.ProtectionBlock(await _protection.GetCatalogVersionAsync(permit.DepartmentId))
				},
				CorrelationId = permit.RmsPermitId, OriginClient = RmsOriginClient.System
			}, cancellationToken);
			return entry.DomainEventOutboxId;
		}

		private async Task<RmsPermit> LiveAsync(int departmentId, string permitId)
		{
			if (string.IsNullOrWhiteSpace(permitId)) return null;
			var permit = await _permits.GetByIdForDepartmentAsync(departmentId, permitId);
			return permit == null || permit.DeletedOn != null ? null : permit;
		}
	}
}
