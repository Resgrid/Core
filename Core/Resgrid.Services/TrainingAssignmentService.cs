using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Who is assigned a training and when it is due (protocols-training-knowledge-plan §13 T0 item 2). Writes are per row
	/// (T-D14) and only active members are assigned (inactive-member policy); the officer hears about every member skipped
	/// (T-D16).
	/// </summary>
	public class TrainingAssignmentService : ITrainingAssignmentService
	{
		/// <summary>Every progress column a reset or a repeat clears.</summary>
		private static readonly string[] ResetColumns =
		{
			"Viewed", "ViewedOn", "Complete", "CompletedOn", "Score", "Passed", "AttemptCount", "Cycle", "AssignedOn", "DueOn", "NotifiedOn",
			"RemindedOn", "LastReminderLeadDays", "OverdueNotifiedOn", "OriginalDueOn", "ExtensionReason", "ExtendedByUserId", "ExtendedOn",
			"CompletionMethod", "RecordedCompletionId"
		};

		private readonly ITrainingRepository _trainingRepository;
		private readonly ITrainingUserRepository _trainingUserRepository;
		private readonly ITrainingAudiencesRepository _audiencesRepository;
		private readonly ITrainingCompletionHistoriesRepository _historiesRepository;
		private readonly IContentAudienceService _audienceService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;

		public TrainingAssignmentService(ITrainingRepository trainingRepository, ITrainingUserRepository trainingUserRepository, ITrainingAudiencesRepository audiencesRepository,
			ITrainingCompletionHistoriesRepository historiesRepository, IContentAudienceService audienceService, IDepartmentsService departmentsService, IEventAggregator eventAggregator)
		{
			_trainingRepository = trainingRepository;
			_trainingUserRepository = trainingUserRepository;
			_audiencesRepository = audiencesRepository;
			_historiesRepository = historiesRepository;
			_audienceService = audienceService;
			_departmentsService = departmentsService;
			_eventAggregator = eventAggregator;
		}

		/// <summary>
		/// A never-opened assignment the audience made can be withdrawn when the member leaves the audience; one the member
		/// started, finished, was given an extension or exemption on, or that an officer or the member made, is kept.
		/// </summary>
		public static bool CanWithdraw(TrainingUser assignment)
		{
			return assignment != null && assignment.AssignmentSource == (int)TrainingAssignmentSources.Audience && !assignment.Viewed && !assignment.Complete &&
				assignment.AttemptCount == 0 && !assignment.RemovedOn.HasValue && !assignment.ExemptOn.HasValue && !assignment.ExtendedOn.HasValue;
		}

		private static TrainingUser Find(IEnumerable<TrainingUser> rows, string userId) =>
			rows?.FirstOrDefault(x => x != null && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));

		private async Task<List<TrainingUser>> RowsAsync(Training training)
		{
			if (training.Users == null)
				training.Users = await _trainingUserRepository.GetByTrainingIdAsync(training.TrainingId) ?? new List<TrainingUser>();
			return training.Users.ToList();
		}

		private async Task<TrainingUser> InsertAsync(Training training, Department department, string userId, TrainingAssignmentSources source, DateTime utcNow, CancellationToken ct)
		{
			var row = new TrainingUser
			{
				TrainingId = training.TrainingId,
				UserId = userId,
				AssignedOn = utcNow,
				DueOn = TrainingDueDates.DueForAssignment(training, utcNow, department),
				AssignmentSource = (int)source
			};

			var inserted = await _trainingUserRepository.InsertAssignmentAsync(row, ct);
			if (inserted != null)
				training.Users?.Add(inserted);
			return inserted;
		}

		/// <summary>Rules a scoped group admin may store: only named members of the group they administer.</summary>
		private static List<ContentAudienceRule> ScopeRules(List<ContentAudienceRule> rules, TrainingRightScope scope)
		{
			if (scope == null || scope.ManagesAll || scope.AdministeredMemberUserIds == null)
				return rules;

			return rules.Where(r => r.Kind == (int)ContentAudienceKinds.User && scope.CoversMember(r.RefId)).ToList();
		}

		public async Task<TrainingAssignmentOutcome> AssignAsync(Training training, IEnumerable<string> pickedUserIds, IEnumerable<IContentAudienceRule> rules,
			TrainingAssignmentSources source, TrainingRightScope assignScope, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var outcome = new TrainingAssignmentOutcome();
			if (training == null || training.TrainingId <= 0)
				return outcome;

			if (training.ClosedToNewAssignees || training.IsArchived)
			{
				outcome.Closed = true;
				return outcome;
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(training.DepartmentId, false);
			var rows = await RowsAsync(training);

			var picked = (pickedUserIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var candidates = new Dictionary<string, TrainingAssignmentSources>(StringComparer.OrdinalIgnoreCase);

			if (picked.Count > 0)
			{
				outcome.Skipped.AddRange(await _audienceService.ExplainSkippedAsync(training.DepartmentId, picked));
				var skipped = new HashSet<string>(outcome.Skipped.Select(s => s.UserId), StringComparer.OrdinalIgnoreCase);
				foreach (var userId in picked.Where(x => !skipped.Contains(x)))
					candidates[userId] = source == TrainingAssignmentSources.SelfEnrolled ? TrainingAssignmentSources.SelfEnrolled : TrainingAssignmentSources.Manual;
			}

			var ruleList = ContentAudienceMatcher.Normalize(rules);
			if (ruleList.Count > 0)
			{
				var resolution = await _audienceService.ResolveAsync(training.DepartmentId, ruleList);
				foreach (var skip in resolution.Skipped.Where(s => outcome.Skipped.All(o => !string.Equals(o.UserId, s.UserId, StringComparison.OrdinalIgnoreCase))))
					outcome.Skipped.Add(skip);
				foreach (var userId in resolution.MemberUserIds)
					if (!candidates.ContainsKey(userId))
						candidates[userId] = TrainingAssignmentSources.Audience;
			}

			foreach (var candidate in candidates)
			{
				if (assignScope != null && !assignScope.CoversMember(candidate.Key))
				{
					outcome.OutOfScope.Add(candidate.Key);
					continue;
				}

				var existing = Find(rows, candidate.Key);
				if (existing != null)
				{
					if (existing.RemovedOn.HasValue)
					{
						// Tracked again: a removed member who completed keeps the completion; anyone else starts afresh.
						existing.RemovedOn = null;
						existing.RemovedByUserId = null;
						var columns = new List<string> { "RemovedOn", "RemovedByUserId" };
						if (!existing.Complete)
						{
							existing.AssignedOn = utcNow;
							existing.DueOn = TrainingDueDates.DueForAssignment(training, utcNow, department);
							columns.AddRange(new[] { "AssignedOn", "DueOn" });
						}
						await _trainingUserRepository.UpdateColumnsAsync(existing, columns, cancellationToken);
						outcome.Added.Add(existing.UserId);
					}
					else
					{
						outcome.AlreadyAssigned.Add(existing.UserId);
					}

					continue;
				}

				if (await InsertAsync(training, department, candidate.Key, candidate.Value, utcNow, cancellationToken) != null)
					outcome.Added.Add(candidate.Key);
				else
					outcome.AlreadyAssigned.Add(candidate.Key);
			}

			return outcome;
		}

		public async Task<TrainingAssignmentOutcome> SaveAudienceAsync(Training training, IEnumerable<IContentAudienceRule> rules, string actingUserId,
			TrainingRightScope assignScope, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (training == null || training.TrainingId <= 0)
				return new TrainingAssignmentOutcome();

			var normalized = ScopeRules(ContentAudienceMatcher.Normalize(rules), assignScope);
			var rows = normalized.Select(r => new TrainingAudience
			{
				DepartmentId = training.DepartmentId,
				TrainingId = training.TrainingId,
				Kind = r.Kind,
				RefId = r.Kind == (int)ContentAudienceKinds.Everyone ? null : r.RefId,
				CreatedOn = utcNow,
				CreatedByUserId = actingUserId
			}).ToList();

			await _audiencesRepository.ReplaceForTrainingAsync(training.DepartmentId, training.TrainingId, rows, cancellationToken);
			training.Audiences = rows;

			if (rows.Count == 0)
				return new TrainingAssignmentOutcome();

			return await AssignAsync(training, null, normalized, TrainingAssignmentSources.Audience, assignScope, utcNow, cancellationToken);
		}

		public async Task<List<TrainingAudience>> GetAudienceAsync(int departmentId, int trainingId)
		{
			return await _audiencesRepository.GetForTrainingAsync(departmentId, trainingId) ?? new List<TrainingAudience>();
		}

		public async Task<TrainingAssignmentOutcome> PreviewAudienceAsync(Training training, IEnumerable<IContentAudienceRule> rules)
		{
			var outcome = new TrainingAssignmentOutcome();
			if (training == null)
				return outcome;

			var resolution = await _audienceService.ResolveAsync(training.DepartmentId, rules);
			var rows = training.TrainingId > 0 ? await RowsAsync(training) : new List<TrainingUser>();
			foreach (var userId in resolution.MemberUserIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
			{
				var existing = Find(rows, userId);
				if (existing != null && !existing.RemovedOn.HasValue)
					outcome.AlreadyAssigned.Add(userId);
				else
					outcome.Added.Add(userId);
			}

			outcome.Skipped = resolution.Skipped;
			outcome.Closed = training.ClosedToNewAssignees;
			return outcome;
		}

		public async Task<Tuple<int, int>> ReevaluateMemberAsync(int departmentId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			int added = 0, withdrawn = 0;
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return Tuple.Create(added, withdrawn);

			var byTraining = (await _audiencesRepository.GetForOpenTrainingsAsync(departmentId) ?? new List<TrainingAudience>()).GroupBy(a => a.TrainingId).ToList();
			if (byTraining.Count == 0)
				return Tuple.Create(added, withdrawn);

			// What the member belongs to, read once; inactive members match nothing, so their unopened rows are withdrawn.
			var facts = await _audienceService.GetMemberFactsAsync(departmentId, userId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			foreach (var group in byTraining)
			{
				var training = await _trainingRepository.GetTrainingByTrainingIdAsync(group.Key);
				if (training == null || training.DepartmentId != departmentId || training.IsArchived || training.ClosedToNewAssignees)
					continue;

				training.Users ??= new List<TrainingUser>();
				var row = Find(training.Users, userId);

				if (ContentAudienceMatcher.Matches(facts, group))
				{
					// A member an officer removed stays removed until an officer adds them again.
					if (row == null && await InsertAsync(training, department, facts.UserId ?? userId, TrainingAssignmentSources.Audience, utcNow, cancellationToken) != null)
						added++;
				}
				else if (CanWithdraw(row))
				{
					withdrawn += await _trainingUserRepository.DeleteAssignmentAsync(training.TrainingId, row.TrainingUserId, cancellationToken);
				}
			}

			return Tuple.Create(added, withdrawn);
		}

		public async Task<Tuple<int, int>> ReevaluateDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			int added = 0, withdrawn = 0;
			var byTraining = (await _audiencesRepository.GetForOpenTrainingsAsync(departmentId) ?? new List<TrainingAudience>()).GroupBy(a => a.TrainingId).ToList();
			if (byTraining.Count == 0)
				return Tuple.Create(added, withdrawn);

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			foreach (var group in byTraining)
			{
				var training = await _trainingRepository.GetTrainingByTrainingIdAsync(group.Key);
				if (training == null || training.DepartmentId != departmentId || training.IsArchived || training.ClosedToNewAssignees)
					continue;

				training.Users ??= new List<TrainingUser>();
				var resolution = await _audienceService.ResolveAsync(departmentId, group);

				foreach (var userId in resolution.MemberUserIds)
					if (Find(training.Users, userId) == null && await InsertAsync(training, department, userId, TrainingAssignmentSources.Audience, utcNow, cancellationToken) != null)
						added++;

				foreach (var row in training.Users.Where(r => CanWithdraw(r) && !resolution.MemberUserIds.Contains(r.UserId)).ToList())
					withdrawn += await _trainingUserRepository.DeleteAssignmentAsync(training.TrainingId, row.TrainingUserId, cancellationToken);
			}

			return Tuple.Create(added, withdrawn);
		}

		/// <summary>Moves a completed row's completion into the history.</summary>
		private async Task ArchiveCompletionAsync(Training training, TrainingUser row, TrainingCompletionHistoryReasons reason, string actingUserId, DateTime utcNow, CancellationToken ct)
		{
			if (row == null || !row.Complete || _historiesRepository == null)
				return;

			await _historiesRepository.InsertAsync(new TrainingCompletionHistory
			{
				DepartmentId = training.DepartmentId,
				TrainingId = training.TrainingId,
				UserId = row.UserId,
				Cycle = row.Cycle,
				AssignedOn = row.AssignedOn,
				DueOn = row.DueOn,
				ViewedOn = row.ViewedOn,
				CompletedOn = row.CompletedOn,
				Score = row.Score,
				Passed = row.Passed,
				Reason = (int)reason,
				RecordedOn = utcNow,
				RecordedByUserId = actingUserId
			}, ct, true);
		}

		/// <summary>Clears a row's progress for a new cycle with a fresh due date (the member is told again by the notifier).</summary>
		private static void ResetForNewCycle(Training training, TrainingUser row, Department department, DateTime utcNow, bool keepProgress)
		{
			if (!keepProgress)
			{
				row.Viewed = false;
				row.ViewedOn = null;
				row.Complete = false;
				row.CompletedOn = null;
				row.Score = 0;
				row.Passed = null;
				row.CompletionMethod = (int)LearningCompletionMethods.Self;
				row.RecordedCompletionId = null;
			}

			row.AttemptCount = 0;
			row.Cycle += 1;
			row.AssignedOn = utcNow;
			row.DueOn = TrainingDueDates.DueForAssignment(training, utcNow, department);
			row.NotifiedOn = null;
			row.RemindedOn = null;
			row.LastReminderLeadDays = null;
			row.OverdueNotifiedOn = null;
			row.OriginalDueOn = null;
			row.ExtensionReason = null;
			row.ExtendedByUserId = null;
			row.ExtendedOn = null;
		}

		public async Task<int> RunRepeatAsync(Training training, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (training == null || !training.RepeatMonths.HasValue || training.RepeatMonths.Value <= 0 || training.IsArchived)
				return 0;

			// The next run is written first, so a run that fails part-way is never repeated over the same members.
			var next = training.NextRepeatOn ?? utcNow;
			while (next <= utcNow)
				next = next.AddMonths(training.RepeatMonths.Value);
			await _trainingRepository.SetNextRepeatOnAsync(training.TrainingId, next, cancellationToken);
			training.NextRepeatOn = next;

			var department = await _departmentsService.GetDepartmentByIdAsync(training.DepartmentId, false);
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(training.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			training.Users = await _trainingUserRepository.GetByTrainingIdAsync(training.TrainingId) ?? new List<TrainingUser>();
			var count = 0;

			foreach (var row in training.Users.Where(r => r != null && !r.RemovedOn.HasValue && active.Contains(r.UserId ?? string.Empty)).ToList())
			{
				// The earlier completion stays on the member's record (TrainingCompletionHistories, for T1's transcript).
				await ArchiveCompletionAsync(training, row, TrainingCompletionHistoryReasons.Repeat, null, utcNow, cancellationToken);
				ResetForNewCycle(training, row, department, utcNow, keepProgress: !row.Complete);
				await _trainingUserRepository.UpdateColumnsAsync(row, ResetColumns, cancellationToken);
				count++;
			}

			// A dynamic training's current audience is assigned too (members who joined since the last run).
			var rules = await _audiencesRepository.GetForTrainingAsync(training.DepartmentId, training.TrainingId) ?? new List<TrainingAudience>();
			if (rules.Count > 0 && !training.ClosedToNewAssignees)
				count += (await AssignAsync(training, null, rules, TrainingAssignmentSources.Audience, null, utcNow, cancellationToken)).Added.Count;

			return count;
		}

		public async Task<int> ReassignCompletedAsync(Training training, string actingUserId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (training == null)
				return 0;

			var department = await _departmentsService.GetDepartmentByIdAsync(training.DepartmentId, false);
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(training.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var rows = await _trainingUserRepository.GetByTrainingIdAsync(training.TrainingId) ?? new List<TrainingUser>();
			var count = 0;

			foreach (var row in rows.Where(r => r != null && r.Complete && !r.RemovedOn.HasValue && active.Contains(r.UserId ?? string.Empty)))
			{
				await ArchiveCompletionAsync(training, row, TrainingCompletionHistoryReasons.MaterialEdit, actingUserId, utcNow, cancellationToken);
				ResetForNewCycle(training, row, department, utcNow, keepProgress: false);
				await _trainingUserRepository.UpdateColumnsAsync(row, ResetColumns, cancellationToken);
				count++;
			}

			training.Users = rows;
			return count;
		}

		public async Task<TrainingUser> SelfEnrollAsync(int departmentId, int trainingId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId || training.IsArchived || !training.HasStarted(utcNow) ||
				!training.OpenToAllMembers || !training.AllowSelfEnroll || training.ClosedToNewAssignees)
				return null;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId);
			if (!DepartmentMemberStateHelper.IsActiveMember(member, departmentId))
				return null;

			training.Users ??= new List<TrainingUser>();
			var existing = Find(training.Users, userId);
			if (existing != null && !existing.RemovedOn.HasValue)
				return existing;

			var outcome = await AssignAsync(training, new[] { member.UserId }, null, TrainingAssignmentSources.SelfEnrolled, null, utcNow, cancellationToken);
			return outcome.Added.Count > 0 ? Find(training.Users, userId) ?? await _trainingUserRepository.GetTrainingUserByTrainingIdAndUserIdAsync(trainingId, member.UserId) : null;
		}

		private async Task<(Training Training, TrainingUser Row)> LoadAsync(int departmentId, int trainingId, string userId)
		{
			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return (null, null);

			training.Users ??= new List<TrainingUser>();
			return (training, Find(training.Users, userId));
		}

		public async Task<bool> RemoveAssigneeAsync(int departmentId, int trainingId, string userId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var (training, row) = await LoadAsync(departmentId, trainingId, userId);
			if (row == null || row.RemovedOn.HasValue)
				return false;

			var before = JsonConvert.SerializeObject(row);
			if (row.Complete)
			{
				// T-D15: someone who completed keeps the completion; they are only no longer tracked.
				row.RemovedOn = DateTime.UtcNow;
				row.RemovedByUserId = actingUserId;
				await _trainingUserRepository.UpdateColumnsAsync(row, new[] { "RemovedOn", "RemovedByUserId" }, cancellationToken);
			}
			else
			{
				await _trainingUserRepository.DeleteAssignmentAsync(trainingId, row.TrainingUserId, cancellationToken);
			}

			Audit(departmentId, actingUserId, AuditLogTypes.TrainingAssigneeRemoved, before, JsonConvert.SerializeObject(new { training.TrainingId, row.UserId, Kept = row.Complete }));
			return true;
		}

		public async Task<TrainingUser> ExtendAsync(int departmentId, int trainingId, string userId, DateTime newDueLocalDate, string reason, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			var (training, row) = await LoadAsync(departmentId, trainingId, userId);
			if (row == null || row.RemovedOn.HasValue || row.Complete || string.IsNullOrWhiteSpace(reason))
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var newDue = TrainingDueDates.EndOfLocalDayUtc(newDueLocalDate, department);
			if (newDue <= DateTime.UtcNow)
				return null;

			var before = JsonConvert.SerializeObject(row);
			row.OriginalDueOn ??= row.DueOn ?? TrainingDueDates.FixedDueUtc(training, department);
			row.DueOn = newDue;
			row.ExtensionReason = Trim(reason, 500);
			row.ExtendedByUserId = actingUserId;
			row.ExtendedOn = DateTime.UtcNow;
			// The new due date gets its own reminders and overdue notice.
			row.RemindedOn = null;
			row.LastReminderLeadDays = null;
			row.OverdueNotifiedOn = null;

			await _trainingUserRepository.UpdateColumnsAsync(row, new[] { "OriginalDueOn", "DueOn", "ExtensionReason", "ExtendedByUserId", "ExtendedOn", "RemindedOn", "LastReminderLeadDays", "OverdueNotifiedOn" }, cancellationToken);
			Audit(departmentId, actingUserId, AuditLogTypes.TrainingAssignmentExtended, before, JsonConvert.SerializeObject(row));
			return row;
		}

		public async Task<TrainingUser> ExemptAsync(int departmentId, int trainingId, string userId, string reason, DateTime? untilLocalDate, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			var (_, row) = await LoadAsync(departmentId, trainingId, userId);
			if (row == null || row.RemovedOn.HasValue || row.Complete || string.IsNullOrWhiteSpace(reason))
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var until = untilLocalDate.HasValue ? TrainingDueDates.EndOfLocalDayUtc(untilLocalDate.Value, department) : (DateTime?)null;
			if (until.HasValue && until.Value <= DateTime.UtcNow)
				return null;

			var before = JsonConvert.SerializeObject(row);
			row.ExemptOn = DateTime.UtcNow;
			row.ExemptReason = Trim(reason, 500);
			row.ExemptByUserId = actingUserId;
			row.ExemptUntil = until;

			await _trainingUserRepository.UpdateColumnsAsync(row, new[] { "ExemptOn", "ExemptReason", "ExemptByUserId", "ExemptUntil" }, cancellationToken);
			Audit(departmentId, actingUserId, AuditLogTypes.TrainingAssignmentExempted, before, JsonConvert.SerializeObject(row));
			return row;
		}

		public async Task<TrainingUser> ClearExemptionAsync(int departmentId, int trainingId, string userId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var (_, row) = await LoadAsync(departmentId, trainingId, userId);
			if (row == null || !row.ExemptOn.HasValue)
				return null;

			var before = JsonConvert.SerializeObject(row);
			row.ExemptOn = null;
			row.ExemptReason = null;
			row.ExemptByUserId = null;
			row.ExemptUntil = null;

			await _trainingUserRepository.UpdateColumnsAsync(row, new[] { "ExemptOn", "ExemptReason", "ExemptByUserId", "ExemptUntil" }, cancellationToken);
			Audit(departmentId, actingUserId, AuditLogTypes.TrainingAssignmentExempted, before, JsonConvert.SerializeObject(row));
			return row;
		}

		public async Task<int> RescheduleOpenAssignmentsAsync(Training training, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (training == null || training.TrainingId <= 0)
				return 0;

			var department = await _departmentsService.GetDepartmentByIdAsync(training.DepartmentId, false);
			var rows = await _trainingUserRepository.GetByTrainingIdAsync(training.TrainingId) ?? new List<TrainingUser>();
			var moved = 0;

			foreach (var row in rows.Where(r => r != null && !r.Complete && !r.RemovedOn.HasValue && !r.ExtendedOn.HasValue))
			{
				var due = TrainingDueDates.DueForAssignment(training, row.AssignedOn ?? utcNow, department);
				if (due == row.DueOn)
					continue;

				row.DueOn = due;
				row.RemindedOn = null;
				row.LastReminderLeadDays = null;
				row.OverdueNotifiedOn = null;
				await _trainingUserRepository.UpdateColumnsAsync(row, new[] { "DueOn", "RemindedOn", "LastReminderLeadDays", "OverdueNotifiedOn" }, cancellationToken);
				moved++;
			}

			return moved;
		}

		public async Task<TrainingUser> ResetMemberAsync(int departmentId, int trainingId, string userId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var (training, row) = await LoadAsync(departmentId, trainingId, userId);
			if (row == null)
				return null;

			var now = DateTime.UtcNow;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			await ArchiveCompletionAsync(training, row, TrainingCompletionHistoryReasons.Reset, actingUserId, now, cancellationToken);

			var notifiedOn = row.NotifiedOn;
			ResetForNewCycle(training, row, department, now, keepProgress: false);
			// A reset is not a new assignment: the member is not announced to again.
			row.NotifiedOn = notifiedOn;
			row.RemovedOn = null;
			row.RemovedByUserId = null;

			await _trainingUserRepository.UpdateColumnsAsync(row, ResetColumns.Concat(new[] { "RemovedOn", "RemovedByUserId" }).ToArray(), cancellationToken);
			return row;
		}

		private static string Trim(string value, int max)
		{
			var trimmed = value?.Trim();
			return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_eventAggregator?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true
			});
		}
	}
}
