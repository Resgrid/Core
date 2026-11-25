using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Learning paths (Training T3 item 4; LMS for department paths, free for the Resgrid Academy role paths, R-S2): ordered steps (LMS
	/// courses, Academy lessons, baseline trainings) with prerequisites and elective groups; finishing a path can issue a certification
	/// through T2's certification issue path. A1's role paths were plain ordered lists: they are seeded here as system paths and their
	/// assignments linked by key (A2 "maintained as code"), and progress keeps coming from the lesson completions, so nothing was lost and
	/// nobody was re-assigned. A step's course is enrolled when it unlocks; the learner's view, worker 93 and onboarding advance paths.
	/// </summary>
	public class LmsPathService : ILmsPathService
	{
		private static readonly SemaphoreSlim SeedLock = new SemaphoreSlim(1, 1);
		private static int _seededVersion;

		private readonly ILmsPathsRepository _paths;
		private readonly ILmsPathStepsRepository _steps;
		private readonly ILmsPathAssignmentsRepository _assignments;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsRequirementFactsRepository _facts;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsAcademyService _academy;
		private readonly ILmsEntitlementService _entitlements;
		private readonly IContentAudienceService _audienceService;
		private readonly ITrainingService _trainings;
		private readonly ICertificationService _certifications;
		private readonly IEventAggregator _eventAggregator;

		public LmsPathService(ILmsPathsRepository paths, ILmsPathStepsRepository steps, ILmsPathAssignmentsRepository assignments, ILmsEnrollmentsRepository enrollments,
			ILmsCoursesRepository courses, ILmsRequirementFactsRepository facts, ILmsLearnerService learner, ILmsAcademyService academy, ILmsEntitlementService entitlements,
			IContentAudienceService audienceService, ITrainingService trainings, ICertificationService certifications, IEventAggregator eventAggregator)
		{
			_paths = paths;
			_steps = steps;
			_assignments = assignments;
			_enrollments = enrollments;
			_courses = courses;
			_facts = facts;
			_learner = learner;
			_academy = academy;
			_entitlements = entitlements;
			_audienceService = audienceService;
			_trainings = trainings;
			_certifications = certifications;
			_eventAggregator = eventAggregator;
		}

		/// <summary>For tests: forget that this process already seeded.</summary>
		public static void ResetSeedMemo() => _seededVersion = 0;

		public string TitleOf(LmsPath path, string locale)
		{
			if (path == null)
				return null;
			return path.IsSystem ? _academy.Text("Path." + path.AssignmentKey, locale ?? "en") ?? path.Name : path.Name;
		}

		#region Academy role paths (A1 → T3)

		public async Task<int> EnsureSystemPathsAsync(CancellationToken cancellationToken = default)
		{
			var changed = 0;
			if (_seededVersion < AcademyCourseManifest.ManifestVersion)
			{
				await SeedLock.WaitAsync(cancellationToken);
				try
				{
					if (_seededVersion < AcademyCourseManifest.ManifestVersion)
					{
						await _academy.EnsureSeededAsync(cancellationToken);
						var courses = (await _courses.GetSystemCoursesAsync() ?? new List<LmsCourse>()).Where(c => c.SystemKey != null)
							.GroupBy(c => c.SystemKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
						foreach (var rolePath in AcademyCourseManifest.RolePaths)
							changed += await SeedSystemPathAsync(rolePath, courses, cancellationToken) ? 1 : 0;
						_seededVersion = AcademyCourseManifest.ManifestVersion;
					}
				}
				finally
				{
					SeedLock.Release();
				}
			}

			// A1's ordered-list assignments (and any made by the Academy page since) are linked to their system path by key.
			foreach (var rolePath in AcademyCourseManifest.RolePaths)
			{
				var path = await _paths.GetBySystemKeyAsync(LmsPath.SystemKeyFor(rolePath.Key));
				if (path != null)
					changed += await _assignments.LinkSystemPathAsync(rolePath.Key, path.LmsPathId, cancellationToken);
			}

			return changed;
		}

		private async Task<bool> SeedSystemPathAsync(AcademyRolePath rolePath, IReadOnlyDictionary<string, LmsCourse> courses, CancellationToken cancellationToken)
		{
			var systemKey = LmsPath.SystemKeyFor(rolePath.Key);
			var path = await _paths.GetBySystemKeyAsync(systemKey);
			var created = false;
			if (path == null)
			{
				path = new LmsPath { DepartmentId = 0, SystemKey = systemKey, Name = rolePath.Key, CreatedOn = DateTime.UtcNow, CreatedByUserId = LmsRequirementService.SystemUserId };
				try
				{
					path = await _paths.InsertAsync(path, cancellationToken, true);
					created = true;
				}
				catch (Exception ex)
				{
					// A concurrent seeder won the unique SystemKey: read its row.
					Logging.LogException(ex, $"Seeding Academy path {rolePath.Key} raced another seeder.");
					path = await _paths.GetBySystemKeyAsync(systemKey);
					if (path == null)
						return false;
				}
			}

			var wanted = LmsPathRules.AcademySteps(rolePath, key => courses.TryGetValue(key, out var course) ? course : null);
			var current = (await _steps.GetForPathsAsync(new[] { path.LmsPathId }) ?? new List<LmsPathStep>()).OrderBy(s => s.SortOrder).ToList();
			if (!created && Same(current, wanted))
				return false;

			await _steps.ReplaceAsync(0, path.LmsPathId, wanted, cancellationToken);
			return true;
		}

		private static bool Same(IReadOnlyList<LmsPathStep> a, IReadOnlyList<LmsPathStep> b) =>
			a.Count == b.Count && a.Zip(b, (x, y) => x.Kind == y.Kind && x.TargetRefId == y.TargetRefId && x.SortOrder == y.SortOrder &&
				string.Equals(x.PrerequisiteOrders ?? string.Empty, y.PrerequisiteOrders ?? string.Empty, StringComparison.Ordinal)).All(same => same);

		#endregion Academy role paths (A1 → T3)

		#region Authoring

		public async Task<List<LmsPathRow>> GetPathsAsync(LmsActor actor)
		{
			var rows = new List<LmsPathRow>();
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.ModuleOn)
				return rows;

			var paths = (await _paths.GetVisibleAsync(actor.DepartmentId) ?? new List<LmsPath>())
				.Where(p => p.IsSystem || actor.Gate.FlagOn).ToList();
			var steps = (await _steps.GetForPathsAsync(paths.Select(p => p.LmsPathId)) ?? new List<LmsPathStep>()).GroupBy(s => s.LmsPathId).ToDictionary(g => g.Key, g => g.Count());
			var assignments = actor.MayAssignAcademy ? await _assignments.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsPathAssignment>() : new List<LmsPathAssignment>();
			foreach (var path in paths)
			{
				var mine = assignments.Where(a => Belongs(a, path)).ToList();
				rows.Add(new LmsPathRow
				{
					Path = path,
					Title = TitleOf(path, CultureInfo.CurrentUICulture.Name),
					Steps = steps.TryGetValue(path.LmsPathId, out var count) ? count : 0,
					Assigned = mine.Select(a => a.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
					Completed = mine.Count(a => a.CompletedOn.HasValue)
				});
			}

			return rows;
		}

		private static bool Belongs(LmsPathAssignment assignment, LmsPath path) =>
			assignment != null && (assignment.LmsPathId == path.LmsPathId || (!assignment.LmsPathId.HasValue && string.Equals(assignment.PathKey, path.AssignmentKey, StringComparison.OrdinalIgnoreCase)));

		public async Task<LmsPathDetail> GetPathAsync(LmsActor actor, int lmsPathId)
		{
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.ModuleOn)
				return null;

			var path = await _paths.GetVisibleByIdAsync(actor.DepartmentId, lmsPathId);
			if (path == null || path.IsDeleted || (!path.IsSystem && !actor.Gate.FlagOn))
				return null;

			var steps = await _steps.GetForPathsAsync(new[] { path.LmsPathId }) ?? new List<LmsPathStep>();
			var titles = await StepTitlesAsync(actor.DepartmentId, steps, CultureInfo.CurrentUICulture.Name);
			return new LmsPathDetail
			{
				Path = path,
				Title = TitleOf(path, CultureInfo.CurrentUICulture.Name),
				Steps = steps.OrderBy(s => s.SortOrder).Select(s => new LmsPathStepView { Step = s, Title = titles.TryGetValue(s.SortOrder, out var t) ? t : null }).ToList(),
				CanEdit = !path.IsSystem && actor.MayManage && actor.Gate.CanAuthor,
				CanAssign = CanAssign(actor, path)
			};
		}

		private static bool CanAssign(LmsActor actor, LmsPath path) =>
			actor != null && actor.MayAssignAcademy && LmsLapseRules.CanAssignNew(actor.Gate, path.IsSystem);

		private async Task<Dictionary<int, string>> StepTitlesAsync(int departmentId, IEnumerable<LmsPathStep> steps, string locale)
		{
			var titles = new Dictionary<int, string>();
			var list = (steps ?? Enumerable.Empty<LmsPathStep>()).ToList();
			var courseIds = list.Where(s => s.Kind == (int)LmsPathStepKinds.Course).Select(s => s.TargetRefId).Distinct().ToList();
			var courses = courseIds.Count == 0 ? new List<LmsCourse>() : await _courses.GetVisibleByIdsAsync(departmentId, courseIds) ?? new List<LmsCourse>();
			foreach (var step in list)
			{
				string title = null;
				if (step.Kind == (int)LmsPathStepKinds.Course)
				{
					var course = courses.FirstOrDefault(c => c.LmsCourseId == step.TargetRefId);
					title = course == null ? null : course.IsSystem ? _academy.Text(AcademyCourseManifest.LessonIdFromSystemKey(course.SystemKey) + ".Title", locale) : course.Title;
				}
				else if (step.Kind == (int)LmsPathStepKinds.Training)
				{
					var training = await _trainings.GetTrainingByIdAsync(step.TargetRefId);
					title = training != null && training.DepartmentId == departmentId ? training.Name : null;
				}

				titles[step.SortOrder] = title ?? "#" + step.TargetRefId.ToString(CultureInfo.InvariantCulture);
			}

			return titles;
		}

		public async Task<LmsResult> SavePathAsync(LmsActor actor, LmsPathInput input, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (actor.Gate == null || !actor.Gate.CanAuthor)
				return LmsResult.Fail(LmsErrors.AddonRequired);
			if (input == null || string.IsNullOrWhiteSpace(input.Name))
				return new LmsResult { Error = LmsErrors.InvalidInput, Value = "Err.NameRequired" };

			LmsPath path;
			if (input.LmsPathId > 0)
			{
				path = await _paths.GetVisibleByIdAsync(actor.DepartmentId, input.LmsPathId);
				if (path == null || path.IsDeleted || path.IsSystem || path.DepartmentId != actor.DepartmentId)
					return LmsResult.Fail(LmsErrors.NotFound);
			}
			else
				path = new LmsPath { DepartmentId = actor.DepartmentId, CreatedOn = DateTime.UtcNow, CreatedByUserId = actor.UserId };

			var steps = new List<LmsPathStep>();
			var order = 0;
			foreach (var input_ in input.Steps ?? new List<LmsPathStepInput>())
			{
				if (input_ == null || input_.TargetRefId <= 0)
					continue;
				order++;
				steps.Add(new LmsPathStep
				{
					DepartmentId = actor.DepartmentId,
					SortOrder = order,
					Kind = (int)input_.Kind,
					TargetRefId = input_.TargetRefId,
					PrerequisiteOrders = NullIfEmpty(LmsPathRules.FormatOrders(LmsPathRules.ParseOrders(input_.Prerequisites))),
					ElectiveGroup = input_.ElectiveGroup.HasValue && input_.ElectiveGroup.Value > 0 ? input_.ElectiveGroup : null,
					ElectiveRequired = input_.ElectiveGroup.HasValue && input_.ElectiveGroup.Value > 0 ? Math.Max(1, input_.ElectiveRequired) : 0
				});
			}

			var problem = LmsPathRules.Validate(steps);
			if (problem != null)
				return new LmsResult { Error = LmsErrors.InvalidInput, Value = "Err." + problem };

			// Every target must be one the department can use: its own courses, Academy lessons it can see, its own trainings.
			var courseIds = steps.Where(s => s.Kind == (int)LmsPathStepKinds.Course).Select(s => s.TargetRefId).Distinct().ToList();
			var courses = courseIds.Count == 0 ? new List<LmsCourse>() : await _courses.GetVisibleByIdsAsync(actor.DepartmentId, courseIds) ?? new List<LmsCourse>();
			foreach (var step in steps)
			{
				if (step.Kind == (int)LmsPathStepKinds.Course)
				{
					var course = courses.FirstOrDefault(c => c.LmsCourseId == step.TargetRefId);
					if (course == null || course.IsDeleted || (course.IsSystem && !await _entitlements.IsVisibleAsync(actor.DepartmentId, course)))
						return new LmsResult { Error = LmsErrors.InvalidInput, Value = "Err.PathInvalidStep" };
					step.TargetSystemKey = course.IsSystem ? course.SystemKey : null;
				}
				else
				{
					var training = await _trainings.GetTrainingByIdAsync(step.TargetRefId);
					if (training == null || training.DepartmentId != actor.DepartmentId)
						return new LmsResult { Error = LmsErrors.InvalidInput, Value = "Err.PathInvalidStep" };
				}
			}

			path.IssuesCertificationTypeId = null;
			if (input.IssuesCertificationTypeId.HasValue && input.IssuesCertificationTypeId.Value > 0)
			{
				var type = await _certifications.GetCertificationTypeByIdAsync(input.IssuesCertificationTypeId.Value);
				if (type == null || type.DepartmentId != actor.DepartmentId || type.IsDeleted || !type.IsActive || type.AppliesTo != (int)CertificationAppliesTo.Person)
					return new LmsResult { Error = LmsErrors.InvalidInput, Value = "Err.CertificationType" };
				path.IssuesCertificationTypeId = type.DepartmentCertificationTypeId;
			}

			var before = path.LmsPathId > 0 ? JsonConvert.SerializeObject(new { path.Name, path.IssuesCertificationTypeId }) : null;
			path.Name = input.Name.Trim().Length > LmsRequirement.MaxNameLength ? input.Name.Trim().Substring(0, LmsRequirement.MaxNameLength) : input.Name.Trim();
			path.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
			if (path.LmsPathId > 0)
			{
				path.UpdatedOn = DateTime.UtcNow;
				path.UpdatedByUserId = actor.UserId;
			}

			var saved = await _paths.SaveOrUpdateAsync(path, cancellationToken, true);
			await _steps.ReplaceAsync(actor.DepartmentId, saved.LmsPathId, steps, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsPathChanged, before,
				JsonConvert.SerializeObject(new { saved.LmsPathId, saved.Name, saved.IssuesCertificationTypeId, steps = steps.Select(s => new { s.SortOrder, s.Kind, s.TargetRefId, s.PrerequisiteOrders, s.ElectiveGroup }) }));
			return new LmsResult { Value = saved, Count = steps.Count };
		}

		private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

		public async Task<LmsResult> DeletePathAsync(LmsActor actor, int lmsPathId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (actor.Gate == null || !actor.Gate.CanAuthor)
				return LmsResult.Fail(LmsErrors.AddonRequired);

			var path = await _paths.GetVisibleByIdAsync(actor.DepartmentId, lmsPathId);
			if (path == null || path.IsDeleted || path.IsSystem || path.DepartmentId != actor.DepartmentId)
				return LmsResult.Fail(LmsErrors.NotFound);

			path.IsDeleted = true;
			path.UpdatedOn = DateTime.UtcNow;
			path.UpdatedByUserId = actor.UserId;
			await _paths.SaveOrUpdateAsync(path, cancellationToken, true);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsPathChanged, JsonConvert.SerializeObject(new { path.LmsPathId, path.Name }), JsonConvert.SerializeObject(new { deleted = true }));
			return new LmsResult { Count = 1 };
		}

		#endregion Authoring

		#region Assignment and progress

		public async Task<LmsAssignOutcome> AssignAsync(LmsActor actor, int lmsPathId, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc, int? lmsRequirementId = null,
			CancellationToken cancellationToken = default)
		{
			var outcome = new LmsAssignOutcome();
			var path = await _paths.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsPathId);
			if (path == null || path.IsDeleted)
				return new LmsAssignOutcome { Error = LmsErrors.NotFound };
			if (!CanAssign(actor, path))
				return new LmsAssignOutcome { Error = path.IsSystem ? LmsErrors.NotAuthorized : (actor?.MayAssignAcademy ?? false) ? LmsErrors.AddonRequired : LmsErrors.NotAuthorized };
			if (dueOnUtc.HasValue && dueOnUtc.Value < DateTime.UtcNow.AddMinutes(-5))
				return new LmsAssignOutcome { Error = LmsErrors.InvalidInput };

			var rules = ContentAudienceMatcher.Normalize(who);
			if (rules.Count == 0)
				return new LmsAssignOutcome { Error = LmsErrors.InvalidInput };

			var resolution = await _audienceService.ResolveAsync(actor.DepartmentId, rules);
			outcome.Skipped = resolution?.Skipped ?? new List<ContentAudienceSkip>();
			var members = resolution?.MemberUserIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (members.Count == 0)
				return outcome;

			var existing = (await _assignments.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsPathAssignment>())
				.Where(a => Belongs(a, path) && !a.CompletedOn.HasValue).Select(a => a.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			var added = new List<LmsPathAssignment>();
			foreach (var userId in members)
			{
				if (existing.Contains(userId))
				{
					outcome.AlreadyEnrolled++;
					continue;
				}

				var assignment = await _assignments.InsertAsync(new LmsPathAssignment
				{
					DepartmentId = actor.DepartmentId,
					PathKey = path.AssignmentKey,
					LmsPathId = path.LmsPathId,
					UserId = userId,
					AssignedByUserId = actor.UserId,
					AssignedOn = now,
					DueOn = dueOnUtc,
					LmsRequirementId = lmsRequirementId
				}, cancellationToken, true);
				added.Add(assignment);
				outcome.Created++;
			}

			if (added.Count > 0)
			{
				await AdvanceAsync(actor.DepartmentId, path, added, actor.Gate, actor.UserId, now, cancellationToken);
				Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsPathAssigned, null,
					JsonConvert.SerializeObject(new { path.LmsPathId, path = path.AssignmentKey, members = added.Count, dueOnUtc, lmsRequirementId }));
				foreach (var assignment in added)
					_eventAggregator?.SendMessage(new TrainingWorkflowEvent
					{
						DepartmentId = actor.DepartmentId, Change = TrainingWorkflowChanges.Assigned, UserId = assignment.UserId, Subject = "Path", PathId = path.LmsPathId,
						RequirementId = lmsRequirementId, TrainingName = TitleOf(path, "en"), DueOn = dueOnUtc, State = "Pending", OccurredOn = now
					});
			}

			return outcome;
		}

		public async Task<List<LmsMyPath>> GetMyPathsAsync(LmsActor actor, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var list = new List<LmsMyPath>();
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.ModuleOn)
				return list;

			var mine = await _assignments.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsPathAssignment>();
			if (mine.Count == 0)
				return list;

			var paths = (await _paths.GetVisibleAsync(actor.DepartmentId) ?? new List<LmsPath>()).Where(p => p.IsSystem || actor.Gate.FlagOn).ToList();
			var pairs = mine.Select(a => (Assignment: a, Path: paths.FirstOrDefault(p => Belongs(a, p)))).Where(p => p.Path != null).ToList();
			if (pairs.Count == 0)
				return list;

			// Steps that unlocked since are enrolled as the learner opens the page (department paths only while new LMS assignments are allowed).
			foreach (var group in pairs.GroupBy(p => p.Path.LmsPathId))
				await AdvanceAsync(actor.DepartmentId, group.First().Path, group.Select(p => p.Assignment).ToList(), actor.Gate, null, utcNow, cancellationToken);

			var steps = (await _steps.GetForPathsAsync(pairs.Select(p => p.Path.LmsPathId).Distinct()) ?? new List<LmsPathStep>()).ToLookup(s => s.LmsPathId);
			var completions = await CompletionsAsync(actor.DepartmentId, steps.SelectMany(g => g), new[] { actor.UserId });
			var enrollments = (await _enrollments.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsEnrollment>())
				.Where(e => e.IsOpen).GroupBy(e => e.LmsCourseId).ToDictionary(g => g.Key, g => g.First().LmsEnrollmentId);

			foreach (var (assignment, path) in pairs)
			{
				var pathSteps = steps[path.LmsPathId].ToList();
				var progress = LmsPathRules.Compute(pathSteps, completions.TryGetValue(actor.UserId, out var done) ? done : new Dictionary<(int, int), DateTime>());
				var titles = await StepTitlesAsync(actor.DepartmentId, pathSteps, CultureInfo.CurrentUICulture.Name);
				var view = new LmsMyPath { Assignment = assignment, Path = path, Title = TitleOf(path, CultureInfo.CurrentUICulture.Name), Progress = progress, StepTitles = titles };
				foreach (var step in pathSteps.Where(s => s.Kind == (int)LmsPathStepKinds.Course))
					if (enrollments.TryGetValue(step.TargetRefId, out var enrollmentId))
						view.StepEnrollmentIds[step.SortOrder] = enrollmentId;
				list.Add(view);
			}

			return list.OrderBy(p => p.Assignment.CompletedOn.HasValue).ThenBy(p => p.Assignment.DueOn ?? DateTime.MaxValue).ToList();
		}

		public async Task<List<LmsPathProgressRow>> GetProgressAsync(LmsActor actor, int lmsPathId)
		{
			var rows = new List<LmsPathProgressRow>();
			if (actor == null || !actor.MayAssignAcademy || actor.Gate == null || !actor.Gate.ModuleOn)
				return rows;

			var path = await _paths.GetVisibleByIdAsync(actor.DepartmentId, lmsPathId);
			if (path == null || (!path.IsSystem && !actor.Gate.FlagOn))
				return rows;

			var assignments = (await _assignments.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsPathAssignment>()).Where(a => Belongs(a, path)).ToList();
			if (assignments.Count == 0)
				return rows;

			var steps = await _steps.GetForPathsAsync(new[] { path.LmsPathId }) ?? new List<LmsPathStep>();
			var completions = await CompletionsAsync(actor.DepartmentId, steps, assignments.Select(a => a.UserId));
			foreach (var assignment in assignments.GroupBy(a => a.UserId, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(a => a.AssignedOn).First()))
			{
				var progress = LmsPathRules.Compute(steps, completions.TryGetValue(assignment.UserId, out var done) ? done : new Dictionary<(int, int), DateTime>());
				rows.Add(new LmsPathProgressRow
				{
					UserId = assignment.UserId, AssignedOn = assignment.AssignedOn, DueOn = assignment.DueOn, CompletedOn = assignment.CompletedOn ?? progress.CompletedOn,
					Done = progress.Done, Needed = progress.Needed
				});
			}

			return rows;
		}

		public async Task<int> AdvanceDepartmentAsync(int departmentId, LmsDepartmentGate gate, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var open = (await _assignments.GetForDepartmentAsync(departmentId) ?? new List<LmsPathAssignment>()).Where(a => !a.CompletedOn.HasValue).ToList();
			if (open.Count == 0)
				return 0;

			var paths = await _paths.GetVisibleAsync(departmentId) ?? new List<LmsPath>();
			var advanced = 0;
			foreach (var path in paths)
			{
				var assignments = open.Where(a => Belongs(a, path)).ToList();
				if (assignments.Count > 0)
					advanced += await AdvanceAsync(departmentId, path, assignments, gate, null, utcNow, cancellationToken);
			}

			return advanced;
		}

		/// <summary>
		/// Enrolls the course steps that are available and not open yet, and stamps finished paths (issuing the path's certification). Department
		/// paths enroll only while new LMS assignments are allowed (rule 3); Academy paths always. Returns the assignments that changed.
		/// </summary>
		private async Task<int> AdvanceAsync(int departmentId, LmsPath path, IReadOnlyList<LmsPathAssignment> assignments, LmsDepartmentGate gate, string assignedBy, DateTime utcNow,
			CancellationToken cancellationToken)
		{
			var steps = await _steps.GetForPathsAsync(new[] { path.LmsPathId }) ?? new List<LmsPathStep>();
			if (steps.Count == 0)
				return 0;

			var users = assignments.Select(a => a.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var completions = await CompletionsAsync(departmentId, steps, users);
			var courseIds = steps.Where(s => s.Kind == (int)LmsPathStepKinds.Course).Select(s => s.TargetRefId).Distinct().ToList();
			var openEnrollments = courseIds.Count == 0 ? new HashSet<(int, string)>()
				: (await _enrollments.GetForCoursesAsync(departmentId, courseIds) ?? new List<LmsEnrollment>()).Where(e => e.IsOpen)
					.Select(e => (e.LmsCourseId, e.UserId.ToUpperInvariant())).ToHashSet();
			var mayEnroll = LmsLapseRules.CanAssignNew(gate, path.IsSystem);
			var actor = new LmsActor { DepartmentId = departmentId, UserId = assignedBy ?? LmsRequirementService.SystemUserId, IsCurrentMember = true, IsDepartmentAdmin = true, Gate = gate };
			var toEnroll = new Dictionary<int, List<(string UserId, DateTime? Due)>>();
			var changed = 0;

			foreach (var assignment in assignments.Where(a => !a.CompletedOn.HasValue && !a.RemovedOn.HasValue))
			{
				var progress = LmsPathRules.Compute(steps, completions.TryGetValue(assignment.UserId, out var done) ? done : new Dictionary<(int, int), DateTime>());
				if (progress.Complete)
				{
					int? certificationId = null;
					if (!path.IsSystem && path.IssuesCertificationTypeId.HasValue)
					{
						try
						{
							certificationId = await _learner.IssueOrRenewCertificationAsync(departmentId, assignment.UserId, path.IssuesCertificationTypeId.Value, progress.CompletedOn ?? utcNow,
								cancellationToken);
						}
						catch (Exception ex)
						{
							Logging.LogException(ex, $"The certification of learning path {path.LmsPathId} could not be issued.");
						}
					}

					if (await _assignments.TryCompleteAsync(departmentId, assignment.LmsPathAssignmentId, progress.CompletedOn ?? utcNow, certificationId, cancellationToken))
					{
						changed++;
						assignment.CompletedOn = progress.CompletedOn ?? utcNow;
						Audit(departmentId, LmsRequirementService.SystemUserId, AuditLogTypes.LmsPathCompleted, null,
							JsonConvert.SerializeObject(new { path.LmsPathId, assignment.LmsPathAssignmentId, assignment.UserId, certificationId }));
						_eventAggregator?.SendMessage(new TrainingWorkflowEvent
						{
							DepartmentId = departmentId, Change = TrainingWorkflowChanges.Completed, UserId = assignment.UserId, Subject = "Path", PathId = path.LmsPathId,
							RequirementId = assignment.LmsRequirementId, TrainingName = TitleOf(path, "en"), DueOn = assignment.DueOn, CompletedOn = assignment.CompletedOn, State = "Compliant",
							OccurredOn = utcNow
						});
					}
					continue;
				}

				if (!mayEnroll)
					continue;
				foreach (var step in progress.Steps.Where(s => s.State == LmsPathStepStates.Available && s.Step.Kind == (int)LmsPathStepKinds.Course))
				{
					if (openEnrollments.Contains((step.Step.TargetRefId, assignment.UserId.ToUpperInvariant())))
						continue;
					if (!toEnroll.TryGetValue(step.Step.TargetRefId, out var who))
						toEnroll[step.Step.TargetRefId] = who = new List<(string, DateTime?)>();
					who.Add((assignment.UserId, assignment.DueOn.HasValue && assignment.DueOn.Value > utcNow ? assignment.DueOn : null));
				}
			}

			foreach (var pair in toEnroll)
				foreach (var byDue in pair.Value.GroupBy(w => w.Due))
				{
					try
					{
						var outcome = await _learner.AssignAsync(actor, pair.Key, byDue.Select(w => (IContentAudienceRule)new ContentAudienceRule(ContentAudienceKinds.User, w.UserId)).ToList(),
							byDue.Key, LmsEnrollmentSources.RolePath, path.AssignmentKey, cancellationToken);
						changed += outcome?.Created ?? 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"A step of learning path {path.LmsPathId} could not be enrolled.");
					}
				}

			return changed;
		}

		/// <summary>Each member's latest completion of each step target: (kind, target id) → completed on.</summary>
		private async Task<Dictionary<string, Dictionary<(int Kind, int RefId), DateTime>>> CompletionsAsync(int departmentId, IEnumerable<LmsPathStep> steps, IEnumerable<string> userIds)
		{
			var list = (steps ?? Enumerable.Empty<LmsPathStep>()).ToList();
			var wanted = userIds == null ? null : new HashSet<string>(userIds, StringComparer.OrdinalIgnoreCase);
			var result = new Dictionary<string, Dictionary<(int, int), DateTime>>(StringComparer.OrdinalIgnoreCase);

			void Add(int kind, IEnumerable<LmsRequirementFact> facts)
			{
				foreach (var fact in facts ?? Enumerable.Empty<LmsRequirementFact>())
				{
					if (string.IsNullOrWhiteSpace(fact.UserId) || (wanted != null && !wanted.Contains(fact.UserId)))
						continue;
					if (!result.TryGetValue(fact.UserId, out var mine))
						result[fact.UserId] = mine = new Dictionary<(int, int), DateTime>();
					var key = (kind, fact.RefId);
					if (!mine.TryGetValue(key, out var on) || fact.CompletedOn > on)
						mine[key] = fact.CompletedOn;
				}
			}

			var courseIds = list.Where(s => s.Kind == (int)LmsPathStepKinds.Course).Select(s => s.TargetRefId).Distinct().ToList();
			if (courseIds.Count > 0)
				Add((int)LmsPathStepKinds.Course, await _facts.GetCourseCompletionsAsync(departmentId, courseIds));
			var trainingIds = list.Where(s => s.Kind == (int)LmsPathStepKinds.Training).Select(s => s.TargetRefId).Distinct().ToList();
			if (trainingIds.Count > 0)
				Add((int)LmsPathStepKinds.Training, await _facts.GetTrainingCompletionsAsync(departmentId, trainingIds));
			return result;
		}

		public async Task<List<LmsRequirementFact>> GetCompletionFactsAsync(int departmentId, IEnumerable<int> lmsPathIds)
		{
			var facts = new List<LmsRequirementFact>();
			var ids = (lmsPathIds ?? Enumerable.Empty<int>()).Distinct().ToList();
			if (ids.Count == 0)
				return facts;

			var steps = (await _steps.GetForPathsAsync(ids) ?? new List<LmsPathStep>()).ToLookup(s => s.LmsPathId);
			var completions = await CompletionsAsync(departmentId, steps.SelectMany(g => g), null);
			foreach (var pathId in ids)
			{
				var pathSteps = steps[pathId].ToList();
				if (pathSteps.Count == 0)
					continue;
				foreach (var member in completions)
				{
					var progress = LmsPathRules.Compute(pathSteps, member.Value);
					if (progress.Complete && progress.CompletedOn.HasValue)
						facts.Add(new LmsRequirementFact { UserId = member.Key, RefId = pathId, CompletedOn = progress.CompletedOn.Value });
				}
			}

			return facts;
		}

		#endregion Assignment and progress

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_eventAggregator?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId ?? LmsRequirementService.SystemUserId,
				Type = type,
				Before = before,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
