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
using Resgrid.Services.Records;

namespace Resgrid.Services
{
	/// <summary>
	/// <see cref="IDispatchCaseReviewService"/> (Protocols P5 QA). Free with Protocols (§25.1 decision 1 keeps every protocol-side capability free
	/// except the LMS items it lists, and QA reads the case record every department already keeps for free): no add-on gate and no Records
	/// module gate. The rubric is a Records QA rubric (RmsQualityRubrics, DefinitionKey <see cref="DispatchCaseRubrics.DefinitionKey"/>) and
	/// the score its weighted formula; the per-question and per-instruction compliance is this phase's own. Reviewers are read from the
	/// department's permission row (Review Dispatch Cases, 134) through the protocol reader, never from token claims.
	/// </summary>
	public class DispatchCaseReviewService : IDispatchCaseReviewService
	{
		public const int QueueTake = 500;
		public const int MineTake = 200;
		public const int ReportTake = 5000;

		private readonly IDispatchCaseReviewsRepository _reviews;
		private readonly IRmsQualityRubricsRepository _rubrics;
		private readonly ICallProtocolsRepository _callProtocols;
		private readonly ICallsService _calls;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolCaseService _cases;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IDispatchCaseReviewNotifier _notifier;
		private readonly IEventAggregator _events;
		private readonly IPermissionsService _permissions;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentGroupsService _groups;
		private readonly Lazy<IProtectedReadService> _protectedRead;
		private readonly IProtectedGrantContext _grant;

		public DispatchCaseReviewService(IDispatchCaseReviewsRepository reviews, IRmsQualityRubricsRepository rubrics, ICallProtocolsRepository callProtocols,
			ICallsService calls, IProtocolAccessService access, IProtocolCaseService cases, IProtocolVersionsRepository versions, IAuthorizationService authorization,
			IDepartmentsService departments, IDispatchCaseReviewNotifier notifier, IEventAggregator events, IPermissionsService permissions, IPersonnelRolesService roles,
			IDepartmentGroupsService groups, Lazy<IProtectedReadService> protectedRead, IProtectedGrantContext grant)
		{
			_protectedRead = protectedRead;
			_grant = grant;
			_permissions = permissions;
			_roles = roles;
			_groups = groups;
			_reviews = reviews;
			_rubrics = rubrics;
			_callProtocols = callProtocols;
			_calls = calls;
			_access = access;
			_cases = cases;
			_versions = versions;
			_authorization = authorization;
			_departments = departments;
			_notifier = notifier;
			_events = events;
		}

		private async Task<ProtocolReader> ReaderAsync(int departmentId, string userId) =>
			await _access.GetReaderAsync(departmentId, userId) ?? ProtocolReader.Nobody(departmentId);

		public async Task<(bool IsReviewer, bool IsAdmin)> GetAccessAsync(int departmentId, string userId)
		{
			var reader = await ReaderAsync(departmentId, userId);
			return (reader.MayReviewDispatchCases, reader.IsCurrentMember && reader.IsDepartmentAdmin);
		}

		public async Task<HashSet<string>> GetReviewerUserIdsAsync(int departmentId)
		{
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (departmentId <= 0)
				return ids;

			foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
				if (!string.IsNullOrWhiteSpace(admin?.Id))
					ids.Add(admin.Id);

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var permission = await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ReviewDispatchCases);
			switch ((PermissionActions?)permission?.Action)
			{
				case PermissionActions.Everyone:
					ids.UnionWith(active);
					break;
				case PermissionActions.DepartmentAndGroupAdmins:
					foreach (var member in await _groups.GetAllGroupAdminsByDepartmentIdAsync(departmentId) ?? new List<DepartmentGroupMember>())
						if (!string.IsNullOrWhiteSpace(member?.UserId))
							ids.Add(member.UserId);
					break;
				case PermissionActions.DepartmentAdminsAndSelectRoles:
					foreach (var roleId in (permission.Data ?? string.Empty).Split(',').Select(x => int.TryParse(x.Trim(), out var id) ? id : 0).Where(id => id > 0).Distinct())
					{
						var role = await _roles.GetRoleByIdAsync(roleId);
						if (role == null || role.DepartmentId != departmentId)
							continue;
						foreach (var member in await _roles.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>())
							if (!string.IsNullOrWhiteSpace(member?.UserId))
								ids.Add(member.UserId);
					}
					break;
			}

			ids.IntersectWith(active);
			return ids;
		}

		#region Rubrics

		public async Task<List<RmsQualityRubric>> GetRubricsAsync(int departmentId, string userId, bool includeInactive = false)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
				return new List<RmsQualityRubric>();
			return await DispatchRubricsAsync(departmentId, includeInactive);
		}

		private async Task<List<RmsQualityRubric>> DispatchRubricsAsync(int departmentId, bool includeInactive) =>
			((await _rubrics.GetForDepartmentAsync(departmentId, includeInactive)) ?? Enumerable.Empty<RmsQualityRubric>())
				.Where(r => r != null && r.DepartmentId == departmentId && r.DeletedOn == null && DispatchCaseRubrics.IsDispatchRubric(r))
				.ToList();

		public async Task<RmsQualityRubric> CreateTemplateRubricAsync(int departmentId, string userId, DispatchCaseRubricTexts texts, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
				return null;
			// Administrators add rubrics; a reviewer may only seed the department's first one.
			if (!reader.IsDepartmentAdmin && (await DispatchRubricsAsync(departmentId, true)).Count > 0)
				return null;
			return await InsertTemplateAsync(departmentId, userId, texts, cancellationToken);
		}

		private async Task<RmsQualityRubric> InsertTemplateAsync(int departmentId, string userId, DispatchCaseRubricTexts texts, CancellationToken cancellationToken)
		{
			texts ??= DispatchCaseRubricTexts.English;
			var now = DateTime.UtcNow;
			var rubric = new RmsQualityRubric
			{
				RmsQualityRubricId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				Name = Trim(texts.Name, 200) ?? DispatchCaseRubricTexts.English.Name,
				DefinitionKey = DispatchCaseRubrics.DefinitionKey,
				CriteriaJson = JsonConvert.SerializeObject(DispatchCaseRubrics.Template(texts)),
				SampleSize = 10,
				IsActive = true,
				CreatedOn = now,
				CreatedByUserId = userId,
				ModifiedOn = now,
				RowVersion = 1
			};
			await _rubrics.InsertAsync(rubric, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.DispatchCaseRubricSaved, null, JsonConvert.SerializeObject(new { rubric.RmsQualityRubricId, rubric.Name, template = true }));
			return rubric;
		}

		public async Task<RmsQualityRubric> SaveRubricAsync(int departmentId, string userId, string rubricId, string name, bool isActive, List<RmsQualityCriterion> criteria,
			CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.IsCurrentMember || !reader.IsDepartmentAdmin)
				return null;

			var items = DispatchCaseRubrics.Normalize(criteria);
			var cleanName = Trim(name, 200);
			if (items == null || cleanName == null)
				return null;

			var now = DateTime.UtcNow;
			RmsQualityRubric rubric;
			if (string.IsNullOrWhiteSpace(rubricId))
			{
				rubric = new RmsQualityRubric
				{
					RmsQualityRubricId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), DefinitionKey = DispatchCaseRubrics.DefinitionKey,
					SampleSize = 10, CreatedOn = now, CreatedByUserId = userId, RowVersion = 1
				};
			}
			else
			{
				rubric = await _rubrics.GetByIdForDepartmentAsync(departmentId, rubricId.Trim());
				if (rubric == null || rubric.DepartmentId != departmentId || rubric.DeletedOn != null || !DispatchCaseRubrics.IsDispatchRubric(rubric))
					return null;
				rubric.RowVersion++;
			}

			var before = string.IsNullOrWhiteSpace(rubricId) ? null : JsonConvert.SerializeObject(new { rubric.RmsQualityRubricId, rubric.Name, rubric.IsActive, rubric.CriteriaJson });
			rubric.Name = cleanName;
			rubric.IsActive = isActive;
			rubric.CriteriaJson = JsonConvert.SerializeObject(items);
			rubric.ModifiedOn = now;
			if (string.IsNullOrWhiteSpace(rubricId))
				await _rubrics.InsertAsync(rubric, cancellationToken, true);
			else
				await _rubrics.UpdateAsync(rubric, cancellationToken, true);

			Audit(departmentId, userId, AuditLogTypes.DispatchCaseRubricSaved, before, JsonConvert.SerializeObject(new { rubric.RmsQualityRubricId, rubric.Name, rubric.IsActive, rubric.CriteriaJson }));
			return rubric;
		}

		#endregion Rubrics

		#region Sampling

		private static DispatchCaseReviewResults ValidateRequest(DispatchCaseSampleRequest request)
		{
			if (request == null || request.FromUtc == default || request.ToUtc == default || request.ToUtc <= request.FromUtc ||
			    (request.ToUtc - request.FromUtc).TotalDays > DispatchCaseSampling.MaxRangeDays || request.FromUtc.Year < 2000 || request.ToUtc.Year > 9000)
				return DispatchCaseReviewResults.Invalid;
			if (request.Percent.HasValue && (request.Percent.Value < 1 || request.Percent.Value > 100))
				return DispatchCaseReviewResults.Invalid;
			if (!request.Percent.HasValue && (!request.Count.HasValue || request.Count.Value < 1 || request.Count.Value > DispatchCaseSampling.MaxSample))
				return DispatchCaseReviewResults.Invalid;
			return DispatchCaseReviewResults.Done;
		}

		private static DispatchCaseCandidateFilter FilterFor(DispatchCaseSampleRequest request) => new DispatchCaseCandidateFilter
		{
			FromUtc = DateTime.SpecifyKind(request.FromUtc, DateTimeKind.Utc),
			ToUtc = DateTime.SpecifyKind(request.ToUtc, DateTimeKind.Utc),
			DispatchProtocolId = request.DispatchProtocolId > 0 ? request.DispatchProtocolId : null,
			ProtocolVersionId = request.ProtocolVersionId > 0 ? request.ProtocolVersionId : null,
			Determinant = ProtocolDecisionGraphs.NormalizeDeterminant(request.Determinant),
			DispatcherUserId = string.IsNullOrWhiteSpace(request.DispatcherUserId) ? null : request.DispatcherUserId.Trim()
		};

		public Task<DispatchCaseSampleResult> PreviewSampleAsync(int departmentId, string userId, DispatchCaseSampleRequest request) =>
			RunSampleAsync(departmentId, userId, request, true, CancellationToken.None);

		public Task<DispatchCaseSampleResult> SampleAsync(int departmentId, string userId, DispatchCaseSampleRequest request, CancellationToken cancellationToken = default) =>
			RunSampleAsync(departmentId, userId, request, false, cancellationToken);

		private async Task<DispatchCaseSampleResult> RunSampleAsync(int departmentId, string userId, DispatchCaseSampleRequest request, bool preview, CancellationToken cancellationToken)
		{
			var result = new DispatchCaseSampleResult { IsPreview = preview };
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
			{
				result.Result = DispatchCaseReviewResults.Forbidden;
				return result;
			}

			result.Result = ValidateRequest(request);
			if (result.Result != DispatchCaseReviewResults.Done)
				return result;

			// The assignee must be a reviewer of this department; an officer is never handed their own calls.
			var assignee = string.IsNullOrWhiteSpace(request.AssignToUserId) ? null : request.AssignToUserId.Trim();
			if (assignee != null && !string.Equals(assignee, userId, StringComparison.OrdinalIgnoreCase) && !(await ReaderAsync(departmentId, assignee)).MayReviewDispatchCases)
			{
				result.Result = DispatchCaseReviewResults.Invalid;
				return result;
			}

			var candidates = await _reviews.GetCandidatesAsync(departmentId, FilterFor(request), DispatchCaseSampling.MaxCandidates) ?? new List<DispatchCaseCandidate>();
			result.Candidates = candidates.Count;
			var eligible = DispatchCaseSampling.Eligible(candidates, assignee, result);
			result.Eligible = eligible.Count;
			result.SampleSize = DispatchCaseSampling.Size(eligible.Count, request.Count, request.Percent);
			if (preview || result.SampleSize == 0)
				return result;

			var rubric = await RubricForSampleAsync(departmentId, userId, reader, request, cancellationToken);
			var criteria = rubric == null ? null : RecordsQualityReviewService.ParseCriteria(rubric.CriteriaJson);
			if (rubric == null || criteria == null || criteria.Count == 0)
			{
				result.Result = DispatchCaseReviewResults.NoRubric;
				return result;
			}

			var picked = DispatchCaseSampling.Pick(eligible, result.SampleSize, DispatchCaseSampling.NewSeed());
			var cases = await _reviews.GetCaseJsonAsync(departmentId, picked.Select(p => p.CallProtocolId).ToList()) ?? new Dictionary<int, string>();
			// ADP catalog 39: a sealed case is read with the sampler's grant; without one the review is still created, with its items deferred
			// until a reviewer with a grant opens it (the answers are never copied into the review).
			cases = await ResolveCasesAsync(departmentId, userId, cases, cancellationToken);
			var headers = new Dictionary<int, ProtocolCaseDefinition>();
			var versionIds = picked.Where(p => p.ProtocolVersionId.HasValue).Select(p => p.ProtocolVersionId.Value).Distinct().ToList();
			var labels = versionIds.Count == 0 ? new Dictionary<int, string>()
				: ((await _versions.GetByIdsAsync(departmentId, versionIds, false)) ?? new List<ProtocolVersion>())
					.Where(v => v != null && v.DepartmentId == departmentId).GroupBy(v => v.ProtocolVersionId).ToDictionary(g => g.Key, g => g.First().Label);

			var now = DateTime.UtcNow;
			foreach (var candidate in picked)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var json = cases.TryGetValue(candidate.CallProtocolId, out var stored) ? stored : null;
				var withheld = json == ProtectedDataEnvelope.RedactionValue;
				var record = withheld ? null : CallProtocolCase.Parse(json);
				if (record == null && !withheld)
					continue;
				if (record == null && !headers.ContainsKey(candidate.DispatchProtocolId))
					headers[candidate.DispatchProtocolId] = await _cases.GetDefinitionAsync(departmentId, candidate.DispatchProtocolId);
				var header = record == null ? headers[candidate.DispatchProtocolId] : null;

				var review = new DispatchCaseReview
				{
					DepartmentId = departmentId,
					CallId = candidate.CallId,
					CallProtocolId = candidate.CallProtocolId,
					DispatchProtocolId = candidate.DispatchProtocolId,
					ProtocolVersionId = candidate.ProtocolVersionId ?? record?.ProtocolVersionId,
					Determinant = candidate.Determinant ?? record?.Determinant,
					Overridden = candidate.Overridden || record?.Overridden == true,
					DispatcherUserId = candidate.DispatcherUserId,
					CallLoggedOn = candidate.LoggedOn,
					CallNumber = Trim(candidate.CallNumber, 50),
					CallName = Trim(candidate.CallName, DispatchCaseReview.CallNameMaxLength),
					ProtocolCode = Trim(record?.ProtocolCode ?? header?.Code, 50),
					ProtocolName = Trim(record?.ProtocolName ?? header?.Name, 200),
					VersionLabel = candidate.ProtocolVersionId.HasValue && labels.TryGetValue(candidate.ProtocolVersionId.Value, out var label) ? Trim(label, 50) : null,
					Status = (int)DispatchCaseReviewStatuses.Queued,
					RmsQualityRubricId = rubric.RmsQualityRubricId,
					RubricName = Trim(rubric.Name, 200),
					CriteriaJson = JsonConvert.SerializeObject(criteria),
					ItemsJson = DispatchCaseReviewItem.ToJson(DispatchCaseReviewItem.WithoutAnswers(DispatchCaseReviewItem.FromCase(record))),
					AssignedToUserId = assignee,
					SampledByUserId = userId,
					SampledOn = now,
					CreatedOn = now,
					ModifiedOn = now,
					RowVersion = 1
				};

				if (await _reviews.TryInsertAsync(review, cancellationToken))
					result.Sampled.Add(review);
			}

			Audit(departmentId, userId, AuditLogTypes.DispatchCasesSampled, null, JsonConvert.SerializeObject(new
			{
				sampled = result.Sampled.Count, result.Candidates, result.Eligible, result.AlreadyReviewed, from = request.FromUtc, to = request.ToUtc,
				protocolId = request.DispatchProtocolId, versionId = request.ProtocolVersionId, determinant = request.Determinant, dispatcher = request.DispatcherUserId,
				request.Count, request.Percent, rubric = rubric.RmsQualityRubricId, assignee
			}));
			return result;
		}

		private async Task<RmsQualityRubric> RubricForSampleAsync(int departmentId, string userId, ProtocolReader reader, DispatchCaseSampleRequest request, CancellationToken cancellationToken)
		{
			var rubrics = await DispatchRubricsAsync(departmentId, true);
			if (!string.IsNullOrWhiteSpace(request.RubricId))
				return rubrics.FirstOrDefault(r => r.IsActive && string.Equals(r.RmsQualityRubricId, request.RubricId.Trim(), StringComparison.OrdinalIgnoreCase));

			var active = rubrics.Where(r => r.IsActive).OrderBy(r => r.CreatedOn).FirstOrDefault();
			if (active != null)
				return active;

			// The first sample of a department with no dispatch case rubric creates the template (in the requester's language).
			return rubrics.Count == 0 && reader.MayReviewDispatchCases ? await InsertTemplateAsync(departmentId, userId, request.TemplateTexts, cancellationToken) : null;
		}

		#endregion Sampling

		#region Queue

		public async Task<DispatchCaseReviewSummary> GetSummaryAsync(int departmentId, string userId)
		{
			var summary = new DispatchCaseReviewSummary();
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
				return summary;

			var counts = await _reviews.GetStatusCountsAsync(departmentId) ?? new Dictionary<int, int>();
			int Count(DispatchCaseReviewStatuses status) => counts.TryGetValue((int)status, out var n) ? n : 0;
			summary.Queued = Count(DispatchCaseReviewStatuses.Queued);
			summary.InReview = Count(DispatchCaseReviewStatuses.InReview);
			summary.AwaitingAcknowledgment = Count(DispatchCaseReviewStatuses.Sent);
			summary.Acknowledged = Count(DispatchCaseReviewStatuses.Acknowledged);
			summary.AssignedToMe = (await _reviews.GetOpenAsync(departmentId, QueueTake) ?? new List<DispatchCaseReview>())
				.Count(r => string.Equals(r.AssignedToUserId, userId, StringComparison.OrdinalIgnoreCase));
			return summary;
		}

		public async Task<List<DispatchCaseReview>> GetQueueAsync(int departmentId, string userId, bool mineOnly)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
				return new List<DispatchCaseReview>();

			var open = (await _reviews.GetOpenAsync(departmentId, QueueTake) ?? new List<DispatchCaseReview>()).Where(r => r.DepartmentId == departmentId);
			return (mineOnly ? open.Where(r => string.Equals(r.AssignedToUserId, userId, StringComparison.OrdinalIgnoreCase)) : open).ToList();
		}

		public async Task<DispatchCaseReviewResults> TakeAsync(int departmentId, string userId, int reviewId, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
				return DispatchCaseReviewResults.Forbidden;

			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId);
			if (review == null || review.DepartmentId != departmentId)
				return DispatchCaseReviewResults.NotFound;
			if (!DispatchCaseReviewRules.IsOpen(review))
				return DispatchCaseReviewResults.AlreadySent;
			if (DispatchCaseReviewRules.IsOwnCase(userId, review.DispatcherUserId))
				return DispatchCaseReviewResults.OwnCase;
			if (string.Equals(review.AssignedToUserId, userId, StringComparison.OrdinalIgnoreCase))
				return DispatchCaseReviewResults.Done;
			if (!string.IsNullOrWhiteSpace(review.AssignedToUserId) && !reader.IsDepartmentAdmin)
				return DispatchCaseReviewResults.Conflict;

			return await _reviews.TryAssignAsync(departmentId, reviewId, userId, review.RowVersion, DateTime.UtcNow, cancellationToken)
				? DispatchCaseReviewResults.Done : DispatchCaseReviewResults.Conflict;
		}

		public async Task<DispatchCaseReviewResults> RemoveAsync(int departmentId, string userId, int reviewId, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
				return DispatchCaseReviewResults.Forbidden;

			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId);
			if (review == null || review.DepartmentId != departmentId)
				return DispatchCaseReviewResults.NotFound;
			if (!DispatchCaseReviewRules.IsOpen(review))
				return DispatchCaseReviewResults.AlreadySent;
			// Someone else's queue item stays theirs unless an administrator clears it.
			if (!string.IsNullOrWhiteSpace(review.AssignedToUserId) && !string.Equals(review.AssignedToUserId, userId, StringComparison.OrdinalIgnoreCase) && !reader.IsDepartmentAdmin)
				return DispatchCaseReviewResults.Conflict;

			if (!await _reviews.TryDeleteOpenAsync(departmentId, reviewId, cancellationToken))
				return DispatchCaseReviewResults.AlreadySent;

			Audit(departmentId, userId, AuditLogTypes.DispatchCaseReviewRemoved, JsonConvert.SerializeObject(new { review.DispatchCaseReviewId, review.CallId, review.CallProtocolId, review.Status }), null);
			return DispatchCaseReviewResults.Done;
		}

		#endregion Queue

		#region Review

		public async Task<DispatchCaseReviewDetail> GetDetailAsync(int departmentId, string userId, int reviewId)
		{
			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId);
			if (review == null || review.DepartmentId != departmentId)
				return null;

			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.IsCurrentMember)
				return null;

			var isDispatcher = DispatchCaseReviewRules.IsDispatcherOf(review, userId);
			var isReviewer = reader.MayReviewDispatchCases;
			if (isReviewer && !isDispatcher && !await _authorization.CanUserViewCallAsync(userId, review.CallId))
				isReviewer = false;
			// The dispatcher sees their review once it was sent; nobody else but a reviewer sees it at all.
			if (!isReviewer && !DispatchCaseReviewRules.DispatcherMaySee(review, userId))
				return null;

			var row = await _callProtocols.GetForDepartmentCallAsync(departmentId, review.CallId, review.CallProtocolId);
			var call = await _calls.GetCallByIdAsync(review.CallId);
			if (call != null && call.DepartmentId != departmentId)
				call = null;
			var record = await ReadCaseAsync(departmentId, userId, row, CancellationToken.None);
			var graph = review.ProtocolVersionId.HasValue ? await _cases.GetGraphForVersionAsync(departmentId, review.ProtocolVersionId.Value) : null;

			var detail = new DispatchCaseReviewDetail
			{
				Review = review,
				// The answers are shown from the case as this reader may see it (REDACTED without a grant under ADP), never from the review.
				Items = DispatchCaseReviewItem.FillAnswers(ItemsFor(review, record), record),
				Criteria = RecordsQualityReviewService.ParseCriteria(review.CriteriaJson),
				Findings = RecordsQualityReviewService.ParseFindings(review.FindingsJson),
				Case = record,
				CaseWithheld = row != null && CallProtocolProtection.IsWithheld(row),
				Graph = graph,
				AttachedOn = row?.AttachedOn,
				CallPriority = call?.Priority,
				StepLog = ProtocolStepLog.Parse(row?.StepLogJson),
				IsReviewer = isReviewer,
				IsDispatcher = isDispatcher
			};
			detail.IsOwnCase = DispatchCaseReviewRules.IsOwnCase(userId, review.DispatcherUserId, row?.AttachedByUserId, call?.ReportingUserId, record?.OverriddenByUserId);
			detail.CanScore = isReviewer && !detail.IsOwnCase && DispatchCaseReviewRules.IsOpen(review) &&
				(string.IsNullOrWhiteSpace(review.AssignedToUserId) || string.Equals(review.AssignedToUserId, userId, StringComparison.OrdinalIgnoreCase) || reader.IsDepartmentAdmin);
			detail.CanAcknowledge = DispatchCaseReviewRules.CanAcknowledge(review, userId);
			detail.CanComment = DispatchCaseReviewRules.CanComment(review, userId);
			var questions = DispatchCaseScoring.Tally(detail.Items, ProtocolDecisionNodeKinds.Question);
			var instructions = DispatchCaseScoring.Tally(detail.Items, ProtocolDecisionNodeKinds.Instruction);
			detail.QuestionCompliance = DispatchCaseScoring.Percent(questions.Count, questions.Points);
			detail.InstructionCompliance = DispatchCaseScoring.Percent(instructions.Count, instructions.Points);
			return detail;
		}

		public async Task<DispatchCaseReviewResults> SaveScoresAsync(int departmentId, string userId, int reviewId, DispatchCaseScoreInput input, bool send,
			CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases)
				return DispatchCaseReviewResults.Forbidden;
			if (input == null)
				return DispatchCaseReviewResults.Invalid;

			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId);
			if (review == null || review.DepartmentId != departmentId)
				return DispatchCaseReviewResults.NotFound;
			if (!DispatchCaseReviewRules.IsOpen(review))
				return DispatchCaseReviewResults.AlreadySent;
			if (!string.IsNullOrWhiteSpace(review.AssignedToUserId) && !string.Equals(review.AssignedToUserId, userId, StringComparison.OrdinalIgnoreCase) && !reader.IsDepartmentAdmin)
				return DispatchCaseReviewResults.Conflict;

			// Never one's own case: the dispatcher of record, who attached the protocol, who reported the call, or who overrode the determinant.
			var row = await _callProtocols.GetForDepartmentCallAsync(departmentId, review.CallId, review.CallProtocolId);
			var call = await _calls.GetCallByIdAsync(review.CallId);
			var record = await ReadCaseAsync(departmentId, userId, row, cancellationToken);
			if (DispatchCaseReviewRules.IsOwnCase(userId, review.DispatcherUserId, row?.AttachedByUserId, call?.DepartmentId == departmentId ? call.ReportingUserId : null, record?.OverriddenByUserId))
				return DispatchCaseReviewResults.OwnCase;
			if (!await _authorization.CanUserViewCallAsync(userId, review.CallId))
				return DispatchCaseReviewResults.Forbidden;

			var items = ItemsFor(review, record);
			foreach (var item in items)
			{
				if (item.NodeKey == null || input.Items == null || !input.Items.TryGetValue(item.NodeKey, out var score) || score == null)
					continue;
				if (!Enum.IsDefined(typeof(DispatchCaseCompliance), score.Compliance))
					return DispatchCaseReviewResults.Invalid;
				item.Compliance = score.Compliance;
				item.Note = Trim(score.Note, DispatchCaseReview.NoteMaxLength);
			}

			var criteria = RecordsQualityReviewService.ParseCriteria(review.CriteriaJson);
			var saved = RecordsQualityReviewService.ParseFindings(review.FindingsJson);
			var findings = new List<RmsQualityFinding>();
			foreach (var criterion in criteria.Where(c => c != null && !DispatchCaseRubrics.IsDerived(c.Key)))
			{
				// A criterion not posted keeps the score a draft saved for it.
				var posted = input.Findings?.FirstOrDefault(f => f != null && string.Equals(f.Key, criterion.Key, StringComparison.Ordinal))
					?? saved.FirstOrDefault(f => f != null && string.Equals(f.Key, criterion.Key, StringComparison.Ordinal));
				if (posted == null)
					continue;
				if (posted.Score < 0 || posted.Score > 100)
					return DispatchCaseReviewResults.Invalid;
				findings.Add(new RmsQualityFinding { Key = criterion.Key, Score = posted.Score, Note = Trim(posted.Note, DispatchCaseReview.NoteMaxLength) });
			}

			if (send && DispatchCaseScoring.Missing(criteria, findings, items).Count > 0)
				return DispatchCaseReviewResults.Incomplete;

			// Derived criteria are stored with the findings once they can be computed, so the record reads whole.
			var stored = new List<RmsQualityFinding>(findings);
			foreach (var criterion in criteria.Where(c => c != null && DispatchCaseRubrics.IsDerived(c.Key)))
			{
				var value = DispatchCaseScoring.Derived(criterion.Key, items);
				if (value.HasValue)
					stored.Add(new RmsQualityFinding { Key = criterion.Key, Score = value.Value });
			}

			var questions = DispatchCaseScoring.Tally(items, ProtocolDecisionNodeKinds.Question);
			var instructions = DispatchCaseScoring.Tally(items, ProtocolDecisionNodeKinds.Instruction);
			var now = DateTime.UtcNow;
			var expected = review.RowVersion;
			review.ItemsJson = DispatchCaseReviewItem.ToJson(DispatchCaseReviewItem.WithoutAnswers(items));
			review.FindingsJson = JsonConvert.SerializeObject(criteria.Select(c => stored.FirstOrDefault(f => f.Key == c.Key)).Where(f => f != null).ToList());
			review.Score = DispatchCaseScoring.Overall(criteria, findings, items);
			review.QuestionCount = questions.Count;
			review.QuestionPoints = questions.Points;
			review.InstructionCount = instructions.Count;
			review.InstructionPoints = instructions.Points;
			review.ReviewerComments = Trim(input.Comments, DispatchCaseReview.CommentsMaxLength);
			review.ReviewerUserId = userId;
			review.AssignedToUserId ??= userId;
			review.ModifiedOn = now;
			review.Status = (int)(send ? DispatchCaseReviewStatuses.Sent : DispatchCaseReviewStatuses.InReview);
			review.SentOn = send ? now : (DateTime?)null;

			if (!await _reviews.TrySaveScoresAsync(review, expected, cancellationToken))
				return DispatchCaseReviewResults.Conflict;

			if (send)
			{
				Audit(departmentId, userId, AuditLogTypes.DispatchCaseReviewSent, null, JsonConvert.SerializeObject(new
				{
					review.DispatchCaseReviewId, review.CallId, review.CallProtocolId, review.DispatchProtocolId, review.ProtocolVersionId, review.DispatcherUserId, review.Score,
					review.QuestionCount, review.QuestionPoints, review.InstructionCount, review.InstructionPoints
				}));
				await NotifyAsync(() => _notifier.NotifySentAsync(review, cancellationToken));
				ContentWorkflowContext.Publish(_events, WorkflowEvent(review, DispatchCaseWorkflowChanges.ReviewSent, now));
			}

			return DispatchCaseReviewResults.Done;
		}

		/// <summary>The review's items; a review sampled while its case was sealed gets them from the case once a reader may see it.</summary>
		private static List<DispatchCaseReviewItem> ItemsFor(DispatchCaseReview review, CallProtocolCase record)
		{
			var items = DispatchCaseReviewItem.Parse(review?.ItemsJson);
			return items.Count == 0 && record != null ? DispatchCaseReviewItem.WithoutAnswers(DispatchCaseReviewItem.FromCase(record)) : items;
		}

		/// <summary>
		/// ADP catalog 39: the case as this reader may see it. A sealed case resolves with the reader's grant; without one it is withheld (null),
		/// never handed over as an envelope.
		/// </summary>
		private async Task<CallProtocolCase> ReadCaseAsync(int departmentId, string userId, CallProtocol row, CancellationToken cancellationToken)
		{
			if (row == null)
				return null;
			if (CallProtocolProtection.IsSealed(row))
				await CallProtocolProtection.ResolveAsync(_protectedRead?.Value, departmentId, new[] { row }, _grant?.GrantToken, _grant?.UserId ?? userId, cancellationToken);
			return CallProtocolProtection.IsWithheld(row) ? null : CallProtocolCase.Parse(row.CaseJson);
		}

		/// <summary>The sampled cases resolved with the sampler's grant; a case that stays sealed reads REDACTED.</summary>
		private async Task<Dictionary<int, string>> ResolveCasesAsync(int departmentId, string userId, Dictionary<int, string> cases, CancellationToken cancellationToken)
		{
			var rows = cases.Select(c => new CallProtocol { CallProtocolId = c.Key, CaseJson = c.Value }).ToList();
			if (!rows.Any(CallProtocolProtection.IsSealed))
				return cases;
			await CallProtocolProtection.ResolveAsync(_protectedRead?.Value, departmentId, rows, _grant?.GrantToken, _grant?.UserId ?? userId, cancellationToken);
			CallProtocolProtection.Redact(rows);
			return rows.ToDictionary(r => r.CallProtocolId, r => r.CaseJson);
		}

		/// <summary>
		/// Workflow triggers 255-256: the review's ids, the protocol, the determinant and the scores; never a comment, a note or the call's
		/// name (the reviewer's and the dispatcher's words stay in the review).
		/// </summary>
		public static DispatchCaseWorkflowEvent WorkflowEvent(DispatchCaseReview review, DispatchCaseWorkflowChanges change, DateTime utcNow) => new DispatchCaseWorkflowEvent
		{
			DepartmentId = review.DepartmentId,
			Change = change,
			CallId = review.CallId,
			CallNumber = review.CallNumber,
			CallProtocolId = review.CallProtocolId,
			ProtocolId = review.DispatchProtocolId,
			ProtocolVersionId = review.ProtocolVersionId,
			ProtocolCode = review.ProtocolCode,
			ProtocolName = review.ProtocolName,
			VersionLabel = review.VersionLabel,
			Determinant = review.Determinant,
			Overridden = review.Overridden,
			ReviewId = review.DispatchCaseReviewId,
			ReviewScore = review.Score,
			QuestionCount = review.QuestionCount,
			QuestionPoints = review.QuestionPoints,
			InstructionCount = review.InstructionCount,
			InstructionPoints = review.InstructionPoints,
			UserId = review.DispatcherUserId,
			ReviewerUserId = review.ReviewerUserId,
			OccurredOn = utcNow
		};

		#endregion Review

		#region Dispatcher

		public async Task<List<DispatchCaseReview>> GetMineAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<DispatchCaseReview>();
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.IsCurrentMember)
				return new List<DispatchCaseReview>();

			// Only the member's own reviews, and only once sent: the repository filters, and the rule is applied again here.
			return ((await _reviews.GetSentForDispatcherAsync(departmentId, userId, MineTake)) ?? new List<DispatchCaseReview>())
				.Where(r => r.DepartmentId == departmentId && DispatchCaseReviewRules.DispatcherMaySee(r, userId))
				.ToList();
		}

		public async Task<DispatchCaseReviewResults> AcknowledgeAsync(int departmentId, string userId, int reviewId, string comment, CancellationToken cancellationToken = default)
		{
			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId);
			if (review == null || review.DepartmentId != departmentId || !DispatchCaseReviewRules.IsDispatcherOf(review, userId))
				return DispatchCaseReviewResults.NotFound;
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.IsCurrentMember)
				return DispatchCaseReviewResults.Forbidden;
			if (!review.IsSent)
				return DispatchCaseReviewResults.NotSent;
			if (review.Status == (int)DispatchCaseReviewStatuses.Acknowledged)
				return DispatchCaseReviewResults.AlreadyAcknowledged;

			var reply = Trim(comment, DispatchCaseReview.DispatcherCommentMaxLength);
			// Read before the write: the comment is the one reply only when there was none (the store keeps an earlier one).
			var replies = reply != null && string.IsNullOrWhiteSpace(review.DispatcherComment);
			var now = DateTime.UtcNow;
			if (!await _reviews.TryAcknowledgeAsync(departmentId, reviewId, userId, reply, now, cancellationToken))
				return DispatchCaseReviewResults.AlreadyAcknowledged;

			Audit(departmentId, userId, AuditLogTypes.DispatchCaseReviewAcknowledged, null, JsonConvert.SerializeObject(new { review.DispatchCaseReviewId, review.CallId, replied = replies }));
			// Once: the acknowledgment's compare-and-set above lets only the first request through.
			ContentWorkflowContext.Publish(_events, WorkflowEvent(review, DispatchCaseWorkflowChanges.ReviewAcknowledged, now));
			if (replies)
			{
				review.DispatcherComment = reply;
				review.DispatcherCommentOn = now;
				Audit(departmentId, userId, AuditLogTypes.DispatchCaseReviewReplied, null, JsonConvert.SerializeObject(new { review.DispatchCaseReviewId, review.CallId }));
				await NotifyAsync(() => _notifier.NotifyReplyAsync(review, cancellationToken));
			}

			return DispatchCaseReviewResults.Done;
		}

		public async Task<DispatchCaseReviewResults> CommentAsync(int departmentId, string userId, int reviewId, string comment, CancellationToken cancellationToken = default)
		{
			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId);
			if (review == null || review.DepartmentId != departmentId || !DispatchCaseReviewRules.IsDispatcherOf(review, userId))
				return DispatchCaseReviewResults.NotFound;
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.IsCurrentMember)
				return DispatchCaseReviewResults.Forbidden;
			if (!review.IsSent)
				return DispatchCaseReviewResults.NotSent;
			var reply = Trim(comment, DispatchCaseReview.DispatcherCommentMaxLength);
			if (reply == null)
				return DispatchCaseReviewResults.CommentRequired;
			if (!string.IsNullOrWhiteSpace(review.DispatcherComment))
				return DispatchCaseReviewResults.AlreadyCommented;

			var now = DateTime.UtcNow;
			if (!await _reviews.TryCommentAsync(departmentId, reviewId, userId, reply, now, cancellationToken))
				return DispatchCaseReviewResults.AlreadyCommented;

			review.DispatcherComment = reply;
			review.DispatcherCommentOn = now;
			Audit(departmentId, userId, AuditLogTypes.DispatchCaseReviewReplied, null, JsonConvert.SerializeObject(new { review.DispatchCaseReviewId, review.CallId }));
			await NotifyAsync(() => _notifier.NotifyReplyAsync(review, cancellationToken));
			return DispatchCaseReviewResults.Done;
		}

		#endregion Dispatcher

		#region Reports

		public async Task<DispatchCaseComplianceReport> GetReportAsync(int departmentId, string userId, DispatchCaseReportFilter filter)
		{
			var reader = await ReaderAsync(departmentId, userId);
			if (!reader.MayReviewDispatchCases || filter == null || filter.ToUtc <= filter.FromUtc || (filter.ToUtc - filter.FromUtc).TotalDays > 3 * DispatchCaseSampling.MaxRangeDays)
				return null;

			var rows = await _reviews.GetSentForReportAsync(departmentId, DateTime.SpecifyKind(filter.FromUtc, DateTimeKind.Utc), DateTime.SpecifyKind(filter.ToUtc, DateTimeKind.Utc),
				filter.DispatchProtocolId > 0 ? filter.DispatchProtocolId : null, filter.ProtocolVersionId > 0 ? filter.ProtocolVersionId : null,
				string.IsNullOrWhiteSpace(filter.DispatcherUserId) ? null : filter.DispatcherUserId.Trim(), ReportTake) ?? new List<DispatchCaseReview>();

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var names = ((await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId)) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			return DispatchCaseReports.Aggregate(rows.Where(r => r.DepartmentId == departmentId).ToList(), filter,
				id => id != null && names.TryGetValue(id, out var name) ? name : id,
				date => department == null ? date : DateTime.SpecifyKind(date, DateTimeKind.Utc).TimeConverter(department));
		}

		#endregion Reports

		private static async Task NotifyAsync(Func<Task<bool>> send)
		{
			try
			{
				await send();
			}
			catch (Exception ex)
			{
				// The review is saved; a notice that could not go out never undoes it (the member still finds it under My case reviews).
				Resgrid.Framework.Logging.LogException(ex, "Dispatch case review notice failed.");
			}
		}

		private static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var text = value.Trim();
			return text.Length > max ? text.Substring(0, max) : text;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}

	/// <summary>
	/// <see cref="IDispatchCaseReviewNotifier"/>: push + email through ICommunicationService with the push event code "PQ:{reviewId}" and the web
	/// link in the text (the older apps open it in the browser). Active members only; nothing goes out where the department cannot transmit.
	/// The text names the call and protocol, never the score or the comments. The text comes from ContentNotices in the recipient's language
	/// (round 9C).
	/// </summary>
	public class DispatchCaseReviewNotifier : IDispatchCaseReviewNotifier
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserProfileService _profiles;

		public DispatchCaseReviewNotifier(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings,
			IUserProfileService profiles)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_profiles = profiles;
		}

		public static string EventCode(int reviewId) => "PQ:" + reviewId.ToString(System.Globalization.CultureInfo.InvariantCulture);

		public static string ReviewLink(int reviewId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/DispatchCaseReviews/Review/" + reviewId.ToString(System.Globalization.CultureInfo.InvariantCulture);

		/// <summary>"call 26-1, protocol P-12 Chest pain" in <paramref name="culture"/> (English when none is given).</summary>
		public static string Subject(DispatchCaseReview review, System.Globalization.CultureInfo culture = null) =>
			Resgrid.Localization.Areas.User.ContentNotices.ContentNoticeText.Format("CaseReview.Subject", culture,
				review?.CallNumber ?? review?.CallId.ToString(System.Globalization.CultureInfo.InvariantCulture), (review?.ProtocolCode + " " + review?.ProtocolName).Trim());

		public Task<bool> NotifySentAsync(DispatchCaseReview review, CancellationToken cancellationToken = default) =>
			review == null ? Task.FromResult(false)
				: SendAsync(review.DepartmentId, review.DispatcherUserId, culture => new ContentNoticeDelivery.Notice(
					Resgrid.Localization.Areas.User.ContentNotices.ContentNoticeText.Get("CaseReview.Sent.Title", culture),
					Resgrid.Localization.Areas.User.ContentNotices.ContentNoticeText.Format("CaseReview.Sent.Message", culture, Subject(review, culture), ReviewLink(review.DispatchCaseReviewId))),
					review.DispatchCaseReviewId, cancellationToken);

		public Task<bool> NotifyReplyAsync(DispatchCaseReview review, CancellationToken cancellationToken = default) =>
			review == null || string.IsNullOrWhiteSpace(review.ReviewerUserId) ? Task.FromResult(false)
				: SendAsync(review.DepartmentId, review.ReviewerUserId, culture => new ContentNoticeDelivery.Notice(
					Resgrid.Localization.Areas.User.ContentNotices.ContentNoticeText.Get("CaseReview.Reply.Title", culture),
					Resgrid.Localization.Areas.User.ContentNotices.ContentNoticeText.Format("CaseReview.Reply.Message", culture, Subject(review, culture), ReviewLink(review.DispatchCaseReviewId))),
					review.DispatchCaseReviewId, cancellationToken);

		/// <summary>One active member, in their own language (<see cref="ContentNoticeDelivery"/>).</summary>
		private async Task<bool> SendAsync(int departmentId, string userId, Func<System.Globalization.CultureInfo, ContentNoticeDelivery.Notice> compose, int reviewId,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			return await ContentNoticeDelivery.SendAsync(_communication.Value, _departments, _settings, _profiles, departmentId, new[] { userId }, (culture, _) => compose(culture),
				EventCode(reviewId), "Dispatch case review notice", cancellationToken) > 0;
		}
	}
}
