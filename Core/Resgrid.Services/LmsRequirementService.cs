using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The learning requirements engine (Training T3 items 1, 2, 3, 5 and 6; LMS, §17.2): requirements and their audiences, each member's
	/// due date from the moment they entered the audience (join, role add, any audience entry through worker 83's queue, or worker 93),
	/// grace periods, leave pauses (R-C2), exemptions and extensions, the compliance matrix, onboarding auto-assignment, workflow events
	/// 224-229 and notices 61-63, and optional training-backed enforcement with the blast-radius guard (R-L11): an overdue refresher can
	/// expire the certification it backs, and the certification module's own enforcement then removes roles after its grace; one run never
	/// thins a role by more than the configured share, and every action is undone when the member completes the requirement.
	/// <para>
	/// Gate (§17.3, R-L9): authoring and new assignments need the add-on; the engine asks the gate once per department per run, and only
	/// for departments with due work. A member without a licensed seat is not licensed, never overdue. During a lapse the engine pauses
	/// (no new overdue states, no enforcement, rule 5) and reverts open enforcement (rule 10); on reactivation due dates are re-based by
	/// F's <see cref="ILmsReactivationService"/> rule (rule 12). Inactive members never count.
	/// </para>
	/// </summary>
	public class LmsRequirementService : ILmsRequirementService
	{
		public const string SystemUserId = "system";

		/// <summary>The certification status reason an enforcement leaves on a record it expired (and the undo checks).</summary>
		public const string EnforcementReasonPrefix = "learning-requirement:";

		private readonly ILmsRequirementsRepository _requirements;
		private readonly ILmsRequirementAudiencesRepository _audiences;
		private readonly ILmsRequirementMembersRepository _members;
		private readonly ILmsRequirementEnforcementsRepository _enforcements;
		private readonly ILmsRequirementFactsRepository _facts;
		private readonly ILmsRequirementDepartmentStatesRepository _departmentStates;
		private readonly ILmsMemberSeatsRepository _seats;
		private readonly ILmsPathsRepository _pathsRepository;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsAccessService _access;
		private readonly ILmsReactivationService _reactivation;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsPathService _paths;
		private readonly ILmsAcademyService _academy;
		private readonly IContentAudienceService _audienceService;
		private readonly IMemberLeaveService _leave;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly ICertificationService _certifications;
		private readonly IPersonnelRolesService _roles;
		private readonly ITrainingService _trainings;
		private readonly ILearningRecordService _learningRecords;
		private readonly ILmsRequirementNotifier _notifier;
		private readonly IEventAggregator _eventAggregator;

		public LmsRequirementService(ILmsRequirementsRepository requirements, ILmsRequirementAudiencesRepository audiences, ILmsRequirementMembersRepository members,
			ILmsRequirementEnforcementsRepository enforcements, ILmsRequirementFactsRepository facts, ILmsRequirementDepartmentStatesRepository departmentStates,
			ILmsMemberSeatsRepository seats, ILmsPathsRepository pathsRepository,
			ILmsCoursesRepository courses, ILmsAccessService access, ILmsReactivationService reactivation, ILmsLearnerService learner, ILmsPathService paths,
			ILmsAcademyService academy, IContentAudienceService audienceService, IMemberLeaveService leave, IDepartmentsService departments,
			IDepartmentSettingsService settings, ICertificationService certifications, IPersonnelRolesService roles, ITrainingService trainings,
			ILearningRecordService learningRecords, ILmsRequirementNotifier notifier, IEventAggregator eventAggregator)
		{
			_requirements = requirements;
			_audiences = audiences;
			_members = members;
			_enforcements = enforcements;
			_facts = facts;
			_departmentStates = departmentStates;
			_seats = seats;
			_pathsRepository = pathsRepository;
			_courses = courses;
			_access = access;
			_reactivation = reactivation;
			_learner = learner;
			_paths = paths;
			_academy = academy;
			_audienceService = audienceService;
			_leave = leave;
			_departments = departments;
			_settings = settings;
			_certifications = certifications;
			_roles = roles;
			_trainings = trainings;
			_learningRecords = learningRecords;
			_notifier = notifier;
			_eventAggregator = eventAggregator;
		}

		#region Settings

		public async Task<LearningRequirementSettings> GetSettingsAsync(int departmentId)
		{
			try
			{
				var row = await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.LearningRequirementSettings);
				if (row != null && !string.IsNullOrWhiteSpace(row.Setting))
					return (ObjectSerialization.Deserialize<LearningRequirementSettings>(row.Setting) ?? new LearningRequirementSettings()).Normalized();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The learning requirement settings of department {departmentId} could not be read.");
			}

			return new LearningRequirementSettings().Normalized();
		}

		public async Task<LmsResult> SaveSettingsAsync(LmsActor actor, LearningRequirementSettings settings, CancellationToken cancellationToken = default)
		{
			var refused = RefuseAuthoring(actor);
			if (refused != null)
				return refused;
			if (settings == null)
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var before = await GetSettingsAsync(actor.DepartmentId);
			var normalized = settings.Normalized();
			await _settings.SaveOrUpdateSettingAsync(actor.DepartmentId, ObjectSerialization.Serialize(normalized), DepartmentSettingTypes.LearningRequirementSettings, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LearningRequirementSettingsChanged, before, normalized);

			// Turning enforcement off strands nothing: every open action is undone now.
			if (before.EnforcementEnabled && !normalized.EnforcementEnabled)
				await UndoAllAsync(actor.DepartmentId, LmsEnforcementUndoReasons.NoLongerApplies, DateTime.UtcNow, cancellationToken);

			return new LmsResult { Value = normalized };
		}

		private static LmsResult RefuseAuthoring(LmsActor actor)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (actor.Gate == null || !actor.Gate.CanAuthor)
				return LmsResult.Fail(LmsErrors.AddonRequired);
			return null;
		}

		#endregion Settings

		#region Authoring

		public async Task<List<LmsRequirementRow>> GetRequirementsAsync(LmsActor actor, DateTime utcNow)
		{
			var rows = new List<LmsRequirementRow>();
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.FlagOn)
				return rows;

			var snapshot = await LoadAsync(actor.DepartmentId, await SubscriptionForViewAsync(actor), utcNow, includeFacts: true);
			foreach (var requirement in await _requirements.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsRequirement>())
			{
				var row = new LmsRequirementRow
				{
					Requirement = requirement,
					Audience = snapshot.Audiences.TryGetValue(requirement.LmsRequirementId, out var rules) ? rules : new List<LmsRequirementAudience>(),
					TargetName = snapshot.TargetNames.TryGetValue(requirement.LmsRequirementId, out var name) ? name : null
				};
				if (requirement.IsLive)
					foreach (var member in snapshot.Members.Where(m => m.LmsRequirementId == requirement.LmsRequirementId && !m.NotRequired && snapshot.Active.Contains(m.UserId)))
					{
						var state = Evaluate(snapshot, requirement, member).State;
						row.Counts[state] = row.Counts.TryGetValue(state, out var count) ? count + 1 : 1;
					}
				rows.Add(row);
			}

			return rows;
		}

		public Task<LmsRequirement> GetRequirementAsync(int departmentId, int lmsRequirementId) => _requirements.GetByIdForDepartmentAsync(departmentId, lmsRequirementId);

		public async Task<List<LmsRequirementAudience>> GetAudienceAsync(int departmentId, int lmsRequirementId) =>
			(await _audiences.GetForDepartmentAsync(departmentId) ?? new List<LmsRequirementAudience>()).Where(a => a.LmsRequirementId == lmsRequirementId).ToList();

		public async Task<LmsResult> SaveRequirementAsync(LmsActor actor, LmsRequirementInput input, CancellationToken cancellationToken = default)
		{
			var refused = RefuseAuthoring(actor);
			if (refused != null)
				return refused;

			var departmentId = actor.DepartmentId;
			LmsRequirement existing = null;
			if (input != null && input.LmsRequirementId > 0)
			{
				existing = await _requirements.GetByIdForDepartmentAsync(departmentId, input.LmsRequirementId);
				if (existing == null || existing.DeletedOn.HasValue)
					return LmsResult.Fail(LmsErrors.NotFound);
			}

			var settings = await GetSettingsAsync(departmentId);
			var (requirement, problem) = await ValidateAsync(departmentId, input, existing, settings);
			if (problem != null)
				return new LmsResult { Error = LmsErrors.InvalidInput, Value = problem };

			var now = DateTime.UtcNow;
			var before = existing == null ? null : JsonConvert.SerializeObject(existing);
			if (existing == null)
			{
				requirement.CreatedOn = now;
				requirement.CreatedByUserId = actor.UserId;
			}
			else
			{
				requirement.UpdatedOn = now;
				requirement.UpdatedByUserId = actor.UserId;
			}

			var saved = await _requirements.SaveOrUpdateAsync(requirement, cancellationToken, true);
			await _audiences.ReplaceAsync(departmentId, saved.LmsRequirementId, input.Audience, cancellationToken);
			Audit(departmentId, actor.UserId, existing == null ? AuditLogTypes.LearningRequirementCreated : AuditLogTypes.LearningRequirementChanged, before, saved);

			// Enforcement switched off on this requirement (or it stopped backing a certification): its open actions are undone now.
			if (existing != null && existing.EnforcementEnabled && (!saved.EnforcementEnabled || !saved.IsActive || saved.BacksCertificationTypeId != existing.BacksCertificationTypeId))
				await UndoForRequirementAsync(departmentId, saved.LmsRequirementId, LmsEnforcementUndoReasons.NoLongerApplies, now, cancellationToken);

			// The requirement applies at once: its members get their rows (and onboarding assignments) without waiting for worker 93.
			if (saved.IsLive)
			{
				try
				{
					var subscription = await _access.GetSubscriptionAsync(departmentId);
					var snapshot = await LoadAsync(departmentId, subscription, now, includeFacts: true);
					await ReconcileAsync(snapshot, null, existing == null ? saved.LmsRequirementId : (int?)null, GateFor(departmentId, now), now, new LmsRequirementRunResult(), cancellationToken);
				}
				catch (Exception ex)
				{
					// Worker 93 applies it on its next pass; the save stands.
					Logging.LogException(ex, $"Learning requirement {saved.LmsRequirementId} could not be applied to its audience at once.");
				}
			}

			return new LmsResult { Value = saved, Count = 1 };
		}

		/// <summary>Builds the stored requirement from the input; the second value names the first problem (a resx key), or null.</summary>
		private async Task<(LmsRequirement Requirement, string Problem)> ValidateAsync(int departmentId, LmsRequirementInput input, LmsRequirement existing, LearningRequirementSettings settings)
		{
			if (input == null)
				return (null, "Err.Invalid");
			var name = input.Name?.Trim();
			if (string.IsNullOrWhiteSpace(name))
				return (null, "Err.NameRequired");
			if (name.Length > LmsRequirement.MaxNameLength)
				name = name.Substring(0, LmsRequirement.MaxNameLength);
			if (!Enum.IsDefined(typeof(LmsRequirementKinds), input.Kind) || !Enum.IsDefined(typeof(LmsRequirementRecurrences), input.Recurrence))
				return (null, "Err.Invalid");

			var requirement = existing ?? new LmsRequirement { DepartmentId = departmentId };
			requirement.Name = name;
			requirement.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
			requirement.Kind = (int)input.Kind;
			requirement.TargetRefId = null;
			requirement.TargetCode = null;
			requirement.RequiredHours = null;
			requirement.RequiredCount = null;

			switch (input.Kind)
			{
				case LmsRequirementKinds.Course:
					var course = input.TargetRefId.HasValue ? await _courses.GetVisibleByIdAsync(departmentId, input.TargetRefId.Value) : null;
					if (course == null || course.IsDeleted)
						return (null, "Err.TargetRequired");
					requirement.TargetRefId = course.LmsCourseId;
					break;
				case LmsRequirementKinds.Training:
					var training = input.TargetRefId.HasValue ? await _trainings.GetTrainingByIdAsync(input.TargetRefId.Value) : null;
					if (training == null || training.DepartmentId != departmentId)
						return (null, "Err.TargetRequired");
					requirement.TargetRefId = training.TrainingId;
					break;
				case LmsRequirementKinds.CategoryHours:
					var categories = await _learningRecords.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>();
					if (!input.TargetRefId.HasValue || !categories.Any(c => c.TrainingCategoryId == input.TargetRefId.Value && !c.IsDeleted))
						return (null, "Err.TargetRequired");
					if (!input.RequiredHours.HasValue || input.RequiredHours.Value <= 0m || input.RequiredHours.Value > 10000m)
						return (null, "Err.HoursRequired");
					requirement.TargetRefId = input.TargetRefId;
					requirement.RequiredHours = Math.Round(input.RequiredHours.Value, 2);
					break;
				case LmsRequirementKinds.Path:
					var path = input.TargetRefId.HasValue ? await _pathsRepository.GetVisibleByIdAsync(departmentId, input.TargetRefId.Value) : null;
					if (path == null || path.IsDeleted)
						return (null, "Err.TargetRequired");
					requirement.TargetRefId = path.LmsPathId;
					break;
				case LmsRequirementKinds.Evaluations:
					var code = input.TargetCode?.Trim();
					if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
						return (null, "Err.SkillRequired");
					if (!input.RequiredCount.HasValue || input.RequiredCount.Value < 1 || input.RequiredCount.Value > 1000)
						return (null, "Err.CountRequired");
					requirement.TargetCode = code;
					requirement.RequiredCount = input.RequiredCount;
					break;
			}

			requirement.Recurrence = (int)input.Recurrence;
			requirement.IntervalMonths = null;
			if (input.Recurrence == LmsRequirementRecurrences.RollingMonths)
			{
				if (!input.IntervalMonths.HasValue || input.IntervalMonths.Value < 1 || input.IntervalMonths.Value > 120)
					return (null, "Err.IntervalRequired");
				requirement.IntervalMonths = input.IntervalMonths;
			}
			else if (input.Recurrence == LmsRequirementRecurrences.CalendarYear && input.Kind == LmsRequirementKinds.Path)
				return (null, "Err.Invalid");

			if (input.DueDays < 0 || input.DueDays > 3650 || input.GraceDays < 0 || input.GraceDays > 365 || (input.AtRiskDays.HasValue && (input.AtRiskDays < 0 || input.AtRiskDays > 365)))
				return (null, "Err.DaysRange");
			requirement.DueDays = input.DueDays;
			requirement.GraceDays = input.GraceDays;
			requirement.AtRiskDays = input.AtRiskDays ?? settings.DefaultAtRiskDays;
			requirement.NewMembersOnly = input.NewMembersOnly;
			requirement.IsOnboarding = input.IsOnboarding;
			requirement.AutoAssign = input.AutoAssign && (input.Kind == LmsRequirementKinds.Course || input.Kind == LmsRequirementKinds.Path);
			requirement.IsActive = input.IsActive;
			// The department's choice for a major Academy update of a lesson it requires (A2 release rule): a Course requirement, or a Path
			// requirement for the lessons on the path.
			requirement.AcademyUpdatePolicy = (input.Kind == LmsRequirementKinds.Course || input.Kind == LmsRequirementKinds.Path) &&
				Enum.IsDefined(typeof(LmsMajorChangePolicies), input.AcademyUpdatePolicy)
				? (int)input.AcademyUpdatePolicy
				: (int)LmsMajorChangePolicies.Notify;

			requirement.BacksCertificationTypeId = null;
			requirement.EnforcementEnabled = false;
			if (input.BacksCertificationTypeId.HasValue && input.BacksCertificationTypeId.Value > 0)
			{
				// Training-backed enforcement expires a certification, so the type must be a person type that expires (the role grace runs from
				// the expiry the enforcement sets).
				var type = await _certifications.GetCertificationTypeByIdAsync(input.BacksCertificationTypeId.Value);
				if (type == null || type.DepartmentId != departmentId || type.IsDeleted || !type.IsActive || type.AppliesTo != (int)CertificationAppliesTo.Person || type.NeverExpires)
					return (null, "Err.CertificationType");
				requirement.BacksCertificationTypeId = type.DepartmentCertificationTypeId;
				requirement.EnforcementEnabled = input.EnforcementEnabled;
			}

			requirement.AlertSupervisors = input.AlertSupervisors;
			requirement.AlertOnCompletion = input.AlertOnCompletion;
			var selectable = new HashSet<string>((await _departments.GetSelectablePersonnelNamesAsync(departmentId) ?? new List<PersonName>()).Select(p => p.UserId),
				StringComparer.OrdinalIgnoreCase);
			var alertIds = (input.AlertUserIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id) && selectable.Contains(id.Trim())).Select(id => id.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
			requirement.AlertUserIds = alertIds.Count == 0 ? null : string.Join(",", alertIds);

			if (ContentAudienceMatcher.Normalize(input.Audience).Count == 0)
				return (null, "Err.AudienceRequired");

			return (requirement, null);
		}

		public async Task<LmsResult> DeleteRequirementAsync(LmsActor actor, int lmsRequirementId, CancellationToken cancellationToken = default)
		{
			var refused = RefuseAuthoring(actor);
			if (refused != null)
				return refused;

			var requirement = await _requirements.GetByIdForDepartmentAsync(actor.DepartmentId, lmsRequirementId);
			if (requirement == null || requirement.DeletedOn.HasValue)
				return LmsResult.Fail(LmsErrors.NotFound);

			var now = DateTime.UtcNow;
			requirement.DeletedOn = now;
			requirement.DeletedByUserId = actor.UserId;
			requirement.IsActive = false;
			await _requirements.SaveOrUpdateAsync(requirement, cancellationToken, true);
			await UndoForRequirementAsync(actor.DepartmentId, lmsRequirementId, LmsEnforcementUndoReasons.NoLongerApplies, now, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LearningRequirementDeleted, null, new { requirement.LmsRequirementId, requirement.Name });
			return new LmsResult { Count = 1 };
		}

		#endregion Authoring

		#region Snapshot and evaluation

		/// <summary>Everything one department's evaluation reads, loaded once.</summary>
		internal sealed class Snapshot
		{
			public int DepartmentId { get; set; }
			public Department Department { get; set; }
			public DateTime UtcNow { get; set; }
			public List<LmsRequirement> Requirements { get; set; } = new List<LmsRequirement>();
			public Dictionary<int, List<LmsRequirementAudience>> Audiences { get; set; } = new Dictionary<int, List<LmsRequirementAudience>>();
			public List<LmsRequirementMember> Members { get; set; } = new List<LmsRequirementMember>();
			public HashSet<string> Active { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public HashSet<string> Licensed { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public Dictionary<string, List<MemberLeavePeriod>> Leaves { get; set; } = new Dictionary<string, List<MemberLeavePeriod>>(StringComparer.OrdinalIgnoreCase);
			public List<LmsAddonLapse> Lapses { get; set; } = new List<LmsAddonLapse>();
			public DateTime? OpenLapseSinceUtc { get; set; }
			public bool Entitled { get; set; }
			public Dictionary<int, ILookup<string, LmsRequirementFact>> Facts { get; set; } = new Dictionary<int, ILookup<string, LmsRequirementFact>>();
			public Dictionary<int, string> TargetNames { get; set; } = new Dictionary<int, string>();
		}

		/// <summary>The subscription a page reads (packs for the licensed seats, the lapse start): one billing read per request.</summary>
		private Task<LmsSubscriptionSnapshot> SubscriptionForViewAsync(LmsActor actor) => _access.GetSubscriptionAsync(actor.DepartmentId);

		internal async Task<Snapshot> LoadAsync(int departmentId, LmsSubscriptionSnapshot subscription, DateTime utcNow, bool includeFacts)
		{
			subscription ??= LmsSubscriptionSnapshot.Unknown;
			var snapshot = new Snapshot { DepartmentId = departmentId, UtcNow = utcNow, Entitled = subscription.IsEntitled };
			snapshot.Department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			snapshot.Requirements = (await _requirements.GetForDepartmentAsync(departmentId) ?? new List<LmsRequirement>()).Where(r => r.IsLive).ToList();
			snapshot.Audiences = (await _audiences.GetForDepartmentAsync(departmentId) ?? new List<LmsRequirementAudience>())
				.GroupBy(a => a.LmsRequirementId).ToDictionary(g => g.Key, g => g.ToList());
			snapshot.Members = await _members.GetOpenForDepartmentAsync(departmentId) ?? new List<LmsRequirementMember>();
			snapshot.Active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);

			var seats = await _seats.GetForDepartmentAsync(departmentId) ?? new List<LmsMemberSeat>();
			snapshot.Licensed = subscription.IsEntitled
				? LmsSeatRules.Licensed(seats, snapshot.Active, subscription.Packs * LmsAddonConfig.PackSize, true)
				// Paused (lapsed): who held a seat, so the matrix shows the paused states rather than "not licensed" for everyone.
				: new HashSet<string>(LmsSeatRules.CountedSeats(seats, snapshot.Active).Select(s => s.UserId), StringComparer.OrdinalIgnoreCase);
			snapshot.OpenLapseSinceUtc = subscription.IsEntitled ? (DateTime?)null : subscription.LapsedSinceUtc ?? utcNow;

			snapshot.Leaves = await _leave.GetByMemberAsync(departmentId) ?? new Dictionary<string, List<MemberLeavePeriod>>(StringComparer.OrdinalIgnoreCase);
			snapshot.Lapses = await _reactivation.GetLapsesAsync(departmentId) ?? new List<LmsAddonLapse>();

			if (includeFacts)
				await LoadFactsAsync(snapshot);
			await LoadTargetNamesAsync(snapshot);
			return snapshot;
		}

		private async Task LoadFactsAsync(Snapshot snapshot)
		{
			var departmentId = snapshot.DepartmentId;
			var byKind = snapshot.Requirements.GroupBy(r => (LmsRequirementKinds)r.Kind).ToDictionary(g => g.Key, g => g.ToList());
			var all = new Dictionary<int, List<LmsRequirementFact>>();

			if (byKind.TryGetValue(LmsRequirementKinds.Course, out var courses))
			{
				var facts = await _facts.GetCourseCompletionsAsync(departmentId, courses.Where(r => r.TargetRefId.HasValue).Select(r => r.TargetRefId.Value).Distinct()) ?? new List<LmsRequirementFact>();
				foreach (var r in courses)
					all[r.LmsRequirementId] = facts.Where(f => f.RefId == r.TargetRefId).ToList();
			}

			if (byKind.TryGetValue(LmsRequirementKinds.Training, out var trainings))
			{
				var facts = await _facts.GetTrainingCompletionsAsync(departmentId, trainings.Where(r => r.TargetRefId.HasValue).Select(r => r.TargetRefId.Value).Distinct()) ?? new List<LmsRequirementFact>();
				foreach (var r in trainings)
					all[r.LmsRequirementId] = facts.Where(f => f.RefId == r.TargetRefId).ToList();
			}

			if (byKind.TryGetValue(LmsRequirementKinds.CategoryHours, out var hours))
			{
				// One read from the earliest moment any of them looks back to.
				var from = hours.Select(r => From(r, snapshot)).Min();
				var facts = await _facts.GetCategoryHoursAsync(departmentId, hours.Where(r => r.TargetRefId.HasValue).Select(r => r.TargetRefId.Value).Distinct(), from) ?? new List<LmsRequirementFact>();
				foreach (var r in hours)
					all[r.LmsRequirementId] = facts.Where(f => f.RefId == r.TargetRefId).ToList();
			}

			if (byKind.TryGetValue(LmsRequirementKinds.Path, out var paths))
			{
				var facts = await _paths.GetCompletionFactsAsync(departmentId, paths.Where(r => r.TargetRefId.HasValue).Select(r => r.TargetRefId.Value).Distinct()) ?? new List<LmsRequirementFact>();
				foreach (var r in paths)
					all[r.LmsRequirementId] = facts.Where(f => f.RefId == r.TargetRefId).ToList();
			}

			if (byKind.TryGetValue(LmsRequirementKinds.Evaluations, out var evaluations))
			{
				var facts = await _facts.GetEvaluationsAsync(departmentId, evaluations.Select(r => r.TargetCode)) ?? new List<LmsRequirementFact>();
				foreach (var r in evaluations)
					all[r.LmsRequirementId] = facts.Where(f => string.Equals(f.Code, r.TargetCode, StringComparison.OrdinalIgnoreCase)).ToList();
			}

			snapshot.Facts = all.ToDictionary(p => p.Key, p => p.Value.Where(f => !string.IsNullOrWhiteSpace(f.UserId)).ToLookup(f => f.UserId, StringComparer.OrdinalIgnoreCase));
		}

		/// <summary>How far back a counted requirement's records matter.</summary>
		private static DateTime From(LmsRequirement requirement, Snapshot snapshot)
		{
			switch ((LmsRequirementRecurrences)requirement.Recurrence)
			{
				case LmsRequirementRecurrences.CalendarYear:
					return LmsRequirementEvaluator.Year(snapshot.UtcNow, snapshot.Department).Start.AddDays(-1);
				case LmsRequirementRecurrences.RollingMonths:
					// Twice the window: a lapse date needs the items that made up the amount before the window.
					return snapshot.UtcNow.AddMonths(-2 * Math.Max(1, requirement.IntervalMonths ?? 12));
				default:
					return new DateTime(1900, 1, 1);
			}
		}

		private async Task LoadTargetNamesAsync(Snapshot snapshot)
		{
			var departmentId = snapshot.DepartmentId;
			var requirements = await _requirements.GetForDepartmentAsync(departmentId) ?? new List<LmsRequirement>();
			var courseIds = requirements.Where(r => r.Kind == (int)LmsRequirementKinds.Course && r.TargetRefId.HasValue).Select(r => r.TargetRefId.Value).Distinct().ToList();
			var courses = courseIds.Count == 0 ? new List<LmsCourse>() : await _courses.GetVisibleByIdsAsync(departmentId, courseIds) ?? new List<LmsCourse>();
			var categories = requirements.Any(r => r.Kind == (int)LmsRequirementKinds.CategoryHours)
				? await _learningRecords.GetCategoriesAsync(departmentId, true) ?? new List<TrainingCategory>()
				: new List<TrainingCategory>();

			foreach (var r in requirements)
			{
				string name = null;
				try
				{
					switch ((LmsRequirementKinds)r.Kind)
					{
						case LmsRequirementKinds.Course:
							var course = courses.FirstOrDefault(c => c.LmsCourseId == r.TargetRefId);
							name = course == null ? null : course.IsSystem ? _academy.Text(AcademyCourseManifest.LessonIdFromSystemKey(course.SystemKey) + ".Title", "en") : course.Title;
							break;
						case LmsRequirementKinds.Training:
							name = r.TargetRefId.HasValue ? (await _trainings.GetTrainingByIdAsync(r.TargetRefId.Value))?.Name : null;
							break;
						case LmsRequirementKinds.CategoryHours:
							name = categories.FirstOrDefault(c => c.TrainingCategoryId == r.TargetRefId)?.Name;
							break;
						case LmsRequirementKinds.Path:
							var path = r.TargetRefId.HasValue ? await _pathsRepository.GetVisibleByIdAsync(departmentId, r.TargetRefId.Value) : null;
							name = path == null ? null : _paths.TitleOf(path, "en");
							break;
						case LmsRequirementKinds.Evaluations:
							name = r.TargetCode;
							break;
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"The target of learning requirement {r.LmsRequirementId} could not be named.");
				}

				snapshot.TargetNames[r.LmsRequirementId] = name ?? r.Name;
			}
		}

		private static LmsRequirementStatus Evaluate(Snapshot snapshot, LmsRequirement requirement, LmsRequirementMember member)
		{
			var facts = snapshot.Facts.TryGetValue(requirement.LmsRequirementId, out var lookup) ? lookup[member.UserId] : Enumerable.Empty<LmsRequirementFact>();
			return LmsRequirementEvaluator.Evaluate(requirement, member, facts, new LmsRequirementContext
			{
				Department = snapshot.Department,
				UtcNow = snapshot.UtcNow,
				Licensed = snapshot.Licensed.Contains(member.UserId),
				Leaves = snapshot.Leaves.TryGetValue(member.UserId, out var leaves) ? leaves : null,
				Lapses = snapshot.Lapses,
				ReactivationGrace = TimeSpan.FromDays(Math.Max(0, LmsAddonConfig.ReactivationGraceDays)),
				OpenLapseSinceUtc = snapshot.OpenLapseSinceUtc
			});
		}

		/// <summary>The gate a run acts under: it asked billing once and the add-on is active.</summary>
		private static LmsDepartmentGate GateFor(int departmentId, DateTime utcNow) =>
			new LmsDepartmentGate { DepartmentId = departmentId, ModuleOn = true, FlagOn = true, AddonActive = true, LastEntitledOn = utcNow };

		/// <summary>The engine assigns as the department (the requirement's author is recorded as the assigner).</summary>
		private static LmsActor SystemActor(int departmentId, LmsRequirement requirement, LmsDepartmentGate gate) => new LmsActor
		{
			DepartmentId = departmentId,
			UserId = requirement?.CreatedByUserId ?? SystemUserId,
			IsCurrentMember = true,
			IsDepartmentAdmin = true,
			Gate = gate
		};

		#endregion Snapshot and evaluation

		#region Matrix and My Training

		public async Task<LmsComplianceMatrix> GetMatrixAsync(LmsActor actor, TrainingManagementScope scope, LmsMatrixFilter filter, DateTime utcNow)
		{
			var matrix = new LmsComplianceMatrix { AsOfUtc = utcNow };
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.FlagOn)
			{
				matrix.Error = LmsErrors.NotAuthorized;
				return matrix;
			}

			// Department-wide: LMS managers and holders of Assign Training department-wide; a group admin sees the group they administer.
			var everyone = actor.MayManage || (scope?.AssignRight?.ManagesAll ?? false);
			var administered = scope?.AssignRight?.AdministeredMemberUserIds;
			if (!everyone && (administered == null || administered.Count == 0))
			{
				matrix.Error = LmsErrors.NotAuthorized;
				return matrix;
			}

			if (actor.Gate.AddonActive != true && !actor.Gate.LastEntitledOn.HasValue)
			{
				matrix.Error = LmsErrors.AddonRequired;
				return matrix;
			}

			matrix.CanEdit = actor.MayManage && actor.Gate.CanAuthor;
			return await BuildMatrixAsync(matrix, actor.DepartmentId, everyone, administered, filter, await SubscriptionForViewAsync(actor), utcNow);
		}

		/// <summary>
		/// The department-wide matrix for the LMS report catalog (round 5F, key "compliance-matrix"): the catalog's callers are already
		/// authorized (officers on the Reports page, scheduled-report recipients who may read the department's training records), and worker
		/// 94 has asked the gate for the department. One subscription read per report build.
		/// </summary>
		public async Task<LmsComplianceMatrix> GetDepartmentMatrixAsync(int departmentId, LmsMatrixFilter filter, DateTime utcNow)
		{
			var matrix = new LmsComplianceMatrix { AsOfUtc = utcNow };
			if (departmentId <= 0)
			{
				matrix.Error = LmsErrors.NotAuthorized;
				return matrix;
			}
			return await BuildMatrixAsync(matrix, departmentId, true, null, filter, await _access.GetSubscriptionAsync(departmentId), utcNow);
		}

		private async Task<LmsComplianceMatrix> BuildMatrixAsync(LmsComplianceMatrix matrix, int departmentId, bool everyone, IReadOnlyCollection<string> administered,
			LmsMatrixFilter filter, LmsSubscriptionSnapshot subscription, DateTime utcNow)
		{
			var snapshot = await LoadAsync(departmentId, subscription, utcNow, includeFacts: true);
			matrix.Paused = !snapshot.Entitled;
			matrix.AllRequirements = snapshot.Requirements.OrderBy(r => r.Name).ToList();
			matrix.Requirements = matrix.AllRequirements.Where(r => filter?.LmsRequirementId == null || r.LmsRequirementId == filter.LmsRequirementId).ToList();
			matrix.TargetNames = snapshot.TargetNames;

			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			HashSet<string> groupMembers = null;
			if (filter?.DepartmentGroupId != null)
				groupMembers = await GroupMembersAsync(departmentId, filter.DepartmentGroupId.Value);

			var open = await OpenEnforcementKeysAsync(departmentId);
			var rows = new Dictionary<string, LmsMatrixRow>(StringComparer.OrdinalIgnoreCase);
			foreach (var requirement in matrix.Requirements)
			{
				foreach (var member in snapshot.Members.Where(m => m.LmsRequirementId == requirement.LmsRequirementId && !m.NotRequired))
				{
					// Inactive members never count, never show (inactive-member policy).
					if (!snapshot.Active.Contains(member.UserId))
						continue;
					if (!everyone && !administered.Contains(member.UserId))
						continue;
					if (groupMembers != null && !groupMembers.Contains(member.UserId))
						continue;

					var status = Evaluate(snapshot, requirement, member);
					if (filter?.State != null && status.State != filter.State.Value)
						continue;

					if (!rows.TryGetValue(member.UserId, out var row))
						rows[member.UserId] = row = new LmsMatrixRow { UserId = member.UserId, Name = names.TryGetValue(member.UserId, out var name) ? name : member.UserId };
					row.Cells[requirement.LmsRequirementId] = new LmsMatrixCell
					{
						LmsRequirementId = requirement.LmsRequirementId,
						LmsRequirementMemberId = member.LmsRequirementMemberId,
						State = status.State,
						DueUtc = status.ShownDueUtc,
						LastCompletedUtc = status.LastCompletedUtc,
						Progress = status.Progress,
						Required = status.Required,
						Counted = requirement.IsCounted,
						ExemptReason = status.State == LmsRequirementStates.Exempt ? member.ExemptReason : null,
						ExtendedDueUtc = member.ExtendedDueOn,
						Enforced = open.Contains((requirement.LmsRequirementId, member.UserId)),
						RecordedByOther = status.LastRecordedByOther
					};
					matrix.Totals[status.State] = matrix.Totals.TryGetValue(status.State, out var total) ? total + 1 : 1;
				}
			}

			matrix.Rows = rows.Values.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			return matrix;
		}

		private async Task<HashSet<(int, string)>> OpenEnforcementKeysAsync(int departmentId) =>
			new HashSet<(int, string)>((await _enforcements.GetOpenForDepartmentAsync(departmentId) ?? new List<LmsRequirementEnforcement>())
				.Select(e => (e.LmsRequirementId, e.UserId.ToUpperInvariant())), new KeyComparer());

		private sealed class KeyComparer : IEqualityComparer<(int, string)>
		{
			public bool Equals((int, string) x, (int, string) y) => x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
			public int GetHashCode((int, string) obj) => HashCode.Combine(obj.Item1, (obj.Item2 ?? string.Empty).ToUpperInvariant());
		}

		private async Task<HashSet<string>> GroupMembersAsync(int departmentId, int departmentGroupId)
		{
			var resolution = await _audienceService.ResolveAsync(departmentId, new[] { new ContentAudienceRule(ContentAudienceKinds.Group, departmentGroupId.ToString(CultureInfo.InvariantCulture)) });
			return resolution?.MemberUserIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		public async Task<List<LmsMyRequirement>> GetMyRequirementsAsync(int departmentId, string userId, DateTime utcNow)
		{
			var list = new List<LmsMyRequirement>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return list;

			var mine = (await _members.GetOpenForUserAsync(departmentId, userId) ?? new List<LmsRequirementMember>()).Where(m => !m.NotRequired).ToList();
			if (mine.Count == 0 || !await _access.IsFlagEnabledAsync(departmentId))
				return list;

			var subscription = await _access.GetSubscriptionAsync(departmentId);
			if (subscription.State == LmsSubscriptionStates.Unknown || (!subscription.IsEntitled && !subscription.LapsedSinceUtc.HasValue))
				return list;

			var snapshot = await LoadAsync(departmentId, subscription, utcNow, includeFacts: true);
			if (!snapshot.Active.Contains(userId))
				return list;

			foreach (var member in mine)
			{
				var requirement = snapshot.Requirements.FirstOrDefault(r => r.LmsRequirementId == member.LmsRequirementId);
				if (requirement == null)
					continue;
				var status = Evaluate(snapshot, requirement, member);
				list.Add(new LmsMyRequirement
				{
					Requirement = requirement,
					Status = status,
					TargetName = snapshot.TargetNames.TryGetValue(requirement.LmsRequirementId, out var name) ? name : requirement.Name,
					CourseId = requirement.Kind == (int)LmsRequirementKinds.Course ? requirement.TargetRefId : null,
					PathId = requirement.Kind == (int)LmsRequirementKinds.Path ? requirement.TargetRefId : null,
					TrainingId = requirement.Kind == (int)LmsRequirementKinds.Training ? requirement.TargetRefId : null
				});
			}

			// The next action first: overdue, at risk, pending (soonest due), then the rest.
			return list.OrderBy(m => Rank(m.Status.State)).ThenBy(m => m.Status.ShownDueUtc ?? DateTime.MaxValue).ToList();
		}

		private static int Rank(LmsRequirementStates state)
		{
			switch (state)
			{
				case LmsRequirementStates.Overdue: return 0;
				case LmsRequirementStates.AtRisk: return 1;
				case LmsRequirementStates.Pending: return 2;
				case LmsRequirementStates.OnLeave: return 3;
				case LmsRequirementStates.Compliant: return 4;
				default: return 5;
			}
		}

		public async Task<LmsResult> SetExemptionAsync(LmsActor actor, TrainingManagementScope scope, int lmsRequirementMemberId, bool exempt, DateTime? untilUtc, string reason,
			CancellationToken cancellationToken = default)
		{
			var (member, refused) = await MemberForChangeAsync(actor, scope, lmsRequirementMemberId);
			if (refused != null)
				return refused;

			var before = JsonConvert.SerializeObject(new { member.ExemptOn, member.ExemptUntil, member.ExemptReason });
			if (exempt)
			{
				if (string.IsNullOrWhiteSpace(reason))
					return new LmsResult { Error = LmsErrors.InvalidInput, Value = "Err.ReasonRequired" };
				member.ExemptOn = DateTime.UtcNow;
				member.ExemptUntil = untilUtc;
				member.ExemptReason = Trim(reason, 500);
				member.ExemptByUserId = actor.UserId;
			}
			else
			{
				member.ExemptOn = null;
				member.ExemptUntil = null;
				member.ExemptReason = null;
				member.ExemptByUserId = null;
			}

			await _members.SaveExemptionAsync(member, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LearningRequirementMemberChanged, before,
				new { member.LmsRequirementMemberId, member.LmsRequirementId, member.UserId, member.ExemptOn, member.ExemptUntil, member.ExemptReason });
			return new LmsResult { Count = 1 };
		}

		public async Task<LmsResult> ExtendAsync(LmsActor actor, TrainingManagementScope scope, int lmsRequirementMemberId, DateTime? dueUtc, string reason,
			CancellationToken cancellationToken = default)
		{
			var (member, refused) = await MemberForChangeAsync(actor, scope, lmsRequirementMemberId);
			if (refused != null)
				return refused;
			if (dueUtc.HasValue && (dueUtc.Value < DateTime.UtcNow.AddMinutes(-5) || string.IsNullOrWhiteSpace(reason)))
				return new LmsResult { Error = LmsErrors.InvalidInput, Value = "Err.ExtensionInvalid" };

			var before = JsonConvert.SerializeObject(new { member.ExtendedDueOn, member.ExtensionReason });
			member.ExtendedDueOn = dueUtc;
			member.ExtensionReason = dueUtc.HasValue ? Trim(reason, 500) : null;
			member.ExtendedByUserId = dueUtc.HasValue ? actor.UserId : null;
			await _members.SaveExemptionAsync(member, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LearningRequirementMemberChanged, before,
				new { member.LmsRequirementMemberId, member.LmsRequirementId, member.UserId, member.ExtendedDueOn, member.ExtensionReason });
			return new LmsResult { Count = 1 };
		}

		/// <summary>An exemption or extension needs the add-on and either the LMS manager right or the member's group-admin scope (R-C2).</summary>
		private async Task<(LmsRequirementMember Member, LmsResult Refused)> MemberForChangeAsync(LmsActor actor, TrainingManagementScope scope, int lmsRequirementMemberId)
		{
			if (actor == null || !actor.IsCurrentMember)
				return (null, LmsResult.Fail(LmsErrors.NotAuthorized));
			if (actor.Gate == null || !actor.Gate.CanAuthor)
				return (null, LmsResult.Fail(LmsErrors.AddonRequired));

			var member = await _members.GetByIdForDepartmentAsync(actor.DepartmentId, lmsRequirementMemberId);
			if (member == null || member.LeftOn.HasValue)
				return (null, LmsResult.Fail(LmsErrors.NotFound));
			if (!actor.MayManage && !(scope?.CanReviewRecordsOf(member.UserId) ?? false))
				return (null, LmsResult.Fail(LmsErrors.NotAuthorized));
			return (member, null);
		}

		#endregion Matrix and My Training

		#region Engine (worker 93, worker 83's hook)

		public async Task<LmsRequirementRunResult> RunDailyAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsRequirementRunResult();
			if (!await _access.IsFlagEnabledAsync(departmentId))
			{
				result.Skipped = true;
				return result;
			}

			// The one billing read of this department's run (R-L9).
			var subscription = await _access.GetSubscriptionAsync(departmentId);
			if (subscription.State == LmsSubscriptionStates.Unknown)
			{
				result.Skipped = true;
				return result;
			}

			if (!subscription.IsEntitled)
			{
				// Rule 5: the engine pauses. Rule 10: nothing enforcement set is left standing.
				result.Paused = true;
				result.Undone = await UndoAllAsync(departmentId, LmsEnforcementUndoReasons.Lapse, utcNow, cancellationToken);
				return result;
			}

			var snapshot = await LoadAsync(departmentId, subscription, utcNow, includeFacts: true);
			var gate = GateFor(departmentId, utcNow);
			await ReconcileAsync(snapshot, null, null, gate, utcNow, result, cancellationToken);
			await EvaluateAllAsync(snapshot, gate, utcNow, result, cancellationToken);
			result.Undone += await UndoCompletedAsync(departmentId, utcNow, cancellationToken);

			var settings = await GetSettingsAsync(departmentId);
			if (settings.EnforcementEnabled)
				await EnforceAsync(snapshot, settings, utcNow, result, cancellationToken);

			try
			{
				result.PathsAdvanced = await _paths.AdvanceDepartmentAsync(departmentId, gate, utcNow, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Learning paths of department {departmentId} could not be advanced.");
			}

			return result;
		}

		public async Task<LmsRequirementRunResult> OnAudienceChangesAsync(int departmentId, IReadOnlyCollection<string> userIds, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsRequirementRunResult();
			// Due work first (R-L9): a department without a live requirement costs one query and no gate call.
			if (departmentId <= 0 || !await _requirements.HasLiveAsync(departmentId) || !await _access.IsFlagEnabledAsync(departmentId))
			{
				result.Skipped = true;
				return result;
			}

			var subscription = await _access.GetSubscriptionAsync(departmentId);
			if (!subscription.IsEntitled)
			{
				// Rule 3: no new LMS assignments while the add-on is inactive; the next pass after reactivation picks these members up.
				result.Paused = true;
				return result;
			}

			var snapshot = await LoadAsync(departmentId, subscription, utcNow, includeFacts: true);
			var users = userIds == null || userIds.Count == 0 ? null : userIds.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
			await ReconcileAsync(snapshot, users, null, GateFor(departmentId, utcNow), utcNow, result, cancellationToken);
			return result;
		}

		/// <summary>
		/// Brings each live requirement's rows in line with its audience: a member in the audience without an open row gets one (entered
		/// now: the due-date anchor), a member who left the audience gets LeftOn (inactive members keep theirs). <paramref name="initialFor"/>
		/// names a requirement saved just now: its members are its starting audience, so a new-members-only requirement marks them not required.
		/// </summary>
		internal async Task ReconcileAsync(Snapshot snapshot, IReadOnlyCollection<string> onlyUsers, int? initialFor, LmsDepartmentGate gate, DateTime utcNow,
			LmsRequirementRunResult result, CancellationToken cancellationToken)
		{
			var departmentId = snapshot.DepartmentId;
			var factsCache = new Dictionary<string, ContentAudienceMemberFacts>(StringComparer.OrdinalIgnoreCase);
			foreach (var requirement in snapshot.Requirements)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var rules = snapshot.Audiences.TryGetValue(requirement.LmsRequirementId, out var list) ? list.Cast<IContentAudienceRule>().ToList() : new List<IContentAudienceRule>();
				if (rules.Count == 0)
					continue;

				HashSet<string> inAudience;
				IEnumerable<string> considered;
				if (onlyUsers == null)
				{
					inAudience = (await _audienceService.ResolveAsync(departmentId, rules))?.MemberUserIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					considered = null;
				}
				else
				{
					inAudience = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (var userId in onlyUsers)
					{
						if (!factsCache.TryGetValue(userId, out var facts))
							factsCache[userId] = facts = await _audienceService.GetMemberFactsAsync(departmentId, userId);
						if (ContentAudienceMatcher.Matches(facts, rules))
							inAudience.Add(userId);
					}
					considered = onlyUsers;
				}

				var rows = snapshot.Members.Where(m => m.LmsRequirementId == requirement.LmsRequirementId).ToList();
				var withRow = new HashSet<string>(rows.Select(r => r.UserId), StringComparer.OrdinalIgnoreCase);

				// Leaving: an active member no longer in the audience (an inactive one keeps the row; inactive members never count anyway).
				var leaving = rows.Where(r => snapshot.Active.Contains(r.UserId) && !inAudience.Contains(r.UserId) &&
					(considered == null || considered.Contains(r.UserId, StringComparer.OrdinalIgnoreCase))).ToList();
				if (leaving.Count > 0)
				{
					result.MembersLeft += await _members.MarkLeftAsync(departmentId, leaving.Select(r => r.LmsRequirementMemberId), utcNow, cancellationToken);
					foreach (var row in leaving)
						snapshot.Members.Remove(row);
				}

				foreach (var userId in inAudience.Where(u => !withRow.Contains(u) && snapshot.Active.Contains(u)))
				{
					var member = new LmsRequirementMember
					{
						DepartmentId = departmentId,
						LmsRequirementId = requirement.LmsRequirementId,
						UserId = userId,
						EnteredOn = utcNow,
						NotRequired = requirement.NewMembersOnly && initialFor == requirement.LmsRequirementId,
						StateChangedOn = utcNow
					};

					var status = Evaluate(snapshot, requirement, member);
					member.State = (int)status.State;
					member.DueOn = status.ShownDueUtc;
					member.LastCompletedOn = status.LastCompletedUtc;
					var (saved, created) = await _members.InsertOpenAsync(member, cancellationToken);
					if (!created || saved == null)
						continue;

					snapshot.Members.Add(saved);
					if (saved.NotRequired)
						continue;

					result.MembersAdded++;
					var targetName = snapshot.TargetNames.TryGetValue(requirement.LmsRequirementId, out var name) ? name : requirement.Name;
					RaiseEvent(requirement, saved.UserId, TrainingWorkflowChanges.Assigned, status, targetName, utcNow);
					if (snapshot.Licensed.Contains(saved.UserId) && !status.Satisfied)
						result.Notified += await SafeAsync(() => _notifier.NotifyAssignedAsync(departmentId, saved.UserId, requirement, targetName, status.EffectiveDueUtc, cancellationToken));
					await AutoAssignAsync(snapshot, requirement, saved, status, gate, result, cancellationToken);
				}
			}
		}

		/// <summary>
		/// Computes every required row's state; a change raises its workflow event (225/227/228) and sends the member's at-risk or overdue
		/// notice (claimed per due date), the supervisor and named escalation on overdue and the completion alert; auto-assigns renewals.
		/// </summary>
		internal async Task EvaluateAllAsync(Snapshot snapshot, LmsDepartmentGate gate, DateTime utcNow, LmsRequirementRunResult result, CancellationToken cancellationToken)
		{
			var departmentId = snapshot.DepartmentId;
			var byId = snapshot.Requirements.ToDictionary(r => r.LmsRequirementId);
			foreach (var member in snapshot.Members.ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (member.NotRequired || !snapshot.Active.Contains(member.UserId) || !byId.TryGetValue(member.LmsRequirementId, out var requirement))
					continue;

				var status = Evaluate(snapshot, requirement, member);
				var previous = (LmsRequirementStates)member.State;
				var targetName = snapshot.TargetNames.TryGetValue(requirement.LmsRequirementId, out var name) ? name : requirement.Name;
				var changed = previous != status.State;

				if (changed)
				{
					result.Transitions++;
					switch (status.State)
					{
						case LmsRequirementStates.Overdue:
							RaiseEvent(requirement, member.UserId, TrainingWorkflowChanges.Overdue, status, targetName, utcNow);
							break;
						case LmsRequirementStates.AtRisk:
							RaiseEvent(requirement, member.UserId, TrainingWorkflowChanges.AtRisk, status, targetName, utcNow);
							break;
						case LmsRequirementStates.Compliant when previous == LmsRequirementStates.Pending || previous == LmsRequirementStates.AtRisk || previous == LmsRequirementStates.Overdue:
							RaiseEvent(requirement, member.UserId, TrainingWorkflowChanges.Completed, status, targetName, utcNow);
							if (requirement.AlertOnCompletion && (requirement.AlertSupervisors || !string.IsNullOrWhiteSpace(requirement.AlertUserIds)))
								result.Notified += await SafeAsync(() => _notifier.NotifySupervisorsAsync(departmentId, member.UserId, requirement, targetName, true, cancellationToken));
							break;
					}
				}

				// Notices are claimed per due date, so a state that persists for days notifies once per due date. A renewal that is coming up
				// (satisfied, at risk) is announced against its next due date.
				var due = status.ShownDueUtc;
				if (due.HasValue && (status.State == LmsRequirementStates.AtRisk || status.State == LmsRequirementStates.Overdue))
				{
					var overdue = status.State == LmsRequirementStates.Overdue;
					if (await _members.TryClaimNoticeAsync(departmentId, member.LmsRequirementMemberId, overdue, due.Value, cancellationToken))
					{
						result.Notified += await SafeAsync(() => _notifier.NotifyDueAsync(departmentId, member.UserId, requirement, targetName, status.State, due, cancellationToken));
						if (overdue && (requirement.AlertSupervisors || !string.IsNullOrWhiteSpace(requirement.AlertUserIds)))
							result.Notified += await SafeAsync(() => _notifier.NotifySupervisorsAsync(departmentId, member.UserId, requirement, targetName, false, cancellationToken));
					}
				}

				if (changed || member.DueOn != status.ShownDueUtc || member.LastCompletedOn != status.LastCompletedUtc)
				{
					member.State = (int)status.State;
					if (changed)
						member.StateChangedOn = utcNow;
					member.DueOn = status.ShownDueUtc;
					member.LastCompletedOn = status.LastCompletedUtc;
					await _members.SaveStateAsync(member, cancellationToken);
				}

				await AutoAssignAsync(snapshot, requirement, member, status, gate, result, cancellationToken);
			}
		}

		/// <summary>Onboarding and renewals (item 6): assigns the course or path for the open due date, once per due date (claimed first).</summary>
		private async Task AutoAssignAsync(Snapshot snapshot, LmsRequirement requirement, LmsRequirementMember member, LmsRequirementStatus status, LmsDepartmentGate gate,
			LmsRequirementRunResult result, CancellationToken cancellationToken)
		{
			// A course renewal is assigned when its at-risk window opens, before the completion lapses (a path is assigned once: its progress
			// is read from the steps' completions, so re-assigning a finished path would count as finished at once).
			var renewal = status.Satisfied && status.State == LmsRequirementStates.AtRisk && status.NextDueUtc.HasValue && requirement.Kind == (int)LmsRequirementKinds.Course;
			if (!requirement.AutoAssign || !requirement.TargetRefId.HasValue || (status.Satisfied && !renewal) || !snapshot.Licensed.Contains(member.UserId) ||
				status.State == LmsRequirementStates.Exempt || status.State == LmsRequirementStates.NotLicensed || !status.ShownDueUtc.HasValue)
				return;

			var dueKey = status.ShownDueUtc.Value;
			if (!await _members.TryClaimAssignmentAsync(snapshot.DepartmentId, member.LmsRequirementMemberId, dueKey, cancellationToken))
				return;

			try
			{
				var actor = SystemActor(snapshot.DepartmentId, requirement, gate);
				// Round 10 A7: the licensed members this run already read stand in for the seat holders (no second billing read per member, R-L9);
				// a member without a seat was skipped above (state NotLicensed) and is assigned on a later run once a seat is enabled.
				actor.SeatHolders = snapshot.Licensed;
				var who = new[] { new ContentAudienceRule(ContentAudienceKinds.User, member.UserId) };
				var due = dueKey > snapshot.UtcNow ? dueKey : (DateTime?)null;
				LmsAssignOutcome outcome = null;
				if (requirement.Kind == (int)LmsRequirementKinds.Course)
					outcome = await _learner.AssignAsync(actor, requirement.TargetRefId.Value, who, due, LmsEnrollmentSources.Assigned, null, cancellationToken);
				else if (requirement.Kind == (int)LmsRequirementKinds.Path)
					outcome = await _paths.AssignAsync(actor, requirement.TargetRefId.Value, who, due, requirement.LmsRequirementId, cancellationToken);
				if (outcome != null)
					result.Assigned += outcome.Created;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Learning requirement {requirement.LmsRequirementId} could not assign its {(LmsRequirementKinds)requirement.Kind} to a member.");
			}
		}

		private void RaiseEvent(LmsRequirement requirement, string userId, TrainingWorkflowChanges change, LmsRequirementStatus status, string targetName, DateTime utcNow,
			int? certificationTypeId = null, string certificationTypeName = null)
		{
			_eventAggregator?.SendMessage(BuildEvent(requirement, userId, change, status, targetName, utcNow, certificationTypeId, certificationTypeName));
		}

		/// <summary>The workflow payload: ids, names of the training and dates; never the member's name, e-mail or phone.</summary>
		public static TrainingWorkflowEvent BuildEvent(LmsRequirement requirement, string userId, TrainingWorkflowChanges change, LmsRequirementStatus status, string targetName,
			DateTime utcNow, int? certificationTypeId = null, string certificationTypeName = null)
		{
			var due = status?.EffectiveDueUtc ?? status?.NextDueUtc;
			int? days = null;
			if (due.HasValue && (change == TrainingWorkflowChanges.Overdue || change == TrainingWorkflowChanges.Enforced))
				days = Math.Max(0, (int)Math.Floor((utcNow - due.Value).TotalDays));
			else if (due.HasValue && change == TrainingWorkflowChanges.AtRisk)
				days = (int)Math.Ceiling((due.Value - utcNow).TotalDays);

			return new TrainingWorkflowEvent
			{
				DepartmentId = requirement.DepartmentId,
				Change = change,
				UserId = userId,
				Subject = "Requirement",
				RequirementId = requirement.LmsRequirementId,
				RequirementName = requirement.Name,
				RequirementKind = ((LmsRequirementKinds)requirement.Kind).ToString(),
				CourseId = requirement.Kind == (int)LmsRequirementKinds.Course ? requirement.TargetRefId : null,
				PathId = requirement.Kind == (int)LmsRequirementKinds.Path ? requirement.TargetRefId : null,
				TrainingName = targetName,
				State = status?.State.ToString(),
				DueOn = due,
				CompletedOn = status?.LastCompletedUtc,
				Days = days,
				CertificationTypeId = certificationTypeId,
				CertificationTypeName = certificationTypeName,
				OccurredOn = utcNow
			};
		}

		private static async Task<int> SafeAsync(Func<Task<int>> send)
		{
			try
			{
				return await send();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A learning requirement notice could not be sent.");
				return 0;
			}
		}

		#endregion Engine (worker 93, worker 83's hook)

		#region Enforcement

		/// <summary>One candidate action: the member's live certification record this overdue requirement would expire, and what it costs.</summary>
		private sealed class Candidate
		{
			public LmsRequirement Requirement { get; set; }
			public LmsRequirementMember Member { get; set; }
			public LmsRequirementStatus Status { get; set; }
			public PersonnelCertification Record { get; set; }
			public string TypeName { get; set; }
			public List<LmsRoleImpact> RolesLost { get; set; } = new List<LmsRoleImpact>();
		}

		private sealed class EnforcementPlan
		{
			public List<Candidate> Candidates { get; set; } = new List<Candidate>();
			public List<LmsRoleBreach> Breaches { get; set; } = new List<LmsRoleBreach>();
			public DepartmentCertificationSettings CertificationSettings { get; set; }
		}

		/// <summary>
		/// The next run's actions (R-L11): every enforceable member (overdue past grace, licensed, not exempt, not on leave, not held by the
		/// reactivation grace, no open action) of a requirement that opted in and backs a certification type, who holds a live record of that
		/// type; the roles each would lose under the certification module's Enforce mode; and the roles the run would thin by more than the
		/// allowed share.
		/// </summary>
		private async Task<EnforcementPlan> PlanAsync(Snapshot snapshot, LearningRequirementSettings settings)
		{
			var plan = new EnforcementPlan();
			var departmentId = snapshot.DepartmentId;
			var enforcing = snapshot.Requirements.Where(r => r.EnforcementEnabled && r.BacksCertificationTypeId.HasValue).ToList();
			if (enforcing.Count == 0)
				return plan;

			var open = await OpenEnforcementKeysAsync(departmentId);
			var due = new List<(LmsRequirement Requirement, LmsRequirementMember Member, LmsRequirementStatus Status)>();
			foreach (var requirement in enforcing)
				foreach (var member in snapshot.Members.Where(m => m.LmsRequirementId == requirement.LmsRequirementId && !m.NotRequired && snapshot.Active.Contains(m.UserId)))
				{
					if (open.Contains((requirement.LmsRequirementId, member.UserId)))
						continue;
					var status = Evaluate(snapshot, requirement, member);
					if (status.Enforceable)
						due.Add((requirement, member, status));
				}

			if (due.Count == 0)
				return plan;

			var users = due.Select(d => d.Member.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var records = (await _certifications.GetCertificationsForDepartmentAsync(departmentId, users) ?? new List<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.IsTyped).ToList();
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => !t.IsDeleted).ToDictionary(t => t.DepartmentCertificationTypeId);
			var roleRequirements = await _certifications.GetAllRoleRequirementsAsync(departmentId) ?? new List<PersonnelRoleCertificationRequirement>();
			plan.CertificationSettings = await _certifications.GetCertificationSettingsAsync(departmentId) ?? new DepartmentCertificationSettings { DepartmentId = departmentId };
			var removesRoles = plan.CertificationSettings.EnforcementMode == (int)CertificationEnforcementModes.Enforce;
			var rolesByUser = await _roles.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();
			var rolesOf = rolesByUser.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.SelectMany(p => p.Value ?? new List<PersonnelRole>()).ToList(),
				StringComparer.OrdinalIgnoreCase);
			var today = TrainingDueDates.LocalDate(snapshot.UtcNow, snapshot.Department);

			foreach (var (requirement, member, status) in due)
			{
				var typeId = requirement.BacksCertificationTypeId.Value;
				var mine = records.Where(r => string.Equals(r.UserId, member.UserId, StringComparison.OrdinalIgnoreCase)).ToList();
				var record = mine.Where(r => r.DepartmentCertificationTypeId == typeId && (r.Status == (int)PersonnelCertificationStatuses.Active ||
						r.Status == (int)PersonnelCertificationStatuses.Trainee || r.Status == (int)PersonnelCertificationStatuses.PendingVerification))
					.OrderByDescending(r => r.ExpiresOn ?? DateTime.MaxValue).FirstOrDefault();
				if (record == null)
					continue;

				var candidate = new Candidate
				{
					Requirement = requirement, Member = member, Status = status, Record = record,
					TypeName = types.TryGetValue(typeId, out var type) ? type.Type : null
				};
				if (removesRoles && rolesOf.TryGetValue(member.UserId, out var memberRoles))
				{
					var lost = LmsEnforcementGuard.RolesLost(member.UserId, memberRoles.Select(r => r.PersonnelRoleId), roleRequirements, mine, types, plan.CertificationSettings,
						record.PersonnelCertificationId, today);
					candidate.RolesLost = lost.Select(id => new LmsRoleImpact
					{
						PersonnelRoleId = id, RoleName = memberRoles.FirstOrDefault(r => r.PersonnelRoleId == id)?.Name, UserId = member.UserId,
						LmsRequirementId = requirement.LmsRequirementId, PersonnelCertificationId = record.PersonnelCertificationId
					}).ToList();
				}
				plan.Candidates.Add(candidate);
			}

			// Active holders per role (inactive members are not holders that staffing counts on).
			var holders = new Dictionary<int, int>();
			foreach (var pair in rolesOf.Where(p => snapshot.Active.Contains(p.Key)))
				foreach (var roleId in pair.Value.Select(r => r.PersonnelRoleId).Distinct())
					holders[roleId] = holders.TryGetValue(roleId, out var count) ? count + 1 : 1;
			plan.Breaches = LmsEnforcementGuard.Check(plan.Candidates.SelectMany(c => c.RolesLost), holders, settings.MaxRoleRemovalSharePercent);
			return plan;
		}

		public async Task<LmsEnforcementPreview> PreviewEnforcementAsync(LmsActor actor, DateTime utcNow)
		{
			var preview = new LmsEnforcementPreview();
			if (actor == null || !actor.MayManage || actor.Gate == null || !actor.Gate.FlagOn)
			{
				preview.Error = LmsErrors.NotAuthorized;
				return preview;
			}

			var settings = await GetSettingsAsync(actor.DepartmentId);
			preview.DepartmentEnabled = settings.EnforcementEnabled;
			preview.MaxSharePercent = settings.MaxRoleRemovalSharePercent;
			var subscription = await SubscriptionForViewAsync(actor);
			var snapshot = await LoadAsync(actor.DepartmentId, subscription, utcNow, includeFacts: true);
			preview.Paused = !snapshot.Entitled;
			var plan = await PlanAsync(snapshot, settings);
			var certSettings = plan.CertificationSettings ?? await _certifications.GetCertificationSettingsAsync(actor.DepartmentId) ?? new DepartmentCertificationSettings();
			preview.CertificationEnforcementMode = certSettings.EnforcementMode;
			preview.RoleRemovalGraceDays = certSettings.RoleRemovalGraceDays;
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(actor.DepartmentId) ?? new List<PersonName>())
				.GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			preview.Rows = plan.Candidates.Select(c => new LmsEnforcementPreviewRow
			{
				Requirement = c.Requirement,
				UserId = c.Member.UserId,
				Name = names.TryGetValue(c.Member.UserId, out var name) ? name : c.Member.UserId,
				PersonnelCertificationId = c.Record.PersonnelCertificationId,
				CertificationTypeName = c.TypeName,
				DueUtc = c.Status.EffectiveDueUtc,
				RolesLost = c.RolesLost
			}).OrderBy(r => r.Name).ToList();
			preview.Breaches = plan.Breaches;
			return preview;
		}

		private async Task EnforceAsync(Snapshot snapshot, LearningRequirementSettings settings, DateTime utcNow, LmsRequirementRunResult result, CancellationToken cancellationToken)
		{
			var departmentId = snapshot.DepartmentId;
			var plan = await PlanAsync(snapshot, settings);
			if (plan.Candidates.Count == 0)
				return;

			if (plan.Breaches.Count > 0)
			{
				// R-L11: the whole run is refused and the admins are told; nothing is expired, nothing is removed.
				result.Refused = true;
				Audit(departmentId, SystemUserId, AuditLogTypes.LearningRequirementEnforcementRefused, null, new
				{
					maxSharePercent = settings.MaxRoleRemovalSharePercent,
					breaches = plan.Breaches.Select(b => new { b.PersonnelRoleId, b.RoleName, b.Losing, b.Holders, b.SharePercent }),
					actions = plan.Candidates.Count
				});
				await _departmentStates.StampRefusedAsync(departmentId, utcNow, cancellationToken);
				result.Notified += await SafeAsync(() => _notifier.NotifyRefusedAsync(departmentId, plan.Breaches, settings.MaxRoleRemovalSharePercent, cancellationToken));
				return;
			}

			var today = TrainingDueDates.LocalDate(utcNow, snapshot.Department);
			foreach (var candidate in plan.Candidates)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var requirement = candidate.Requirement;
				var action = new LmsRequirementEnforcement
				{
					DepartmentId = departmentId,
					LmsRequirementId = requirement.LmsRequirementId,
					LmsRequirementMemberId = candidate.Member.LmsRequirementMemberId,
					UserId = candidate.Member.UserId,
					PersonnelCertificationId = candidate.Record.PersonnelCertificationId,
					DepartmentCertificationTypeId = requirement.BacksCertificationTypeId.Value,
					PreviousStatus = candidate.Record.Status,
					PreviousExpiresOn = candidate.Record.ExpiresOn,
					CauseLearningRecordId = candidate.Status.LastLearningRecordId,
					DueOn = candidate.Status.EffectiveDueUtc ?? utcNow,
					ActedOn = utcNow,
					RolesJson = JsonConvert.SerializeObject(candidate.RolesLost.Select(r => new { r.PersonnelRoleId, r.RoleName }))
				};

				// The action is recorded before the record changes, so an expiry can always be undone exactly.
				if (!await _enforcements.TryInsertOpenAsync(action, cancellationToken))
					continue;

				try
				{
					var expired = await _certifications.ExpireCertificationByRuleAsync(candidate.Record.PersonnelCertificationId, departmentId, today,
						EnforcementReasonPrefix + requirement.LmsRequirementId.ToString(CultureInfo.InvariantCulture), SystemUserId, cancellationToken);
					if (expired == null)
					{
						await _enforcements.TryMarkUndoneAsync(departmentId, action.LmsRequirementEnforcementId, LmsEnforcementUndoReasons.NoLongerApplies, null, utcNow, cancellationToken);
						continue;
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Learning requirement {requirement.LmsRequirementId} could not expire certification {candidate.Record.PersonnelCertificationId}.");
					await _enforcements.TryMarkUndoneAsync(departmentId, action.LmsRequirementEnforcementId, LmsEnforcementUndoReasons.NoLongerApplies, null, utcNow, cancellationToken);
					continue;
				}

				result.Enforced++;
				Audit(departmentId, SystemUserId, AuditLogTypes.LearningRequirementEnforced, new { candidate.Record.PersonnelCertificationId, candidate.Record.Status, candidate.Record.ExpiresOn },
					new
					{
						action.LmsRequirementEnforcementId,
						requirement = new { requirement.LmsRequirementId, requirement.Name },
						action.UserId,
						action.PersonnelCertificationId,
						certificationType = candidate.TypeName,
						causeLearningRecordId = action.CauseLearningRecordId,
						dueOn = action.DueOn,
						roles = candidate.RolesLost.Select(r => new { r.PersonnelRoleId, r.RoleName })
					});
				var targetName = snapshot.TargetNames.TryGetValue(requirement.LmsRequirementId, out var name) ? name : requirement.Name;
				RaiseEvent(requirement, action.UserId, TrainingWorkflowChanges.Enforced, candidate.Status, targetName, utcNow, action.DepartmentCertificationTypeId, candidate.TypeName);
				result.Notified += await SafeAsync(() => _notifier.NotifyEnforcementAsync(departmentId, action.UserId, requirement, candidate.TypeName, false, cancellationToken));
			}
		}

		public async Task<List<LmsRequirementEnforcement>> GetEnforcementHistoryAsync(LmsActor actor, int take)
		{
			if (actor == null || !actor.MayManage || actor.Gate == null || !actor.Gate.FlagOn)
				return new List<LmsRequirementEnforcement>();
			return await _enforcements.GetRecentForDepartmentAsync(actor.DepartmentId, take) ?? new List<LmsRequirementEnforcement>();
		}

		public async Task<LmsResult> UndoEnforcementAsync(LmsActor actor, int lmsRequirementEnforcementId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage || actor.Gate == null || !actor.Gate.FlagOn)
				return LmsResult.Fail(LmsErrors.NotAuthorized);

			var action = (await _enforcements.GetOpenForDepartmentAsync(actor.DepartmentId) ?? new List<LmsRequirementEnforcement>())
				.FirstOrDefault(e => e.LmsRequirementEnforcementId == lmsRequirementEnforcementId);
			if (action == null)
				return LmsResult.Fail(LmsErrors.NotFound);

			return await UndoAsync(action, LmsEnforcementUndoReasons.Manual, DateTime.UtcNow, actor.UserId, cancellationToken) ? new LmsResult { Count = 1 } : LmsResult.Fail(LmsErrors.StaleCopy);
		}

		public async Task<int> UndoCompletedAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var open = await _enforcements.GetOpenForDepartmentAsync(departmentId) ?? new List<LmsRequirementEnforcement>();
			if (open.Count == 0)
				return 0;

			var settings = await GetSettingsAsync(departmentId);
			var requirements = (await _requirements.GetForDepartmentAsync(departmentId, includeDeleted: true) ?? new List<LmsRequirement>()).ToDictionary(r => r.LmsRequirementId);
			var members = (await _members.GetOpenForDepartmentAsync(departmentId) ?? new List<LmsRequirementMember>());

			// Satisfaction does not depend on licensing, leave or lapses: evaluate the members with open actions against their requirements.
			var snapshot = new Snapshot
			{
				DepartmentId = departmentId,
				UtcNow = utcNow,
				Department = await _departments.GetDepartmentByIdAsync(departmentId, false),
				Requirements = open.Select(e => requirements.TryGetValue(e.LmsRequirementId, out var r) ? r : null).Where(r => r != null && r.IsLive).Distinct().ToList(),
				Entitled = true
			};
			await LoadFactsAsync(snapshot);

			var undone = 0;
			foreach (var action in open)
			{
				cancellationToken.ThrowIfCancellationRequested();
				LmsEnforcementUndoReasons? reason = null;
				if (!settings.EnforcementEnabled || !requirements.TryGetValue(action.LmsRequirementId, out var requirement) || !requirement.IsLive || !requirement.EnforcementEnabled ||
					requirement.BacksCertificationTypeId != action.DepartmentCertificationTypeId)
					reason = LmsEnforcementUndoReasons.NoLongerApplies;
				else
				{
					var member = members.FirstOrDefault(m => m.LmsRequirementId == action.LmsRequirementId && string.Equals(m.UserId, action.UserId, StringComparison.OrdinalIgnoreCase));
					if (member == null || member.IsExemptOn(utcNow))
						reason = LmsEnforcementUndoReasons.NoLongerApplies;
					else if (Evaluate(snapshot, requirement, member).Satisfied)
						reason = LmsEnforcementUndoReasons.Completed;
				}

				if (reason.HasValue && await UndoAsync(action, reason.Value, utcNow, SystemUserId, cancellationToken))
					undone++;
			}

			return undone;
		}

		private async Task<int> UndoAllAsync(int departmentId, LmsEnforcementUndoReasons reason, DateTime utcNow, CancellationToken cancellationToken)
		{
			var undone = 0;
			foreach (var action in await _enforcements.GetOpenForDepartmentAsync(departmentId) ?? new List<LmsRequirementEnforcement>())
				if (await UndoAsync(action, reason, utcNow, SystemUserId, cancellationToken))
					undone++;
			return undone;
		}

		private async Task<int> UndoForRequirementAsync(int departmentId, int lmsRequirementId, LmsEnforcementUndoReasons reason, DateTime utcNow, CancellationToken cancellationToken)
		{
			var undone = 0;
			foreach (var action in (await _enforcements.GetOpenForDepartmentAsync(departmentId) ?? new List<LmsRequirementEnforcement>()).Where(e => e.LmsRequirementId == lmsRequirementId))
				if (await UndoAsync(action, reason, utcNow, SystemUserId, cancellationToken))
					undone++;
			return undone;
		}

		/// <summary>
		/// Undoes one action: the certification record goes back to the status and expiry it had (only while it is still as the enforcement
		/// left it: a record someone renewed or changed since is theirs), and the member gets back the roles that required it and that they
		/// lost since (the certification sweep removed them under its grace). Audited; the member is told.
		/// </summary>
		internal async Task<bool> UndoAsync(LmsRequirementEnforcement action, LmsEnforcementUndoReasons reason, DateTime utcNow, string actingUserId, CancellationToken cancellationToken)
		{
			var departmentId = action.DepartmentId;
			var restoredRoles = new List<int>();
			try
			{
				var record = await _certifications.GetCertificationByIdAsync(action.PersonnelCertificationId);
				if (record != null && !record.IsDeleted && record.DepartmentId == departmentId && record.Status == (int)PersonnelCertificationStatuses.Expired &&
					(record.StatusReason ?? string.Empty).StartsWith(EnforcementReasonPrefix, StringComparison.OrdinalIgnoreCase))
				{
					await _certifications.RestoreCertificationStateAsync(record.PersonnelCertificationId, departmentId, (PersonnelCertificationStatuses)action.PreviousStatus,
						action.PreviousExpiresOn, null, actingUserId ?? SystemUserId, cancellationToken);
				}

				var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>();
				var wanted = ParseRoleIds(action.RolesJson);
				if (wanted.Count > 0 && active.Contains(action.UserId, StringComparer.OrdinalIgnoreCase))
				{
					var current = (await _roles.GetRolesForUserAsync(action.UserId, departmentId) ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId).ToHashSet();
					var departmentRoles = (await _roles.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId).ToHashSet();
					var missing = wanted.Where(id => !current.Contains(id) && departmentRoles.Contains(id)).ToList();
					if (missing.Count > 0)
					{
						try
						{
							await _roles.SetRolesForUserAsync(departmentId, action.UserId, current.Concat(missing).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(),
								cancellationToken, actingUserId ?? SystemUserId);
							restoredRoles.AddRange(missing);
						}
						catch (Exception ex)
						{
							// A role whose other requirements the member does not meet stays off; the officer sees it in the history.
							Logging.LogException(ex, $"Roles taken by enforcement {action.LmsRequirementEnforcementId} could not all be given back.");
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Enforcement {action.LmsRequirementEnforcementId} could not be undone.");
				return false;
			}

			if (!await _enforcements.TryMarkUndoneAsync(departmentId, action.LmsRequirementEnforcementId, reason, JsonConvert.SerializeObject(restoredRoles), utcNow, cancellationToken))
				return false;

			Audit(departmentId, actingUserId ?? SystemUserId, AuditLogTypes.LearningRequirementEnforcementUndone, null, new
			{
				action.LmsRequirementEnforcementId,
				action.LmsRequirementId,
				action.UserId,
				action.PersonnelCertificationId,
				reason = reason.ToString(),
				restoredRoles
			});

			var requirement = await _requirements.GetByIdForDepartmentAsync(departmentId, action.LmsRequirementId);
			// Workflow trigger 264, once: TryMarkUndoneAsync above let only this call undo it. The system's own undo names no actor.
			ContentWorkflowContext.Publish(_eventAggregator, LmsWorkflowEvents.EnforcementUndone(action, requirement, reason, restoredRoles.Count,
				string.Equals(actingUserId, SystemUserId, StringComparison.OrdinalIgnoreCase) ? null : actingUserId, utcNow));
			if (requirement != null)
				await SafeAsync(() => _notifier.NotifyEnforcementAsync(departmentId, action.UserId, requirement, null, true, cancellationToken));
			return true;
		}

		public static List<int> ParseRoleIds(string rolesJson)
		{
			if (string.IsNullOrWhiteSpace(rolesJson))
				return new List<int>();
			try
			{
				return (JsonConvert.DeserializeObject<List<RoleRef>>(rolesJson) ?? new List<RoleRef>()).Where(r => r != null && r.PersonnelRoleId > 0).Select(r => r.PersonnelRoleId).Distinct().ToList();
			}
			catch (JsonException)
			{
				return new List<int>();
			}
		}

		private sealed class RoleRef
		{
			public int PersonnelRoleId { get; set; }
		}

		#endregion Enforcement

		private static string Trim(string value, int max)
		{
			var trimmed = value?.Trim();
			return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, object before, object after)
		{
			_eventAggregator?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId ?? SystemUserId,
				Type = type,
				Before = before == null ? null : before as string ?? JsonConvert.SerializeObject(before),
				After = after == null ? null : after as string ?? JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
