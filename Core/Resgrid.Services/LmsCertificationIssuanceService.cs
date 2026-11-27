using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 9A: the one place the training engine issues or renews a certification, and takes it back when a completion behind it is voided.
	/// <list type="bullet">
	/// <item>Issue or renew: the expiry comes from the type's validity rule on the department's local date of the completion
	/// (<see cref="CertificationValidity"/>); a suspended or revoked record is never touched; a record already valid at least as long is left
	/// alone; an expired record is renewed in place (reactivated); otherwise a new record. Each change is a ledger row with the record's
	/// previous state and the learning records that satisfied it, an audit entry, a member notice (push + email, EventTypes 77) and a workflow
	/// event (275/276).</item>
	/// <item>Void: every live ledger row of the member whose basis holds a voided record is re-evaluated against what still stands. Another
	/// valid completion keeps it (the basis follows; the expiry moves to what that completion gives). Otherwise the record returns to its state
	/// before (a record the engine created is withdrawn), unless a later issuance stands on it (the row is unwound: the later one inherits
	/// the earlier state) or the record was changed by hand since (left for an officer, audited). Audited, the member told, trigger 277.</item>
	/// </list>
	/// </summary>
	public class LmsCertificationIssuanceService : ILmsCertificationIssuanceService
	{
		public const string SystemUserId = "system";
		public const string EnrollmentRefPrefix = "lms-enrollment:";

		private readonly ILmsCertificationIssuancesRepository _issuances;
		private readonly ICertificationService _certifications;
		private readonly IDepartmentsService _departments;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsCertificationRequirementSetsRepository _sets;
		private readonly ILmsCertificationRequirementItemsRepository _items;
		private readonly ILmsProgramNotifier _notifier;
		private readonly IEventAggregator _events;
		private readonly LmsCertificationFactReader _reader;

		public LmsCertificationIssuanceService(ILmsCertificationIssuancesRepository issuances, ICertificationService certifications, IDepartmentsService departments,
			ILmsRequirementFactsRepository facts, ILmsPathStepsRepository pathSteps, ILmsEnrollmentsRepository enrollments, ILmsCertificationRequirementSetsRepository sets,
			ILmsCertificationRequirementItemsRepository items, ILmsProgramNotifier notifier, IEventAggregator events)
		{
			_issuances = issuances;
			_certifications = certifications;
			_departments = departments;
			_enrollments = enrollments;
			_sets = sets;
			_items = items;
			_notifier = notifier;
			_events = events;
			_reader = new LmsCertificationFactReader(facts, pathSteps);
		}

		#region Issue or renew

		public async Task<LmsCertificationIssueResult> IssueOrRenewAsync(LmsCertificationIssueRequest request, CancellationToken cancellationToken = default)
		{
			var result = new LmsCertificationIssueResult();
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.UserId) || LmsExternalLearnerIds.IsExternal(request.UserId))
			{
				result.Skipped = "type_invalid";
				return result;
			}

			var type = await _certifications.GetCertificationTypeByIdAsync(request.CertificationTypeId);
			if (type == null || type.DepartmentId != request.DepartmentId || type.IsDeleted || !type.IsActive || type.AppliesTo != (int)CertificationAppliesTo.Person)
			{
				result.Skipped = "type_invalid";
				return result;
			}

			var department = await _departments.GetDepartmentByIdAsync(request.DepartmentId, false);
			var localCompleted = TrainingDueDates.LocalDate(request.CompletedOnUtc == default ? DateTime.UtcNow : request.CompletedOnUtc, department);
			var localToday = TrainingDueDates.LocalDate(DateTime.UtcNow, department);
			var records = await _certifications.GetCertificationsForDepartmentAsync(request.DepartmentId, new[] { request.UserId }) ?? new List<PersonnelCertification>();
			var existing = LmsCertificationIssuanceRules.CurrentRecord(records, type.DepartmentCertificationTypeId, request.UserId);
			result.CertificationId = existing?.PersonnelCertificationId;

			// An officer's suspension or revocation is never overridden by a completion.
			if (LmsCertificationIssuanceRules.IsBlocked(existing))
			{
				result.Skipped = "blocked";
				return result;
			}

			var renewable = existing != null && LmsCertificationRequirementEvaluator.LiveOrExpired(existing) && CertificationValidity.IsRenewable(type, existing, localCompleted)
				? existing : null;
			var expires = CertificationValidity.ExpiryFor(type, localCompleted, renewable);
			if (expires.HasValue && expires.Value.Date < localToday)
			{
				result.Skipped = "already_expired";
				return result;
			}

			var holderId = existing?.UserId ?? request.UserId.Trim();
			LmsCertificationIssuance issuance;
			if (existing != null)
			{
				var live = LmsCertificationIssuanceRules.IsBlocked(existing) == false && existing.Status != (int)PersonnelCertificationStatuses.Expired;
				// Already valid at least as long (or for good): nothing to renew.
				if (live && (!existing.ExpiresOn.HasValue || (expires.HasValue && existing.ExpiresOn.Value.Date >= expires.Value.Date)))
				{
					result.Skipped = "already_current";
					return result;
				}
				// A type without an expiry the engine can compute (rolling with no months) never renews by itself.
				if (!expires.HasValue && !type.NeverExpires)
				{
					result.Skipped = "already_current";
					return result;
				}
				if (request.RenewOnly && renewable == null)
				{
					result.Skipped = "not_renewable";
					return result;
				}

				var before = (Status: existing.Status, ExpiresOn: existing.ExpiresOn, RecievedOn: existing.RecievedOn);
				// Renewed as the holder: a type that requires verification goes back to PendingVerification (CertificationService rule).
				var renewed = await _certifications.RenewCertificationAsync(existing.PersonnelCertificationId, request.DepartmentId, expires, null, holderId, cancellationToken);
				if (renewed == null)
				{
					result.Skipped = "not_renewable";
					return result;
				}

				issuance = NewIssuance(request, type, renewed, LmsCertificationIssueActions.Renewed);
				issuance.PreviousStatus = before.Status;
				issuance.PreviousExpiresOn = before.ExpiresOn;
				issuance.PreviousRecievedOn = before.RecievedOn;
				result.Action = LmsCertificationIssueActions.Renewed;
				result.CertificationId = renewed.PersonnelCertificationId;
				result.ExpiresOn = renewed.ExpiresOn;
			}
			else
			{
				if (request.RenewOnly)
				{
					result.Skipped = "not_renewable";
					return result;
				}

				var created = await _certifications.SaveCertificationAsync(new PersonnelCertification
				{
					DepartmentId = request.DepartmentId,
					UserId = holderId,
					// Only the type's own (plaintext) names: protected fields (the number, the document) are never written by the engine.
					Name = type.Type,
					Type = type.Type,
					DepartmentCertificationTypeId = type.DepartmentCertificationTypeId,
					IssuedBy = department?.Name,
					RecievedOn = localCompleted,
					ExpiresOn = expires,
					Status = type.RequiresVerification ? (int)PersonnelCertificationStatuses.PendingVerification : (int)PersonnelCertificationStatuses.Active,
					StatusChangedOn = DateTime.UtcNow,
					StatusChangedByUserId = holderId
				}, cancellationToken);
				if (created == null || created.PersonnelCertificationId <= 0)
				{
					result.Skipped = "not_renewable";
					return result;
				}

				issuance = NewIssuance(request, type, created, LmsCertificationIssueActions.Issued);
				result.Action = LmsCertificationIssueActions.Issued;
				result.CertificationId = created.PersonnelCertificationId;
				result.ExpiresOn = created.ExpiresOn;
			}

			result.Issuance = await RecordAsync(issuance, cancellationToken);
			Audit(request.DepartmentId, request.ActorUserId ?? SystemUserId, AuditLogTypes.CertificationIssuedByTraining, null, JsonConvert.SerializeObject(new
			{
				result.Issuance.LmsCertificationIssuanceId,
				result.Issuance.UserId,
				result.Issuance.PersonnelCertificationId,
				result.Issuance.DepartmentCertificationTypeId,
				Source = ((LmsCertificationIssueSources)result.Issuance.Source).ToString(),
				result.Issuance.SourceRefId,
				Action = ((LmsCertificationIssueActions)result.Issuance.Action).ToString(),
				result.Issuance.PreviousExpiresOn,
				result.Issuance.NewExpiresOn,
				result.Issuance.BasisRecordIds
			}));

			if (request.NotifyMember)
				await NotifyAsync(request.DepartmentId, holderId, type, result.CertificationId ?? 0,
					result.Action == LmsCertificationIssueActions.Issued ? (expires.HasValue ? "CertReq.Notify.Issued" : "CertReq.Notify.IssuedNoExpiry") : "CertReq.Notify.Renewed",
					expires, null, cancellationToken);

			Raise(new CertificationTrainingWorkflowEvent
			{
				DepartmentId = request.DepartmentId,
				Change = result.Action == LmsCertificationIssueActions.Issued ? CertificationTrainingWorkflowChanges.Issued : CertificationTrainingWorkflowChanges.Renewed,
				CertificationId = result.CertificationId ?? 0,
				CertificationTypeId = type.DepartmentCertificationTypeId,
				CertificationTypeName = type.Type,
				CertificationTypeCode = type.Code,
				UserId = holderId,
				Source = request.Source.ToString(),
				Outcome = result.Action.ToString(),
				ExpiresOn = result.ExpiresOn,
				PreviousExpiresOn = result.Issuance.PreviousExpiresOn,
				IssuanceId = result.Issuance.LmsCertificationIssuanceId,
				ActorUserId = request.ActorUserId,
				OccurredOn = DateTime.UtcNow
			});
			return result;
		}

		private static LmsCertificationIssuance NewIssuance(LmsCertificationIssueRequest request, DepartmentCertificationType type, PersonnelCertification record,
			LmsCertificationIssueActions action) => new LmsCertificationIssuance
			{
				DepartmentId = request.DepartmentId,
				UserId = record.UserId,
				PersonnelCertificationId = record.PersonnelCertificationId,
				DepartmentCertificationTypeId = type.DepartmentCertificationTypeId,
				Source = (int)request.Source,
				SourceRefId = Trim(request.SourceRefId, 128),
				Action = (int)action,
				NewStatus = record.Status,
				NewExpiresOn = record.ExpiresOn?.Date,
				BasisRecordIds = LmsCertificationIssuanceRules.FormatIds(request.BasisRecordIds),
				CompletedOn = request.CompletedOnUtc == default ? DateTime.UtcNow : request.CompletedOnUtc,
				IssuedOn = DateTime.UtcNow,
				IssuedByUserId = Trim(request.ActorUserId ?? SystemUserId, 128),
				State = (int)LmsCertificationIssuanceStates.Live
			};

		public async Task<LmsCertificationIssuance> RecordAsync(LmsCertificationIssuance issuance, CancellationToken cancellationToken = default)
		{
			if (issuance == null)
				return null;
			issuance.IssuedOn = issuance.IssuedOn == default ? DateTime.UtcNow : issuance.IssuedOn;
			issuance.CompletedOn = issuance.CompletedOn == default ? issuance.IssuedOn : issuance.CompletedOn;
			return await _issuances.InsertAsync(issuance, cancellationToken, true) ?? issuance;
		}

		public async Task<List<LmsCertificationIssuance>> GetForCertificationAsync(int departmentId, int personnelCertificationId) =>
			departmentId <= 0 ? new List<LmsCertificationIssuance>() : await _issuances.GetForCertificationAsync(departmentId, personnelCertificationId) ?? new List<LmsCertificationIssuance>();

		#endregion Issue or renew

		#region Void

		public async Task<LmsCertificationVoidResult> OnRecordsVoidedAsync(int departmentId, string userId, IReadOnlyCollection<int> voidedRecordIds, string actorUserId,
			CancellationToken cancellationToken = default)
		{
			var result = new LmsCertificationVoidResult();
			var voided = new HashSet<int>((voidedRecordIds ?? Array.Empty<int>()).Where(id => id > 0));
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || voided.Count == 0)
				return result;

			// A cycle's renewal hours can rest on any record of the member (a manual credit's record is not on its credit row): those rows
			// are cheap to re-check on any void. Every other row only when its basis names a voided record. Newest first, so an older row
			// that a later one stood on is judged after the later one.
			var affected = (await _issuances.GetLiveForUserAsync(departmentId, userId) ?? new List<LmsCertificationIssuance>())
				.Where(i => i.Source == (int)LmsCertificationIssueSources.CycleAutoRenew || i.Basis.Any(voided.Contains))
				.OrderByDescending(i => i.IssuedOn).ThenByDescending(i => i.LmsCertificationIssuanceId).ToList();
			foreach (var issuance in affected)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					await ReevaluateAsync(issuance, voided, actorUserId, result, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Certification issuance {issuance.LmsCertificationIssuanceId} could not be re-evaluated after a void.");
				}
			}

			return result;
		}

		private async Task ReevaluateAsync(LmsCertificationIssuance issuance, ISet<int> voided, string actorUserId, LmsCertificationVoidResult result, CancellationToken cancellationToken)
		{
			var departmentId = issuance.DepartmentId;
			var type = await _certifications.GetCertificationTypeByIdAsync(issuance.DepartmentCertificationTypeId);
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var localToday = TrainingDueDates.LocalDate(DateTime.UtcNow, department);
			var (satisfied, completedOnUtc, recordIds) = await CurrentBasisAsync(issuance, type, department, voided);

			if (satisfied && type != null)
			{
				DateTime? expiry;
				if (issuance.Source == (int)LmsCertificationIssueSources.CycleAutoRenew)
				{
					expiry = issuance.NewExpiresOn;
				}
				else
				{
					var previous = Previous(issuance);
					var localCompleted = TrainingDueDates.LocalDate(completedOnUtc ?? issuance.CompletedOn, department);
					var renewing = previous != null && CertificationValidity.IsRenewable(type, previous, localCompleted) ? previous : null;
					expiry = CertificationValidity.ExpiryFor(type, localCompleted, renewing);
				}

				var stillValid = !expiry.HasValue || expiry.Value.Date >= localToday;
				var laterThanBefore = !issuance.PreviousExpiresOn.HasValue || !expiry.HasValue || expiry.Value.Date > issuance.PreviousExpiresOn.Value.Date ||
				                      issuance.PreviousStatus == (int)PersonnelCertificationStatuses.Expired;
				if (stillValid && laterThanBefore)
				{
					if (SameDate(expiry, issuance.NewExpiresOn))
					{
						// Another valid completion still covers it: nothing changes for the member; the basis follows.
						issuance.BasisRecordIds = LmsCertificationIssuanceRules.FormatIds(recordIds);
						await _issuances.UpdateStateAsync(issuance, cancellationToken);
						result.Kept++;
						return;
					}

					await AdjustAsync(issuance, type, expiry, recordIds, actorUserId, cancellationToken);
					result.Kept++;
					return;
				}
			}

			await RollBackAsync(issuance, type, actorUserId, result, cancellationToken);
		}

		/// <summary>What still satisfies the issuance's source without the voided records: whether it holds, when it completed, and its records.</summary>
		private async Task<(bool Satisfied, DateTime? CompletedOnUtc, List<int> RecordIds)> CurrentBasisAsync(LmsCertificationIssuance issuance, DepartmentCertificationType type,
			Department department, ISet<int> voided)
		{
			var departmentId = issuance.DepartmentId;
			switch ((LmsCertificationIssueSources)issuance.Source)
			{
				case LmsCertificationIssueSources.CourseCompletion:
				{
					var enrollmentId = LmsCertificationIssuanceRules.RefId(issuance.SourceRefId, EnrollmentRefPrefix);
					var enrollment = enrollmentId > 0 ? await _enrollments.GetByIdForDepartmentAsync(departmentId, enrollmentId) : null;
					if (enrollment == null)
						return (false, null, new List<int>());
					var latest = (await _reader.CourseCompletionsAsync(departmentId, enrollment.LmsCourseId, issuance.UserId, voided))
						.OrderByDescending(f => f.CompletedOn).FirstOrDefault();
					return latest == null ? (false, null, new List<int>())
						: (true, latest.CompletedOn, latest.LearningRecordId.HasValue ? new List<int> { latest.LearningRecordId.Value } : new List<int>());
				}
				case LmsCertificationIssueSources.PathCompletion:
				{
					var pathId = LmsCertificationIssuanceRules.RefId(issuance.SourceRefId, LmsCertificationIssuanceRules.PathRefPrefix);
					var latest = pathId <= 0 ? null : (await _reader.PathCompletionsAsync(departmentId, new[] { pathId }, issuance.UserId, voided))
						.OrderByDescending(f => f.CompletedOn).FirstOrDefault();
					return latest == null ? (false, null, new List<int>()) : (true, latest.CompletedOn, latest.RecordIds ?? new List<int>());
				}
				case LmsCertificationIssueSources.RequirementSet:
				{
					var setId = LmsCertificationIssuanceRules.RefId(issuance.SourceRefId, LmsCertificationIssuanceRules.SetRefPrefix);
					var set = type == null ? null : await _sets.GetForTypeAsync(departmentId, type.DepartmentCertificationTypeId);
					if (set == null || set.LmsCertificationRequirementSetId != setId)
						return (false, null, new List<int>());
					var items = await _items.GetForSetsAsync(departmentId, new[] { set.LmsCertificationRequirementSetId }) ?? new List<LmsCertificationRequirementItem>();
					if (items.Count == 0)
						return (false, null, new List<int>());
					var facts = await _reader.ForItemsAsync(departmentId, items, issuance.UserId, voided, DateTime.UtcNow.AddMonths(-(CertificationValidity.CycleMonths(type) + 24)));
					var previous = Previous(issuance);
					var progress = LmsCertificationRequirementEvaluator.Evaluate(set, type, items, i => i.Label, facts, issuance.UserId,
						previous == null ? Array.Empty<PersonnelCertification>() : new[] { previous }, department, DateTime.UtcNow);
					return progress.Complete ? (true, progress.CompletedOnUtc, progress.BasisRecordIds) : (false, null, new List<int>());
				}
				case LmsCertificationIssueSources.CycleAutoRenew:
				{
					var previousId = LmsCertificationIssuanceRules.RefId(issuance.SourceRefId, LmsCertificationIssuanceRules.CycleRefPrefix);
					if (previousId <= 0 || type?.RenewalCreditHoursRequired == null || type.RenewalCreditHoursRequired.Value <= 0)
						return (false, null, new List<int>());
					var totals = await _certifications.GetCertificationCreditTotalsAsync(new[] { previousId });
					var hours = totals != null && totals.TryGetValue(previousId, out var h) ? h : 0m;
					if (hours < type.RenewalCreditHoursRequired.Value)
						return (false, null, new List<int>());
					var credits = await _certifications.GetCertificationCreditsAsync(previousId) ?? new List<PersonnelCertificationCredit>();
					return (true, issuance.CompletedOn, credits.Where(c => c.LearningRecordId.HasValue).Select(c => c.LearningRecordId.Value).Distinct().ToList());
				}
				default:
					return (false, null, new List<int>());
			}
		}

		/// <summary>The record's state before the issuance as a record (null when the issuance created it).</summary>
		private static PersonnelCertification Previous(LmsCertificationIssuance issuance) => issuance.PreviousStatus.HasValue
			? new PersonnelCertification
			{
				PersonnelCertificationId = issuance.PersonnelCertificationId,
				DepartmentId = issuance.DepartmentId,
				UserId = issuance.UserId,
				DepartmentCertificationTypeId = issuance.DepartmentCertificationTypeId,
				Status = issuance.PreviousStatus.Value,
				ExpiresOn = issuance.PreviousExpiresOn,
				RecievedOn = issuance.PreviousRecievedOn
			}
			: null;

		/// <summary>Another completion keeps the issuance with a different expiry: the record follows when it still shows this issuance.</summary>
		private async Task AdjustAsync(LmsCertificationIssuance issuance, DepartmentCertificationType type, DateTime? expiry, List<int> recordIds, string actorUserId,
			CancellationToken cancellationToken)
		{
			var departmentId = issuance.DepartmentId;
			var record = await RecordOfAsync(issuance);
			var later = await LaterLiveAsync(issuance);
			var before = issuance.NewExpiresOn;
			if (record != null && !record.IsDeleted && later == null && SameDate(record.ExpiresOn, issuance.NewExpiresOn))
				await _certifications.RestoreCertificationStateAsync(record.PersonnelCertificationId, departmentId, (PersonnelCertificationStatuses)record.Status, expiry,
					"training_completion_voided", actorUserId ?? SystemUserId, cancellationToken);

			issuance.NewExpiresOn = expiry?.Date;
			issuance.BasisRecordIds = LmsCertificationIssuanceRules.FormatIds(recordIds);
			await _issuances.UpdateStateAsync(issuance, cancellationToken);
			Audit(departmentId, actorUserId ?? SystemUserId, AuditLogTypes.CertificationRolledBackByVoid,
				JsonConvert.SerializeObject(new { issuance.LmsCertificationIssuanceId, issuance.PersonnelCertificationId, ExpiresOn = before }),
				JsonConvert.SerializeObject(new { issuance.LmsCertificationIssuanceId, issuance.PersonnelCertificationId, ExpiresOn = expiry, Outcome = "Adjusted", issuance.BasisRecordIds }));
			if (type != null && later == null)
				await NotifyAsync(departmentId, issuance.UserId, type, issuance.PersonnelCertificationId, "CertReq.Notify.Adjusted", expiry, null, cancellationToken);
		}

		private async Task RollBackAsync(LmsCertificationIssuance issuance, DepartmentCertificationType type, string actorUserId, LmsCertificationVoidResult result,
			CancellationToken cancellationToken)
		{
			var departmentId = issuance.DepartmentId;
			var actor = actorUserId ?? SystemUserId;
			var record = await RecordOfAsync(issuance);
			var later = await LaterLiveAsync(issuance);
			string outcome;

			if (later != null)
			{
				// A later issuance stands on the record: the record keeps that state; the later row now goes back to what this one found.
				later.PreviousStatus = issuance.PreviousStatus;
				later.PreviousExpiresOn = issuance.PreviousExpiresOn;
				later.PreviousRecievedOn = issuance.PreviousRecievedOn;
				if (issuance.Action != (int)LmsCertificationIssueActions.Renewed)
					later.Action = issuance.Action;
				await _issuances.UpdateStateAsync(later, cancellationToken);
				issuance.State = (int)LmsCertificationIssuanceStates.Unwound;
				outcome = "Unwound";
				result.Unwound++;
			}
			else if (record == null || record.IsDeleted)
			{
				issuance.State = (int)LmsCertificationIssuanceStates.RolledBack;
				outcome = "AlreadyRemoved";
				result.RolledBack++;
			}
			else if (!SameDate(record.ExpiresOn, issuance.NewExpiresOn) || LmsCertificationIssuanceRules.IsBlocked(record))
			{
				// Changed by hand since (renewed, re-dated, suspended): an officer decides; the engine never undoes a person's change.
				issuance.State = (int)LmsCertificationIssuanceStates.ManualChangeKept;
				outcome = "ManualChangeKept";
				result.LeftForReview++;
			}
			else
			{
				if (issuance.Action == (int)LmsCertificationIssueActions.Renewed && issuance.PreviousStatus.HasValue)
				{
					await _certifications.RollBackCertificationAsync(record.PersonnelCertificationId, departmentId, (PersonnelCertificationStatuses)issuance.PreviousStatus.Value,
						issuance.PreviousExpiresOn, issuance.PreviousRecievedOn, "training_completion_voided", actor, cancellationToken);
					outcome = "Restored";
				}
				else
				{
					await _certifications.SoftDeleteCertificationAsync(record.PersonnelCertificationId, departmentId, actor, cancellationToken);
					outcome = "Withdrawn";
				}
				issuance.State = (int)LmsCertificationIssuanceStates.RolledBack;
				result.RolledBack++;
			}

			issuance.RolledBackOn = DateTime.UtcNow;
			issuance.RolledBackByUserId = Trim(actor, 128);
			issuance.RollbackReason = "training_completion_voided";
			await _issuances.UpdateStateAsync(issuance, cancellationToken);
			Audit(departmentId, actor, AuditLogTypes.CertificationRolledBackByVoid,
				JsonConvert.SerializeObject(new { issuance.LmsCertificationIssuanceId, issuance.PersonnelCertificationId, record?.Status, record?.ExpiresOn }),
				JsonConvert.SerializeObject(new
				{
					issuance.LmsCertificationIssuanceId, issuance.PersonnelCertificationId, Outcome = outcome, issuance.PreviousStatus, issuance.PreviousExpiresOn,
					Source = ((LmsCertificationIssueSources)issuance.Source).ToString(), issuance.SourceRefId
				}));

			if (type == null || (outcome != "Restored" && outcome != "Withdrawn"))
				return;
			await NotifyAsync(departmentId, issuance.UserId, type, issuance.PersonnelCertificationId, outcome == "Restored" ? "CertReq.Notify.Restored" : "CertReq.Notify.Withdrawn",
				issuance.PreviousExpiresOn, null, cancellationToken);
			Raise(new CertificationTrainingWorkflowEvent
			{
				DepartmentId = departmentId,
				Change = CertificationTrainingWorkflowChanges.RevokedByVoid,
				CertificationId = issuance.PersonnelCertificationId,
				CertificationTypeId = type.DepartmentCertificationTypeId,
				CertificationTypeName = type.Type,
				CertificationTypeCode = type.Code,
				UserId = issuance.UserId,
				Source = ((LmsCertificationIssueSources)issuance.Source).ToString(),
				Outcome = outcome,
				ExpiresOn = outcome == "Restored" ? issuance.PreviousExpiresOn : null,
				PreviousExpiresOn = issuance.NewExpiresOn,
				IssuanceId = issuance.LmsCertificationIssuanceId,
				ActorUserId = actorUserId,
				OccurredOn = DateTime.UtcNow
			});
		}

		private async Task<PersonnelCertification> RecordOfAsync(LmsCertificationIssuance issuance) =>
			(await _certifications.GetCertificationsForDepartmentAsync(issuance.DepartmentId, new[] { issuance.UserId }) ?? new List<PersonnelCertification>())
				.FirstOrDefault(r => r != null && r.PersonnelCertificationId == issuance.PersonnelCertificationId);

		/// <summary>The next live issuance on the same record after this one, if any.</summary>
		private async Task<LmsCertificationIssuance> LaterLiveAsync(LmsCertificationIssuance issuance) =>
			(await _issuances.GetForCertificationAsync(issuance.DepartmentId, issuance.PersonnelCertificationId) ?? new List<LmsCertificationIssuance>())
				.Where(i => i.IsLive && i.LmsCertificationIssuanceId != issuance.LmsCertificationIssuanceId &&
				            (i.IssuedOn > issuance.IssuedOn || (i.IssuedOn == issuance.IssuedOn && i.LmsCertificationIssuanceId > issuance.LmsCertificationIssuanceId)))
				.OrderBy(i => i.IssuedOn).ThenBy(i => i.LmsCertificationIssuanceId).FirstOrDefault();

		#endregion Void

		#region Helpers

		private static bool SameDate(DateTime? a, DateTime? b) => a.HasValue == b.HasValue && (!a.HasValue || a.Value.Date == b.Value.Date);

		private static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		/// <summary>The member's notice (push + email in their language; inactive members are skipped by the notifier).</summary>
		private async Task NotifyAsync(int departmentId, string userId, DepartmentCertificationType type, int certificationId, string messageKey, DateTime? date, object extra,
			CancellationToken cancellationToken)
		{
			try
			{
				await _notifier.SendAsync(departmentId, new[] { userId }, "CertReq.Notify.Title", messageKey,
					culture => new object[] { type.Type, date.HasValue ? date.Value.ToString("d", culture) : "-" }, EventCode(certificationId), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The certification notice to {userId} in department {departmentId} failed.");
			}
		}

		/// <summary>The push event code of a certification notice ("CR:{id}", EventTypes 77/78).</summary>
		public static string EventCode(int certificationId) => "CR:" + certificationId.ToString(CultureInfo.InvariantCulture);

		private void Raise(CertificationTrainingWorkflowEvent evt)
		{
			try
			{
				_events?.SendMessage(evt);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The certification workflow event could not be raised.");
			}
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after) =>
			_events?.SendMessage(new AuditEvent { DepartmentId = departmentId, UserId = userId, Type = type, Before = before, After = after, Successful = true });

		#endregion Helpers
	}
}
