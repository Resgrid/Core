using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// Learning recommendations (K3, protocols-training-knowledge-plan §13a "path recommendations from role, requirements and gaps"), in the
	/// LMS layer. Deterministic first: the signals are read without writing anything (no path step is enrolled, no requirement re-run) —
	/// the member's open learning requirements (T3), the learning paths they are on, the competency gap report of each framework (T3
	/// programs, courses tagged with a missing or aged competency) and the department courses offered to their roles — and ranked by
	/// <see cref="LmsRecommendationRanker"/>. Competency and role suggestions are only courses the member can act on (enrolled, or in their
	/// catalog). The optional explanation sends only each item's title and reasons (never the member's name or id) to the operator's model,
	/// is validated (no number the reasons do not hold) and is logged to AiGenerations; nothing is saved.
	/// </summary>
	public class LmsRecommendationService : ILmsRecommendationService
	{
		public const string Feature = "LmsRecommendations";
		public const int ReservedTokens = 4096;
		public const int MaxFrameworks = 5;

		private readonly ILmsRequirementService _requirements;
		private readonly ILmsPathAssignmentsRepository _assignments;
		private readonly ILmsPathsRepository _paths;
		private readonly ILmsCompetencyService _competencies;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseAudiencesRepository _audiences;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly IContentAudienceService _audienceService;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentsService _departments;
		private readonly IAiKnowledgeService _knowledge;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly TimeProvider _clock;

		public LmsRecommendationService(ILmsRequirementService requirements, ILmsPathAssignmentsRepository assignments, ILmsPathsRepository paths,
			ILmsCompetencyService competencies, ILmsCoursesRepository courses, ILmsCourseAudiencesRepository audiences, ILmsEnrollmentsRepository enrollments,
			IContentAudienceService audienceService, IPersonnelRolesService roles, IDepartmentsService departments, IAiKnowledgeService knowledge,
			IDepartmentDataProtectionService protection, IAdminAssistConversationProtection conversationProtection, IAiBackgroundAdmission admission, IAiUsageMeter usage,
			Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, TimeProvider clock)
		{
			_requirements = requirements;
			_assignments = assignments;
			_paths = paths;
			_competencies = competencies;
			_courses = courses;
			_audiences = audiences;
			_enrollments = enrollments;
			_audienceService = audienceService;
			_roles = roles;
			_departments = departments;
			_knowledge = knowledge;
			_protection = protection;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_clock = clock ?? TimeProvider.System;
		}

		#region Scope

		/// <summary>A member sees their own; LMS managers and holders of the records right (department-wide or their group) see their scope.</summary>
		public static bool MaySee(LmsActor actor, TrainingManagementScope scope, string memberUserId) =>
			actor != null && actor.IsCurrentMember && !string.IsNullOrWhiteSpace(memberUserId) &&
			(string.Equals(actor.UserId, memberUserId, StringComparison.OrdinalIgnoreCase) || actor.MayManage || scope?.CanReviewRecordsOf(memberUserId) == true);

		public async Task<List<LmsRecommendationMember>> GetMembersInScopeAsync(LmsActor actor, TrainingManagementScope scope)
		{
			var list = new List<LmsRecommendationMember>();
			if (actor == null || !actor.IsCurrentMember || !LmsCourseKnowledgeSource.GateOpen(actor))
				return list;

			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(actor.DepartmentId) ?? new List<PersonName>())
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			string NameOf(string userId) => names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : userId;

			list.Add(new LmsRecommendationMember { UserId = actor.UserId, Name = NameOf(actor.UserId) });
			// Officers address active members only (inactive-member policy); the active set keeps each id as stored.
			var active = await _departments.GetActiveMemberUserIdsAsync(actor.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			list.AddRange(active.Where(u => !string.Equals(u, actor.UserId, StringComparison.OrdinalIgnoreCase) && MaySee(actor, scope, u))
				.Select(u => new LmsRecommendationMember { UserId = u, Name = NameOf(u) })
				.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.UserId, StringComparer.Ordinal));
			return list;
		}

		#endregion Scope

		#region Recommendations

		public async Task<LmsRecommendationSet> GetForMemberAsync(LmsActor actor, TrainingManagementScope scope, string memberUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.IsCurrentMember)
				return LmsRecommendationSet.Fail(LmsErrors.NotAuthorized);
			if (!LmsCourseKnowledgeSource.GateOpen(actor))
				return LmsRecommendationSet.Fail(LmsErrors.AddonRequired);

			var member = string.IsNullOrWhiteSpace(memberUserId) ? actor.UserId : memberUserId.Trim();
			if (!MaySee(actor, scope, member))
				return LmsRecommendationSet.Fail(LmsErrors.NotAuthorized, member);
			var self = string.Equals(member, actor.UserId, StringComparison.OrdinalIgnoreCase);
			if (!self)
			{
				var active = await _departments.GetActiveMemberUserIdsAsync(actor.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var stored = active.FirstOrDefault(u => string.Equals(u, member, StringComparison.OrdinalIgnoreCase));
				if (stored == null)
					return LmsRecommendationSet.Fail(LmsErrors.NotFound, member);
				member = stored;
			}
			else
			{
				member = actor.UserId;
			}

			var set = new LmsRecommendationSet { UserId = member, Items = await RankAsync(actor.DepartmentId, member, self ? actor.Facts : null, utcNow) };
			set.CanExplain = set.Items.Count > 0 && await ExplainOpenAsync(actor.DepartmentId) == null;
			return set;
		}

		private async Task<List<LmsRecommendation>> RankAsync(int departmentId, string member, ContentAudienceMemberFacts facts, DateTime utcNow)
		{
			var signals = new List<LmsRecommendationSignal>();
			var titles = new Dictionary<(LmsRecommendationTargetKinds, int), string>();

			// 1. Open learning requirements (read-only: the requirement engine's own evaluation for the member).
			foreach (var mine in await _requirements.GetMyRequirementsAsync(departmentId, member, utcNow) ?? new List<LmsMyRequirement>())
			{
				var reason = mine?.Status == null || mine.Status.Satisfied ? (LmsRecommendationReasons?)null : mine.Status.State switch
				{
					LmsRequirementStates.Overdue => LmsRecommendationReasons.RequirementOverdue,
					LmsRequirementStates.AtRisk => LmsRecommendationReasons.RequirementAtRisk,
					LmsRequirementStates.Pending => LmsRecommendationReasons.RequirementDue,
					_ => (LmsRecommendationReasons?)null
				};
				if (reason == null)
					continue;
				var target = mine.CourseId.HasValue ? (LmsRecommendationTargetKinds.Course, mine.CourseId.Value)
					: mine.PathId.HasValue ? (LmsRecommendationTargetKinds.Path, mine.PathId.Value) : ((LmsRecommendationTargetKinds, int)?)null;
				if (target == null)
					continue;
				signals.Add(new LmsRecommendationSignal
				{
					TargetKind = target.Value.Item1, TargetId = target.Value.Item2, Reason = reason.Value, DueUtc = mine.Status.ShownDueUtc, Detail = mine.Requirement?.Name
				});
				if (!string.IsNullOrWhiteSpace(mine.TargetName))
					titles[target.Value] = mine.TargetName;
			}

			// 2. Learning paths the member is on and has not finished (assignments read as stored; nothing is enrolled here).
			var open = (await _assignments.GetForUserAsync(departmentId, member) ?? new List<LmsPathAssignment>())
				.Where(a => a.LmsPathId.HasValue && !a.RemovedOn.HasValue && !a.CompletedOn.HasValue && string.Equals(a.UserId, member, StringComparison.OrdinalIgnoreCase)).ToList();
			if (open.Count > 0)
			{
				var paths = (await _paths.GetVisibleAsync(departmentId) ?? new List<LmsPath>()).Where(p => !p.IsDeleted).ToDictionary(p => p.LmsPathId);
				foreach (var assignment in open)
				{
					if (!paths.TryGetValue(assignment.LmsPathId.Value, out var path))
						continue;
					signals.Add(new LmsRecommendationSignal
					{
						TargetKind = LmsRecommendationTargetKinds.Path, TargetId = path.LmsPathId, Reason = LmsRecommendationReasons.PathInProgress, DueUtc = assignment.DueOn, Detail = path.Name
					});
					if (!string.IsNullOrWhiteSpace(path.Name))
						titles[(LmsRecommendationTargetKinds.Path, path.LmsPathId)] = path.Name;
				}
			}

			// 3. Competency gaps: courses tagged with a competency the member lacks or holds only aged evidence for.
			var gapCourses = new List<LmsRecommendationSignal>();
			foreach (var framework in (await _competencies.GetFrameworksAsync(departmentId) ?? new List<LmsCompetencyFramework>())
				.Where(f => f != null && !f.IsDeleted).OrderBy(f => f.LmsCompetencyFrameworkId).Take(MaxFrameworks))
			{
				var report = await _competencies.GetMemberGapsAsync(departmentId, framework.LmsCompetencyFrameworkId, member, utcNow);
				var gaps = (report?.Rows ?? new List<LmsCompetencyGapRow>())
					.Where(r => r.State == LmsCompetencyEvidenceStates.None || r.State == LmsCompetencyEvidenceStates.Aged).ToDictionary(r => r.LmsCompetencyItemId);
				if (gaps.Count == 0)
					continue;
				foreach (var alignment in await _competencies.GetAlignmentsAsync(departmentId, framework.LmsCompetencyFrameworkId) ?? new List<LmsCompetencyAlignment>())
				{
					if (alignment == null || alignment.SubjectKind != (int)LmsCompetencySubjectKinds.Course || !gaps.TryGetValue(alignment.LmsCompetencyItemId, out var gap) ||
						!int.TryParse(alignment.SubjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var courseId))
						continue;
					gapCourses.Add(new LmsRecommendationSignal
					{
						TargetKind = LmsRecommendationTargetKinds.Course,
						TargetId = courseId,
						Reason = gap.State == LmsCompetencyEvidenceStates.None ? LmsRecommendationReasons.CompetencyMissing : LmsRecommendationReasons.CompetencyAged,
						Detail = string.Join(" ", new[] { gap.Code, gap.Label }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
					});
				}
			}

			// 4. Department courses offered to one of the member's roles.
			facts ??= await _audienceService.GetMemberFactsAsync(departmentId, member) ?? new ContentAudienceMemberFacts { UserId = member };
			var roles = ((await _roles.GetRolesForUserAsync(member, departmentId)) ?? new List<PersonnelRole>()).Where(r => r != null && r.DepartmentId == departmentId).ToList();
			facts.RoleIds ??= new HashSet<int>();
			foreach (var role in roles)
				facts.RoleIds.Add(role.PersonnelRoleId);
			var courses = (await _courses.GetForDepartmentAsync(departmentId) ?? new List<LmsCourse>()).Where(c => LmsCourseKnowledgeSource.IsIndexable(c, departmentId)).ToList();
			var audiences = (await _audiences.GetForDepartmentAsync(departmentId) ?? new List<LmsCourseAudience>()).ToLookup(a => a.LmsCourseId);
			var roleCourses = new List<LmsRecommendationSignal>();
			foreach (var course in courses.Where(c => c.IsSelfEnrollable))
			{
				foreach (var rule in audiences[course.LmsCourseId].Where(a => a.Kind == (int)ContentAudienceKinds.Role))
				{
					var role = roles.FirstOrDefault(r => string.Equals(r.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), rule.RefId, StringComparison.Ordinal));
					if (role != null)
						roleCourses.Add(new LmsRecommendationSignal { TargetKind = LmsRecommendationTargetKinds.Course, TargetId = course.LmsCourseId, Reason = LmsRecommendationReasons.RoleAudience, Detail = role.Name });
				}
			}

			// The member's enrollments: completed courses drop out unless a requirement still needs them; open ones offer "Continue".
			var enrollments = (await _enrollments.GetForUserAsync(departmentId, member) ?? new List<LmsEnrollment>())
				.Where(e => string.Equals(e.UserId, member, StringComparison.OrdinalIgnoreCase) && e.Status != (int)LmsEnrollmentStatuses.Withdrawn).ToList();
			var completed = new HashSet<int>(enrollments.Where(e => e.Status == (int)LmsEnrollmentStatuses.Completed).Select(e => e.LmsCourseId));
			var openEnrollments = enrollments.Where(e => e.IsOpen).GroupBy(e => e.LmsCourseId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.LmsEnrollmentId).First().LmsEnrollmentId);
			var enrolled = new HashSet<int>(enrollments.Select(e => e.LmsCourseId));

			// Competency and role suggestions only for courses the member can act on: enrolled, or in their catalog.
			var actorForCatalog = new LmsActor { DepartmentId = departmentId, UserId = member, IsCurrentMember = true, Facts = facts };
			var actionable = new HashSet<int>(courses.Where(c => enrolled.Contains(c.LmsCourseId) || LmsCourseKnowledgeSource.InCatalog(actorForCatalog, c, audiences[c.LmsCourseId]))
				.Select(c => c.LmsCourseId));
			signals.AddRange(gapCourses.Concat(roleCourses).Where(s => actionable.Contains(s.TargetId)));

			// Titles: the course rows (department courses published and in force, or the Academy's system courses).
			// A course is shown only as the store returns it now: published, in force, the department's or the Academy's (never deleted or retired).
			var courseIds = signals.Where(s => s.TargetKind == LmsRecommendationTargetKinds.Course).Select(s => s.TargetId).Distinct().ToList();
			var systemIds = new HashSet<int>();
			var named = new Dictionary<int, string>();
			if (courseIds.Count > 0)
			{
				foreach (var course in await _courses.GetVisibleByIdsAsync(departmentId, courseIds) ?? new List<LmsCourse>())
				{
					if (course == null || course.IsDeleted || course.Status != (int)ProtocolStatuses.Published || !course.CurrentVersionId.HasValue ||
						(course.DepartmentId != departmentId && !course.IsSystem))
						continue;
					var key = (LmsRecommendationTargetKinds.Course, course.LmsCourseId);
					var title = !string.IsNullOrWhiteSpace(course.Title) ? course.Title : titles.TryGetValue(key, out var requirementTitle) ? requirementTitle : null;
					if (!string.IsNullOrWhiteSpace(title))
						named[course.LmsCourseId] = title;
					if (course.IsSystem)
						systemIds.Add(course.LmsCourseId);
				}
			}
			foreach (var id in courseIds)
			{
				if (named.TryGetValue(id, out var title))
					titles[(LmsRecommendationTargetKinds.Course, id)] = title;
				else
					titles.Remove((LmsRecommendationTargetKinds.Course, id));
			}

			return LmsRecommendationRanker.Rank(signals, titles, completed, openEnrollments, systemIds);
		}

		#endregion Recommendations

		#region Explanation

		/// <summary>Null when the explanation may run; else why not (the Enhanced AI gate, the model, Advanced Data Protection).</summary>
		private async Task<string> ExplainOpenAsync(int departmentId)
		{
			if (!await _knowledge.IsEntitledAsync(departmentId))
				return KnowledgeOutcomes.NotEntitled;
			if (!AiKnowledgeService.OperatorConfigured())
				return KnowledgeOutcomes.Unconfigured;
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, departmentId))
				return KnowledgePracticeOutcomes.KeptLocal;
			return null;
		}

		/// <summary>One recommendation's reasons as plain English facts (the model writes in the member's language from them).</summary>
		public static string Facts(LmsRecommendation item)
		{
			var text = new StringBuilder();
			foreach (var reason in item?.Reasons ?? new List<LmsRecommendationSignal>())
			{
				var detail = string.IsNullOrWhiteSpace(reason.Detail) ? string.Empty : "'" + reason.Detail.Trim() + "'";
				var due = reason.DueUtc.HasValue ? " (due " + reason.DueUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")" : string.Empty;
				text.Append(reason.Reason switch
				{
					LmsRecommendationReasons.RequirementOverdue => "learning requirement " + detail + " is overdue" + due,
					LmsRecommendationReasons.RequirementAtRisk => "learning requirement " + detail + " is at risk" + due,
					LmsRecommendationReasons.RequirementDue => "learning requirement " + detail + " is not yet met" + due,
					LmsRecommendationReasons.PathInProgress => "learning path " + detail + " is in progress" + due,
					LmsRecommendationReasons.CompetencyMissing => "competency " + detail + " has no evidence yet",
					LmsRecommendationReasons.CompetencyAged => "competency " + detail + " has evidence older than the framework allows",
					LmsRecommendationReasons.RoleAudience => "offered to the role " + detail,
					_ => string.Empty
				}).Append("; ");
			}
			return text.ToString().Trim().TrimEnd(';');
		}

		public async Task<LmsRecommendationExplanation> ExplainAsync(LmsActor actor, TrainingManagementScope scope, string memberUserId, string locale, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var explanation = new LmsRecommendationExplanation();
			var set = await GetForMemberAsync(actor, scope, memberUserId, utcNow, cancellationToken);
			if (set.Error != LmsErrors.None)
			{
				explanation.Outcome = set.Error == LmsErrors.AddonRequired ? KnowledgeOutcomes.NotEntitled : KnowledgeOutcomes.Invalid;
				return explanation;
			}
			if (set.Items.Count == 0)
			{
				explanation.Outcome = KnowledgeOutcomes.Invalid;
				return explanation;
			}
			var closed = await ExplainOpenAsync(actor.DepartmentId);
			if (closed != null)
			{
				explanation.Outcome = closed;
				return explanation;
			}

			var items = set.Items.Take(AiTutorPrompt.MaxRecommendations).ToList();
			var facts = items.Select((r, i) => new AiRecommendationFacts("R" + (i + 1).ToString(CultureInfo.InvariantCulture), r.Title, Facts(r))).ToList();
			var aiActor = new AdminAssistActor(actor.DepartmentId, actor.UserId, string.IsNullOrWhiteSpace(locale) ? "en" : locale);
			var admission = await KnowledgeModelCalls.ReserveAsync(_usage, _admission, _clock, actor.DepartmentId, Feature, ReservedTokens, cancellationToken);
			if (admission.Outcome != null)
			{
				explanation.Outcome = admission.Outcome;
				return explanation;
			}

			var used = 0;
			var ledger = "Unavailable";
			explanation.Outcome = KnowledgeOutcomes.Unavailable;
			var evidence = items.Select(r => LmsRecommendationExplanation.Key(r.TargetKind, r.TargetId)).ToList();
			try
			{
				try
				{
					await _conversationProtection.PreflightAsync(aiActor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					explanation.Outcome = KnowledgeOutcomes.Unconfigured;
					return explanation;
				}

				LlmResult result;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds, 5, 120)));
					try
					{
						result = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiTutorPrompt.ExplainMessages(facts, locale), new[] { AiTutorPrompt.Tool(2) },
							Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens, 256, 2048),
							AiCacheIsolation.SaltFor(actor.DepartmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return explanation;
					}
				}
				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, result.InputTokens + result.OutputTokens));

				IReadOnlyDictionary<string, string> texts;
				try
				{
					using var document = AiTutorPrompt.Parse(result, 2);
					texts = AiTutorPrompt.ValidateExplanations(document, facts);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is JsonException)
				{
					ledger = "InvalidOutput";
					explanation.Outcome = KnowledgePracticeOutcomes.Refused;
					explanation.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiTutorPrompt.ExplainVersion,
						"recommendations", evidence, KnowledgePracticeOutcomes.Refused, result.InputTokens, result.OutputTokens, cancellationToken);
					return explanation;
				}

				ledger = "Answered";
				explanation.Outcome = KnowledgeOutcomes.Answered;
				for (var i = 0; i < items.Count; i++)
					if (texts.TryGetValue(facts[i].Ref, out var text))
						explanation.Texts[LmsRecommendationExplanation.Key(items[i].TargetKind, items[i].TargetId)] = text;
				explanation.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiTutorPrompt.ExplainVersion,
					"recommendations", evidence, KnowledgeOutcomes.Answered, result.InputTokens, result.OutputTokens, cancellationToken);
				return explanation;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		#endregion Explanation
	}
}
