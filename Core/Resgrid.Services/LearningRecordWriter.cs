using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;

using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The writers of the one transcript (Training T1, §12.2). Every source turns its event into drafts and
	/// <see cref="WriteSourceAsync"/> reconciles them with the ledger: an identical record is left alone, a changed one is voided
	/// and superseded (R-T4), a member or component the source no longer lists is voided, and new ones are inserted through
	/// the idempotency key (R-T1). Linked sources of one session count once (R-T2), unlinked overlapping ones are flagged as
	/// possible duplicates, and derived certification credits follow every write (R-T5). Reads its sources through
	/// repositories so the source services can call it without a construction cycle.
	/// </summary>
	public class LearningRecordWriter : ILearningRecordWriter
	{
		public const string SystemUserId = "system";

		private readonly ILearningRecordsRepository _records;
		private readonly ICategoryCreditMappingsRepository _mappings;
		private readonly ITrainingCategoriesRepository _categories;
		private readonly IPersonnelCertificationRepository _certificationRecords;
		private readonly IPersonnelCertificationCreditsRepository _credits;
		private readonly Lazy<ICertificationService> _certifications;
		private readonly ITrainingRepository _trainings;
		private readonly ITrainingUserRepository _trainingUsers;
		private readonly ITrainingCompletionHistoriesRepository _histories;
		private readonly ILogUsersRepository _logUsers;
		private readonly IRmsOperationalRecordsRepository _rmsRecords;
		private readonly IRmsRecordParticipantsRepository _rmsParticipants;
		private readonly ICalendarItemsRepository _calendarItems;
		private readonly ICalendarItemTypeRepository _calendarTypes;
		private readonly IDepartmentSettingsService _settings;
		private readonly IEventAggregator _events;
		private readonly Lazy<IEnumerable<ILearningRecordObserver>> _observers;

		/// <summary>An observer's own work never notifies again (a certification issued from a write is not a new learning record).</summary>
		private static readonly System.Threading.AsyncLocal<int> NotifyDepth = new System.Threading.AsyncLocal<int>();

		public LearningRecordWriter(ILearningRecordsRepository records, ICategoryCreditMappingsRepository mappings, ITrainingCategoriesRepository categories,
			IPersonnelCertificationRepository certificationRecords, IPersonnelCertificationCreditsRepository credits, Lazy<ICertificationService> certifications,
			ITrainingRepository trainings, ITrainingUserRepository trainingUsers, ITrainingCompletionHistoriesRepository histories, ILogUsersRepository logUsers,
			IRmsOperationalRecordsRepository rmsRecords, IRmsRecordParticipantsRepository rmsParticipants, ICalendarItemsRepository calendarItems,
			ICalendarItemTypeRepository calendarTypes, IDepartmentSettingsService settings, IEventAggregator events,
			Lazy<IEnumerable<ILearningRecordObserver>> observers)
		{
			_observers = observers;
			_records = records;
			_mappings = mappings;
			_categories = categories;
			_certificationRecords = certificationRecords;
			_credits = credits;
			_certifications = certifications;
			_trainings = trainings;
			_trainingUsers = trainingUsers;
			_histories = histories;
			_logUsers = logUsers;
			_rmsRecords = rmsRecords;
			_rmsParticipants = rmsParticipants;
			_calendarItems = calendarItems;
			_calendarTypes = calendarTypes;
			_settings = settings;
			_events = events;
		}

		#region Core

		public async Task<LearningRecordWriteResult> WriteSourceAsync(int departmentId, LearningRecordSourceKinds kind, string sourceRefId,
			IReadOnlyCollection<LearningRecordDraft> drafts, string actingUserId, CancellationToken cancellationToken = default)
		{
			var result = new LearningRecordWriteResult();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(sourceRefId))
				return result;

			var now = DateTime.UtcNow;
			var live = (await _records.GetBySourceAsync(departmentId, (int)kind, sourceRefId) ?? new List<LearningRecord>()).Where(r => !r.IsVoided).ToList();
			var wanted = (drafts ?? Array.Empty<LearningRecordDraft>())
				.Where(d => d != null && !string.IsNullOrWhiteSpace(d.UserId))
				.GroupBy(d => Key(d.UserId, d.ComponentKey))
				.Select(g => g.First())
				.ToList();
			var matched = new HashSet<int>();
			var voided = new List<LearningRecord>();

			foreach (var draft in wanted)
			{
				// R-T2: a source below another linked source of the same session writes no hours, so a re-save compares as unchanged.
				if (!string.IsNullOrWhiteSpace(draft.SessionKey) && LearningRecordRules.Precedence((int)kind) > 0 && draft.Hours > 0 &&
					(await _records.GetBySessionKeyAsync(departmentId, draft.SessionKey) ?? new List<LearningRecord>())
						.Any(r => !r.IsVoided && string.Equals(r.UserId, draft.UserId, StringComparison.OrdinalIgnoreCase) &&
							LearningRecordRules.Precedence(r.SourceKind) > LearningRecordRules.Precedence((int)kind)))
					draft.Hours = 0;

				var existing = live.FirstOrDefault(r => Key(r.UserId, r.ComponentKey) == Key(draft.UserId, draft.ComponentKey));
				if (existing != null)
				{
					matched.Add(existing.LearningRecordId);

					// A rejected submission stays rejected, and an officer's verification of an open check-in stands until the
					// source itself says more (a check-out).
					if (existing.Status == (int)LearningRecordStatuses.Rejected ||
						LearningRecordRules.SameAs(existing, draft) ||
						(existing.VerifiedByUserId != null && draft.Status == LearningRecordStatuses.Pending))
					{
						result.Unchanged++;
						continue;
					}

					// R-T4: the source changed. The old record is voided (its credits go) and a new one supersedes it; a category
					// an officer gave the old record carries over when the source has none.
					await _records.VoidAsync(departmentId, existing.LearningRecordId, "source_changed", actingUserId ?? SystemUserId, now, cancellationToken);
					existing.Status = (int)LearningRecordStatuses.Voided;
					voided.Add(existing);
					result.Voided++;
					result.CreditsRemoved += await RemoveCreditsAsync(existing, actingUserId, cancellationToken);

					draft.CategoryId ??= existing.CategoryId;
					// Round 7B: a completion recorded by an instructor stays one when its source is rewritten (who recorded it, when, and
					// the recording row carry over; the source row only knows the method).
					if (draft.CompletionMethod == existing.CompletionMethod && draft.CompletionMethod == (int)LearningCompletionMethods.RecordedByOther)
					{
						draft.RecordedByUserId ??= existing.RecordedByUserId;
						draft.RecordedOn ??= existing.RecordedOn;
						draft.RecordedCompletionId ??= existing.RecordedCompletionId;
					}
					var replacement = await InsertAsync(departmentId, kind, sourceRefId, draft, existing.LearningRecordId, actingUserId, now, cancellationToken);
					if (replacement != null)
					{
						result.Written++;
						result.Records.Add(replacement);
					}

					continue;
				}

				var inserted = await InsertAsync(departmentId, kind, sourceRefId, draft, null, actingUserId, now, cancellationToken);
				if (inserted != null)
				{
					result.Written++;
					result.Records.Add(inserted);
				}
				else
				{
					result.Unchanged++;
				}
			}

			// A member or component the source no longer lists (removed participant, deleted check-in).
			foreach (var stale in live.Where(r => !matched.Contains(r.LearningRecordId) && r.Status != (int)LearningRecordStatuses.Rejected))
			{
				if (await _records.VoidAsync(departmentId, stale.LearningRecordId, "source_removed", actingUserId ?? SystemUserId, now, cancellationToken))
				{
					stale.Status = (int)LearningRecordStatuses.Voided;
					voided.Add(stale);
					result.Voided++;
					result.CreditsRemoved += await RemoveCreditsAsync(stale, actingUserId, cancellationToken);
				}
			}

			foreach (var record in result.Records.ToList())
			{
				await ApplySessionPrecedenceAsync(record, actingUserId, now, result, cancellationToken);
				await FlagPossibleDuplicateAsync(record, cancellationToken);
				if (!record.IsVoided)
					result.CreditsAdded += await ApplyCreditsAsync(record, false, actingUserId, cancellationToken);
			}

			// Round 9A: the certification engine hears about it once the whole source is reconciled (a replacement is already stored).
			await NotifyChangedAsync(departmentId, result.Records, voided, actingUserId, cancellationToken);
			return result;
		}

		public async Task<LearningRecordWriteResult> VoidSourceAsync(int departmentId, LearningRecordSourceKinds kind, string sourceRefId, string reason,
			string actingUserId, CancellationToken cancellationToken = default)
		{
			var result = new LearningRecordWriteResult();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(sourceRefId))
				return result;

			var now = DateTime.UtcNow;
			var voided = new List<LearningRecord>();
			foreach (var record in (await _records.GetBySourceAsync(departmentId, (int)kind, sourceRefId) ?? new List<LearningRecord>()).Where(r => !r.IsVoided))
			{
				if (!await _records.VoidAsync(departmentId, record.LearningRecordId, reason ?? "source_removed", actingUserId ?? SystemUserId, now, cancellationToken))
					continue;
				record.Status = (int)LearningRecordStatuses.Voided;
				voided.Add(record);
				result.Voided++;
				result.CreditsRemoved += await RemoveCreditsAsync(record, actingUserId, cancellationToken);
			}

			await NotifyChangedAsync(departmentId, null, voided, actingUserId, cancellationToken);
			return result;
		}

		private async Task<LearningRecord> InsertAsync(int departmentId, LearningRecordSourceKinds kind, string sourceRefId, LearningRecordDraft draft, int? supersedesId,
			string actingUserId, DateTime now, CancellationToken cancellationToken)
		{
			var record = new LearningRecord
			{
				DepartmentId = departmentId,
				UserId = draft.UserId.Trim(),
				SourceKind = (int)kind,
				SourceRefId = LearningRecordRules.Trim(sourceRefId, 128),
				ComponentKey = LearningRecordRules.Trim(draft.ComponentKey, 64) ?? string.Empty,
				SessionKey = LearningRecordRules.Trim(draft.SessionKey, 128),
				Title = LearningRecordRules.Trim(draft.Title, LearningRecordRules.MaxTitleLength) ?? "Training",
				CourseCode = LearningRecordRules.Trim(draft.CourseCode, 100),
				CompletedOn = draft.CompletedOn == default ? now : draft.CompletedOn,
				StartedOn = draft.StartedOn,
				EndedOn = draft.EndedOn,
				Hours = Math.Min(9999m, Math.Round(Math.Max(0m, draft.Hours), 2, MidpointRounding.AwayFromZero)),
				CategoryId = draft.CategoryId,
				MethodOfTraining = draft.MethodOfTraining.HasValue && Enum.IsDefined(typeof(MethodsOfTraining), draft.MethodOfTraining.Value) ? draft.MethodOfTraining : null,
				Objectives = LearningRecordRules.Trim(draft.Objectives, LearningRecord.MaxObjectivesLength),
				Location = LearningRecordRules.Trim(draft.Location, 250),
				CeProvider = LearningRecordRules.Trim(draft.CeProvider, 200),
				CeCourseNumber = LearningRecordRules.Trim(draft.CeCourseNumber, 100),
				CeHours = draft.CeHours.HasValue ? Math.Min(9999m, Math.Round(Math.Max(0m, draft.CeHours.Value), 2)) : (decimal?)null,
				InstructorUserIds = LearningRecordRules.Trim(draft.InstructorUserIds, 1000),
				InstructorNames = LearningRecordRules.Trim(draft.InstructorNames, 1000),
				Status = (int)draft.Status,
				SupersedesId = supersedesId,
				VerifiedByUserId = draft.Status == LearningRecordStatuses.Verified ? LearningRecordRules.Trim(draft.VerifiedByUserId, 128) : null,
				VerifiedOn = draft.Status == LearningRecordStatuses.Verified && !string.IsNullOrWhiteSpace(draft.VerifiedByUserId) ? now : (DateTime?)null,
				EvidenceFileId = draft.EvidenceFileId,
				IsBackfill = draft.IsBackfill,
				ExternalId = LearningRecordRules.Trim(draft.ExternalId, 100),
				Notes = LearningRecordRules.Trim(draft.Notes, LearningRecord.MaxNotesLength),
				Tags = LearningRecordRules.Trim(draft.Tags, 500),
				CreatedOn = now,
				CreatedByUserId = actingUserId ?? SystemUserId
			};
			// Round 7B: a completion recorded by someone else (permission 135) says so, and by whom; never presented as the member's own.
			if (draft.CompletionMethod == (int)LearningCompletionMethods.RecordedByOther)
			{
				record.CompletionMethod = draft.CompletionMethod;
				record.RecordedByUserId = LearningRecordRules.Trim(draft.RecordedByUserId, 128);
				record.RecordedOn = draft.RecordedOn;
				record.RecordedCompletionId = draft.RecordedCompletionId;
			}
			// Round 9B: a member submission carries its review state; one written by another path (an LMS training request's completion) that
			// is pending waits for a reviewer like any other submission.
			if (kind == LearningRecordSourceKinds.ExternalSubmission)
			{
				record.ReviewState = draft.ReviewState ?? (draft.Status == LearningRecordStatuses.Pending ? (int)TrainingSubmissionReviewStates.AwaitingReview : (int?)null);
				record.SubmittedOn = draft.SubmittedOn ?? now;
			}

			var (stored, inserted) = await _records.InsertIfAbsentAsync(record, cancellationToken);
			return inserted ? stored : null;
		}

		/// <summary>
		/// R-T2: of the linked sources of one session (one SessionKey, one member), only the highest precedence one carries hours.
		/// A lower source written after a higher one gets a zero-hour record; a higher one written after lower ones supersedes
		/// their records with zero-hour copies.
		/// </summary>
		private async Task ApplySessionPrecedenceAsync(LearningRecord record, string actingUserId, DateTime now, LearningRecordWriteResult result, CancellationToken ct)
		{
			if (record == null || string.IsNullOrWhiteSpace(record.SessionKey) || LearningRecordRules.Precedence(record.SourceKind) == 0)
				return;

			var siblings = (await _records.GetBySessionKeyAsync(record.DepartmentId, record.SessionKey) ?? new List<LearningRecord>())
				.Where(r => r.LearningRecordId != record.LearningRecordId && !r.IsVoided && r.SourceKind != record.SourceKind &&
					string.Equals(r.UserId, record.UserId, StringComparison.OrdinalIgnoreCase) && LearningRecordRules.Precedence(r.SourceKind) > 0)
				.ToList();
			if (siblings.Count == 0)
				return;

			var mine = LearningRecordRules.Precedence(record.SourceKind);
			if (siblings.Any(s => LearningRecordRules.Precedence(s.SourceKind) > mine) && record.Hours > 0)
			{
				record.Hours = 0;
				await _records.UpdateColumnsAsync(record, new[] { "Hours" }, ct);
				return;
			}

			foreach (var lower in siblings.Where(s => LearningRecordRules.Precedence(s.SourceKind) < mine && s.Hours > 0))
			{
				var status = (LearningRecordStatuses)lower.Status;
				if (!await _records.VoidAsync(lower.DepartmentId, lower.LearningRecordId, "session_counted_by_higher_source", actingUserId ?? SystemUserId, now, ct))
					continue;
				lower.Status = (int)LearningRecordStatuses.Voided;
				result.Voided++;
				await NotifyChangedAsync(lower.DepartmentId, null, new[] { lower }, actingUserId, ct);
				result.CreditsRemoved += await RemoveCreditsAsync(lower, actingUserId, ct);

				var copy = new LearningRecordDraft
				{
					UserId = lower.UserId, ComponentKey = lower.ComponentKey, SessionKey = lower.SessionKey, Title = lower.Title, CourseCode = lower.CourseCode,
					CompletedOn = lower.CompletedOn, StartedOn = lower.StartedOn, EndedOn = lower.EndedOn, Hours = 0, CategoryId = lower.CategoryId,
					MethodOfTraining = lower.MethodOfTraining, Objectives = lower.Objectives, CeProvider = lower.CeProvider, CeCourseNumber = lower.CeCourseNumber,
					CeHours = lower.CeHours, InstructorNames = lower.InstructorNames, InstructorUserIds = lower.InstructorUserIds, Tags = lower.Tags, Notes = lower.Notes,
					ExternalId = lower.ExternalId, Status = status, IsBackfill = lower.IsBackfill, VerifiedByUserId = lower.VerifiedByUserId
				};
				var zero = await InsertAsync(lower.DepartmentId, (LearningRecordSourceKinds)lower.SourceKind, lower.SourceRefId, copy, lower.LearningRecordId, actingUserId, now, ct);
				if (zero != null)
					result.Written++;
			}
		}

		/// <summary>R-T2 for unlinked sources: flags the record when the member has another overlapping record that day.</summary>
		private async Task FlagPossibleDuplicateAsync(LearningRecord record, CancellationToken ct)
		{
			if (record == null || record.IsVoided || !record.StartedOn.HasValue || !record.EndedOn.HasValue || record.Hours <= 0)
				return;

			var day = record.StartedOn.Value.Date;
			var others = await _records.GetForMemberInWindowAsync(record.DepartmentId, record.UserId, day.AddDays(-1), day.AddDays(2)) ?? new List<LearningRecord>();
			var other = others.FirstOrDefault(o => LearningRecordRules.IsPossibleDuplicate(record, o));
			if (other == null)
				return;

			record.PossibleDuplicateOfId = other.LearningRecordId;
			await _records.UpdateColumnsAsync(record, new[] { "PossibleDuplicateOfId" }, ct);
		}

		private static string Key(string userId, string componentKey) =>
			(userId ?? string.Empty).Trim().ToUpperInvariant() + "|" + (componentKey ?? string.Empty).Trim().ToUpperInvariant();

		/// <summary>
		/// Round 9A: the observers (the LMS certification engine) hear about records that now count and records that were voided. Backfilled
		/// records and pending ones are left out; an observer's own work never notifies again; a failure is logged and never undoes the write.
		/// </summary>
		public async Task NotifyChangedAsync(int departmentId, IReadOnlyCollection<LearningRecord> written, IReadOnlyCollection<LearningRecord> voided, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || NotifyDepth.Value > 0)
				return;
			var change = new LearningRecordChange
			{
				DepartmentId = departmentId,
				ActingUserId = actingUserId,
				Written = (written ?? Array.Empty<LearningRecord>()).Where(r => r != null && !r.IsBackfill && r.Status == (int)LearningRecordStatuses.Verified).ToList(),
				Voided = (voided ?? Array.Empty<LearningRecord>()).Where(r => r != null && r.LearningRecordId > 0 && !r.IsBackfill).ToList()
			};
			if (change.IsEmpty)
				return;

			IEnumerable<ILearningRecordObserver> observers;
			try
			{
				observers = _observers?.Value ?? Enumerable.Empty<ILearningRecordObserver>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Learning record observers could not be resolved.");
				return;
			}

			NotifyDepth.Value++;
			try
			{
				foreach (var observer in observers)
				{
					try
					{
						await observer.OnLearningRecordsChangedAsync(change, cancellationToken);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"A learning record observer failed for department {departmentId}.");
					}
				}
			}
			finally
			{
				NotifyDepth.Value--;
			}
		}

		#endregion

		#region Credits (R-T5)

		public async Task<int> ApplyCreditsAsync(LearningRecord record, bool includeBackfill, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (!LearningRecordRules.CanCredit(record) || (record.IsBackfill && !includeBackfill))
				return 0;

			var mappings = (await _mappings.GetForDepartmentAsync(record.DepartmentId) ?? new List<CategoryCreditMapping>())
				.Where(m => LearningRecordRules.MappingApplies(m, record, includeBackfill))
				.ToList();
			if (mappings.Count == 0)
				return 0;

			var holder = (await _certificationRecords.GetForDepartmentAsync(record.DepartmentId, new[] { record.UserId }) ?? Enumerable.Empty<PersonnelCertification>())
				.Where(r => string.Equals(r.UserId, record.UserId, StringComparison.OrdinalIgnoreCase)).ToList();
			var existing = (await _credits.GetByLearningRecordIdsAsync(record.DepartmentId, new[] { record.LearningRecordId }) ?? Enumerable.Empty<PersonnelCertificationCredit>()).ToList();
			var category = record.CategoryId.HasValue
				? (await _categories.GetForDepartmentAsync(record.DepartmentId, true) ?? new List<TrainingCategory>()).FirstOrDefault(c => c.TrainingCategoryId == record.CategoryId.Value)
				: null;
			var added = 0;

			foreach (var typeId in mappings.Select(m => m.DepartmentCertificationTypeId).Distinct())
			{
				var cycle = LearningRecordRules.FindCycle(holder, typeId, record.CompletedOn);
				if (cycle == null || existing.Any(c => c.PersonnelCertificationId == cycle.PersonnelCertificationId))
					continue;

				try
				{
					await _certifications.Value.AddCertificationCreditAsync(new PersonnelCertificationCredit
					{
						PersonnelCertificationId = cycle.PersonnelCertificationId,
						DepartmentId = record.DepartmentId,
						CreditDate = record.CompletedOn.Date,
						Hours = record.Hours,
						Category = LearningRecordRules.Trim(category?.Name, 100),
						Description = LearningRecordRules.Trim(record.Title, 250),
						LearningRecordId = record.LearningRecordId
					}, actingUserId ?? SystemUserId, cancellationToken);
					added++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// A refused credit (a unit-scoped type, hours out of range) leaves the record on the transcript.
					Logging.LogException(ex, $"Learning record {record.LearningRecordId} credit to certification {cycle.PersonnelCertificationId} failed");
				}
			}

			return added;
		}

		public async Task<int> RemoveCreditsAsync(LearningRecord record, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (record == null || record.LearningRecordId <= 0)
				return 0;

			var removed = 0;
			foreach (var credit in (await _credits.GetByLearningRecordIdsAsync(record.DepartmentId, new[] { record.LearningRecordId }) ?? Enumerable.Empty<PersonnelCertificationCredit>()).ToList())
			{
				try
				{
					if (await _certifications.Value.DeleteCertificationCreditAsync(credit.PersonnelCertificationCreditId, record.DepartmentId, actingUserId ?? SystemUserId, cancellationToken))
						removed++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Removing credit {credit.PersonnelCertificationCreditId} of learning record {record.LearningRecordId} failed");
				}
			}

			return removed;
		}

		#endregion

		#region Sources

		public async Task<LearningRecordWriteResult> WriteCourseCompletionAsync(int departmentId, int trainingId, string userId, bool isBackfill = false,
			CancellationToken cancellationToken = default)
		{
			var result = new LearningRecordWriteResult();
			var training = await _trainings.GetTrainingByTrainingIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId || string.IsNullOrWhiteSpace(userId))
				return result;

			var cycles = new Dictionary<int, DateTime>();
			var live = await _trainingUsers.GetTrainingUserByTrainingIdAndUserIdAsync(trainingId, userId);
			if (live != null && live.Complete && live.CompletedOn.HasValue)
				cycles[live.Cycle] = live.CompletedOn.Value;

			foreach (var history in (await _histories.GetForMemberAsync(departmentId, userId) ?? new List<TrainingCompletionHistory>())
				.Where(h => h.TrainingId == trainingId && h.CompletedOn.HasValue))
				if (!cycles.ContainsKey(history.Cycle))
					cycles[history.Cycle] = history.CompletedOn.Value;

			var member = live?.UserId ?? userId;
			var existing = await _records.GetExistingSourceRefIdsAsync(departmentId, (int)LearningRecordSourceKinds.CourseCompletion,
				cycles.Keys.Select(c => LearningRecordRules.CompletionRef(trainingId, c)));
			foreach (var (cycle, completedOn) in cycles.OrderBy(c => c.Key))
			{
				var reference = LearningRecordRules.CompletionRef(trainingId, cycle);
				// A completion is a fact of the day it happened: an earlier cycle already on the transcript is not rewritten when the
				// training is edited later (only a re-write of the live cycle by its own completion would).
				if (existing.Contains(reference) && (live == null || live.Cycle != cycle))
				{
					result.Unchanged++;
					continue;
				}

				result.Add(await WriteSourceAsync(departmentId, LearningRecordSourceKinds.CourseCompletion, reference,
					new[] { CompletionDraft(training, member, completedOn, isBackfill, live != null && live.Cycle == cycle ? live : null) }, isBackfill ? SystemUserId : member, cancellationToken));
			}

			return result;
		}

		/// <summary>
		/// The transcript draft of a baseline training completion. <paramref name="recordedFrom"/> is the live assignment row of the cycle: when
		/// an instructor recorded that completion (round 7B), the draft carries the method and the recording row.
		/// </summary>
		public static LearningRecordDraft CompletionDraft(Training training, string userId, DateTime completedOn, bool isBackfill, TrainingUser recordedFrom = null)
		{
			var draft = CompletionDraft(training, userId, completedOn, isBackfill);
			if (recordedFrom != null && recordedFrom.CompletionMethod == (int)LearningCompletionMethods.RecordedByOther)
			{
				draft.CompletionMethod = recordedFrom.CompletionMethod;
				draft.RecordedCompletionId = recordedFrom.RecordedCompletionId;
			}
			return draft;
		}

		public static LearningRecordDraft CompletionDraft(Training training, string userId, DateTime completedOn, bool isBackfill)
		{
			return new LearningRecordDraft
			{
				UserId = userId,
				Title = training.Name,
				CourseCode = training.ExternalCode,
				CompletedOn = completedOn,
				Hours = training.Hours ?? 0m,
				CategoryId = training.CategoryId,
				MethodOfTraining = training.MethodOfTraining,
				Objectives = training.Objectives,
				CeProvider = training.CeProvider,
				CeCourseNumber = training.CeCourseNumber,
				CeHours = training.CeHours,
				Tags = training.Tags,
				Status = LearningRecordStatuses.Verified,
				IsBackfill = isBackfill
			};
		}

		public async Task<LearningRecordWriteResult> WriteTrainingLogAsync(Log log, string actingUserId, bool isBackfill = false, CancellationToken cancellationToken = default)
		{
			if (log == null || log.LogId <= 0)
				return new LearningRecordWriteResult();

			var reference = LearningRecordRules.LogRef(log.LogId);
			if (log.LogType != (int)LogTypes.Training)
				return await VoidSourceAsync(log.DepartmentId, LearningRecordSourceKinds.TrainingRecord, reference, "not_a_training_record", actingUserId, cancellationToken);

			var users = log.Users?.ToList();
			if (users == null || users.Count == 0)
				users = (await _logUsers.GetLogsByLogIdAsync(log.LogId) ?? Enumerable.Empty<LogUser>()).ToList();

			return await WriteSourceAsync(log.DepartmentId, LearningRecordSourceKinds.TrainingRecord, reference, LogDrafts(log, users, isBackfill), actingUserId, cancellationToken);
		}

		public static List<LearningRecordDraft> LogDrafts(Log log, IEnumerable<LogUser> users, bool isBackfill)
		{
			var hours = LearningRecordRules.Hours(log.StartedOn, log.EndedOn);
			return (users ?? Enumerable.Empty<LogUser>())
				.Where(u => u != null && !string.IsNullOrWhiteSpace(u.UserId))
				.GroupBy(u => u.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g => new LearningRecordDraft
				{
					UserId = g.First().UserId,
					Title = string.IsNullOrWhiteSpace(log.Course) ? "Training record" : log.Course,
					CourseCode = log.CourseCode,
					CompletedOn = log.EndedOn ?? log.StartedOn ?? log.LoggedOn,
					StartedOn = log.StartedOn,
					EndedOn = log.EndedOn,
					Hours = hours,
					InstructorNames = log.Instructors,
					Status = LearningRecordStatuses.Verified,
					IsBackfill = isBackfill
				})
				.ToList();
		}

		public Task<LearningRecordWriteResult> RemoveTrainingLogAsync(int departmentId, int logId, string actingUserId, CancellationToken cancellationToken = default) =>
			VoidSourceAsync(departmentId, LearningRecordSourceKinds.TrainingRecord, LearningRecordRules.LogRef(logId), "training_record_deleted", actingUserId, cancellationToken);

		private static readonly int[] RecordsFinalStates = { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Submitted, (int)RmsRecordState.Accepted };

		public async Task<LearningRecordWriteResult> WriteRecordsTrainingAsync(int departmentId, string rmsRecordId, string actingUserId, bool isBackfill = false,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(rmsRecordId))
				return new LearningRecordWriteResult();

			var reference = LearningRecordRules.RecordsRef(rmsRecordId);
			var record = await _rmsRecords.GetByIdForDepartmentAsync(departmentId, rmsRecordId);
			if (record == null || !string.Equals(record.DefinitionKey, RmsDefinitionKeys.Training, StringComparison.OrdinalIgnoreCase) ||
				record.DeletedOn.HasValue || record.PurgedOn.HasValue || !RecordsFinalStates.Contains(record.State) || string.IsNullOrEmpty(record.CurrentRevisionId))
				return await VoidSourceAsync(departmentId, LearningRecordSourceKinds.TrainingRecord, reference, "training_record_not_final", actingUserId, cancellationToken);

			var participants = (await _rmsParticipants.GetForRecordAsync(departmentId, rmsRecordId, record.CurrentRevisionId) ?? Enumerable.Empty<RmsRecordParticipant>())
				.Where(p => p != null && p.DeletedOn == null && !string.IsNullOrWhiteSpace(p.UserId)).ToList();
			return await WriteSourceAsync(departmentId, LearningRecordSourceKinds.TrainingRecord, reference, RecordsDrafts(record, participants, isBackfill), actingUserId, cancellationToken);
		}

		/// <summary>One draft per participant; hours as Records analytics computes them (participation times, else the record's).</summary>
		public static List<LearningRecordDraft> RecordsDrafts(RmsOperationalRecord record, IEnumerable<RmsRecordParticipant> participants, bool isBackfill)
		{
			return (participants ?? Enumerable.Empty<RmsRecordParticipant>())
				.GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g =>
				{
					var p = g.First();
					var timed = p.ParticipationStart.HasValue && p.ParticipationEnd.HasValue;
					var start = timed ? p.ParticipationStart : record.StartedOn;
					var end = timed ? p.ParticipationEnd : record.EndedOn;
					return new LearningRecordDraft
					{
						UserId = p.UserId,
						Title = string.IsNullOrWhiteSpace(record.DisplaySummary) ? (string.IsNullOrWhiteSpace(record.RecordNumber) ? "Training record" : "Training record " + record.RecordNumber) : record.DisplaySummary,
						CourseCode = record.RecordNumber,
						CompletedOn = end ?? start ?? record.FinalizedOn ?? record.CreatedOn,
						StartedOn = start,
						EndedOn = end,
						Hours = LearningRecordRules.Hours(start, end),
						Status = LearningRecordStatuses.Verified,
						IsBackfill = isBackfill
					};
				})
				.ToList();
		}

		public async Task<LearningRecordWriteResult> WriteCalendarCheckInAsync(CalendarItemCheckIn checkIn, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (checkIn == null || string.IsNullOrWhiteSpace(checkIn.CalendarItemCheckInId))
				return new LearningRecordWriteResult();

			var reference = LearningRecordRules.CheckInRef(checkIn.CalendarItemCheckInId);
			var item = await _calendarItems.GetByIdAsync(checkIn.CalendarItemId);
			var type = item != null && item.ItemType > 0 ? await _calendarTypes.GetByIdAsync(item.ItemType) : null;
			if (item == null || type == null || !type.CountsAsTraining || type.DepartmentId != checkIn.DepartmentId)
				return await VoidSourceAsync(checkIn.DepartmentId, LearningRecordSourceKinds.CalendarAttendance, reference, "not_a_training_event", actingUserId, cancellationToken);

			var settings = await _settings.GetTrainingRecordSettingsAsync(checkIn.DepartmentId) ?? new TrainingRecordSettings();
			return await WriteSourceAsync(checkIn.DepartmentId, LearningRecordSourceKinds.CalendarAttendance, reference,
				new[] { CheckInDraft(item, type, checkIn, settings) }, actingUserId, cancellationToken);
		}

		/// <summary>R-T9: hours rounded per the department setting; a check-in with no check-out is Pending for an officer, never a guess.</summary>
		public static LearningRecordDraft CheckInDraft(CalendarItem item, CalendarItemType type, CalendarItemCheckIn checkIn, TrainingRecordSettings settings)
		{
			var closed = checkIn.CheckOutTime.HasValue && checkIn.CheckOutTime.Value > checkIn.CheckInTime;
			return new LearningRecordDraft
			{
				UserId = checkIn.UserId,
				SessionKey = LearningRecordRules.CalendarSession(checkIn.CalendarItemId),
				Title = string.IsNullOrWhiteSpace(item.Title) ? type.Name : item.Title,
				CompletedOn = closed ? checkIn.CheckOutTime.Value : checkIn.CheckInTime,
				StartedOn = checkIn.CheckInTime,
				EndedOn = closed ? checkIn.CheckOutTime : null,
				Hours = closed ? (settings ?? new TrainingRecordSettings()).RoundHours(checkIn.CheckOutTime.Value - checkIn.CheckInTime) : 0m,
				CategoryId = type.DefaultCategoryId,
				Status = closed ? LearningRecordStatuses.Verified : LearningRecordStatuses.Pending
			};
		}

		public Task<LearningRecordWriteResult> RemoveCalendarCheckInAsync(CalendarItemCheckIn checkIn, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (checkIn == null || string.IsNullOrWhiteSpace(checkIn.CalendarItemCheckInId))
				return Task.FromResult(new LearningRecordWriteResult());
			return VoidSourceAsync(checkIn.DepartmentId, LearningRecordSourceKinds.CalendarAttendance, LearningRecordRules.CheckInRef(checkIn.CalendarItemCheckInId),
				"check_in_deleted", actingUserId, cancellationToken);
		}

		public async Task<LearningRecordWriteResult> WriteManualCreditAsync(PersonnelCertificationCredit credit, string userId, CancellationToken cancellationToken = default)
		{
			if (credit == null || credit.PersonnelCertificationCreditId <= 0 || credit.IsDerived)
				return new LearningRecordWriteResult();

			var holder = await _certificationRecords.GetByIdAsync(credit.PersonnelCertificationId);
			if (holder == null || holder.DepartmentId != credit.DepartmentId)
				return new LearningRecordWriteResult();

			// The title is the certification type's name: the credit's own category and description may be protected data (ADP)
			// and are never copied into the ledger.
			var type = holder.DepartmentCertificationTypeId.HasValue ? await _certifications.Value.GetCertificationTypeByIdAsync(holder.DepartmentCertificationTypeId.Value) : null;
			var draft = new LearningRecordDraft
			{
				UserId = holder.UserId,
				Title = type != null ? type.Type + " credit" : "Certification credit",
				CourseCode = type?.Code,
				CompletedOn = credit.CreditDate == default ? DateTime.UtcNow.Date : credit.CreditDate,
				Hours = credit.Hours,
				Status = LearningRecordStatuses.Verified,
				VerifiedByUserId = credit.AddedByUserId ?? userId
			};
			return await WriteSourceAsync(credit.DepartmentId, LearningRecordSourceKinds.ManualCredit, LearningRecordRules.CreditRef(credit.PersonnelCertificationCreditId),
				new[] { draft }, userId, cancellationToken);
		}

		public Task<LearningRecordWriteResult> RemoveManualCreditAsync(PersonnelCertificationCredit credit, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (credit == null || credit.PersonnelCertificationCreditId <= 0 || credit.IsDerived)
				return Task.FromResult(new LearningRecordWriteResult());
			return VoidSourceAsync(credit.DepartmentId, LearningRecordSourceKinds.ManualCredit, LearningRecordRules.CreditRef(credit.PersonnelCertificationCreditId),
				"credit_deleted", actingUserId, cancellationToken);
		}

		#endregion
	}
}
