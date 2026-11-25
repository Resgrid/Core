using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// LMS course authoring on the shared content core (T2 item 1, §12.1, decision 8). Courses reuse P1's lifecycle state machine
	/// (<see cref="ProtocolLifecycle"/>: Draft → InReview → Approved → Published → Superseded/Retired, content editable only in
	/// Draft, numbering), its approval decisions, the content document (one sanitized html block) and the S0 signing layer (an L2
	/// signature per approval bound to the version's canonical hash, <see cref="SignatureUse.CourseApproval"/>). Authoring needs the
	/// LMS add-on (lapse rule 3); system-published content is seeded by <see cref="LmsAcademyService"/>, never authored here.
	/// </summary>
	public class LmsCourseService : ILmsCourseService
	{
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCourseVersionApprovalsRepository _approvals;
		private readonly ILmsCourseAudiencesRepository _audiences;
		private readonly ILmsCourseOwnersRepository _owners;
		private readonly ILmsCourseModulesRepository _modules;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsQuestionBanksRepository _banks;
		private readonly ILmsMediaAssetsRepository _media;
		private readonly IProtocolVersionsRepository _protocolVersions;
		private readonly ISignatureService _signatures;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _settingsService;
		private readonly IUserProfileService _profiles;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;
		private readonly Lazy<ICommunicationService> _communicationService;
		private readonly IKnowledgeIndexQueue _knowledgeQueue;

		public LmsCourseService(ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions, ILmsCourseVersionApprovalsRepository approvals,
			ILmsCourseAudiencesRepository audiences, ILmsCourseOwnersRepository owners, ILmsCourseModulesRepository modules, ILmsLessonsRepository lessons,
			ILmsEnrollmentsRepository enrollments, ILmsQuestionBanksRepository banks, ILmsMediaAssetsRepository media, IProtocolVersionsRepository protocolVersions,
			ISignatureService signatures, IDepartmentsService departmentsService, IDepartmentSettingsService settingsService, IUserProfileService profiles,
			IEventAggregator eventAggregator, IUnitOfWork unitOfWork, Lazy<ICommunicationService> communicationService, IKnowledgeIndexQueue knowledgeQueue)
		{
			_courses = courses;
			_versions = versions;
			_approvals = approvals;
			_audiences = audiences;
			_owners = owners;
			_modules = modules;
			_lessons = lessons;
			_enrollments = enrollments;
			_banks = banks;
			_media = media;
			_protocolVersions = protocolVersions;
			_signatures = signatures;
			_departmentsService = departmentsService;
			_settingsService = settingsService;
			_profiles = profiles;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
			_communicationService = communicationService;
			_knowledgeQueue = knowledgeQueue;
		}

		/// <summary>
		/// K3: the course's knowledge index entry is rebuilt from what is now in force (published, retired, audience or catalog changed). Never
		/// fails the caller; a lost request is repaired by worker 89's nightly pass.
		/// </summary>
		private async Task QueueKnowledgeReindexAsync(int departmentId, int lmsCourseId, CancellationToken cancellationToken)
		{
			if (_knowledgeQueue == null || departmentId <= 0 || lmsCourseId <= 0)
				return;
			try
			{
				await _knowledgeQueue.EnqueueAsync(departmentId, KnowledgeSourceKinds.Course, lmsCourseId.ToString(CultureInfo.InvariantCulture), cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"The knowledge reindex of course {lmsCourseId} could not be queued.");
			}
		}

		#region Reading

		public async Task<List<LmsCourseRow>> GetCourseRowsAsync(LmsActor actor)
		{
			var rows = new List<LmsCourseRow>();
			if (actor == null || !actor.IsCurrentMember)
				return rows;

			var owned = await _owners.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsCourseOwner>();
			foreach (var course in await _courses.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsCourse>())
			{
				var rights = actor.RightsOn(course, owned);
				if (!rights.Any)
					continue;

				var versions = await _versions.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId) ?? new List<LmsCourseVersion>();
				var enrollments = await _enrollments.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId) ?? new List<LmsEnrollment>();
				rows.Add(new LmsCourseRow
				{
					Course = course,
					Current = versions.FirstOrDefault(v => v.LmsCourseVersionId == course.CurrentVersionId),
					OpenDraft = versions.FirstOrDefault(IsOpenDraft),
					Enrolled = enrollments.Count(e => e.IsOpen),
					Completed = enrollments.Count(e => e.Status == (int)LmsEnrollmentStatuses.Completed),
					Rights = rights
				});
			}

			return rows;
		}

		private static bool IsOpenDraft(LmsCourseVersion v) =>
			v.Status == (int)ProtocolVersionStatuses.Draft || v.Status == (int)ProtocolVersionStatuses.InReview || v.Status == (int)ProtocolVersionStatuses.Approved;

		public Task<LmsCourse> GetCourseAsync(int departmentId, int lmsCourseId) => _courses.GetVisibleByIdAsync(departmentId, lmsCourseId);

		public async Task<List<LmsCourseVersion>> GetVersionsAsync(int departmentId, int lmsCourseId) =>
			await _versions.GetForCourseAsync(departmentId, lmsCourseId) ?? new List<LmsCourseVersion>();

		public async Task<LmsCourseOutline> GetOutlineAsync(int departmentId, int lmsCourseVersionId)
		{
			var version = await _versions.GetVisibleByIdAsync(departmentId, lmsCourseVersionId);
			if (version == null)
				return null;

			var course = await _courses.GetVisibleByIdAsync(departmentId, version.LmsCourseId);
			if (course == null)
				return null;

			var modules = await _modules.GetForVersionAsync(departmentId, lmsCourseVersionId) ?? new List<LmsCourseModule>();
			var lessons = await _lessons.GetForVersionAsync(departmentId, lmsCourseVersionId) ?? new List<LmsLesson>();
			return BuildOutline(course, version, modules, lessons);
		}

		internal static LmsCourseOutline BuildOutline(LmsCourse course, LmsCourseVersion version, IEnumerable<LmsCourseModule> modules, IEnumerable<LmsLesson> lessons)
		{
			var lessonList = (lessons ?? Enumerable.Empty<LmsLesson>()).ToList();
			return new LmsCourseOutline
			{
				Course = course,
				Version = version,
				Modules = (modules ?? Enumerable.Empty<LmsCourseModule>()).OrderBy(m => m.SortOrder).ThenBy(m => m.LmsCourseModuleId).Select(m => new LmsOutlineModule
				{
					Module = m,
					Lessons = lessonList.Where(l => l.LmsCourseModuleId == m.LmsCourseModuleId).OrderBy(l => l.SortOrder).ThenBy(l => l.LmsLessonId).ToList()
				}).ToList()
			};
		}

		public async Task<List<LmsCourseVersionApproval>> GetApprovalsAsync(int departmentId, int lmsCourseVersionId) =>
			await _approvals.GetForVersionAsync(departmentId, lmsCourseVersionId) ?? new List<LmsCourseVersionApproval>();

		public async Task<List<SignatureVerification>> GetApprovalSignaturesAsync(int departmentId, int lmsCourseVersionId)
		{
			var result = new List<SignatureVerification>();
			var outline = await GetOutlineAsync(departmentId, lmsCourseVersionId);
			if (outline == null || outline.Course.IsSystem)
				return result;

			// The shared signing layer cannot recompute a course hash itself (it never reads LMS tables), so the course service
			// recomputes the canonical hash and asks the layer to verify each signature against it.
			var hash = Hash(outline);
			foreach (var record in await _signatures.GetForSubjectAsync(departmentId, SignatureSubjectKinds.CourseVersion, lmsCourseVersionId.ToString(CultureInfo.InvariantCulture)) ?? new List<SignatureRecord>())
				result.Add(_signatures.Verify(record, hash));
			return result;
		}

		public async Task<List<LmsReviewItem>> GetReviewQueueAsync(LmsActor actor)
		{
			var items = new List<LmsReviewItem>();
			if (actor == null || !actor.MayManage)
				return items;

			foreach (var version in await _versions.GetInReviewAsync(actor.DepartmentId) ?? new List<LmsCourseVersion>())
			{
				var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
				if (course == null || course.IsDeleted)
					continue;
				var round = CurrentRound(await GetApprovalsAsync(actor.DepartmentId, version.LmsCourseVersionId));
				var step = round.FirstOrDefault(a => a.Decision == (int)ProtocolApprovalDecisions.Pending);
				items.Add(new LmsReviewItem
				{
					Course = course,
					Version = version,
					Step = step,
					CanDecide = step != null && actor.Gate?.CanAuthor == true &&
						!round.Any(a => a.Decision == (int)ProtocolApprovalDecisions.Approved && string.Equals(a.DecidedByUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
				});
			}

			return items;
		}

		public async Task<List<LmsCourseOwner>> GetOwnersAsync(int departmentId, int lmsCourseId) =>
			await _owners.GetForCourseAsync(departmentId, lmsCourseId) ?? new List<LmsCourseOwner>();

		public async Task<List<LmsCourseAudience>> GetAudienceAsync(int departmentId, int lmsCourseId) =>
			await _audiences.GetForCourseAsync(departmentId, lmsCourseId) ?? new List<LmsCourseAudience>();

		public async Task<LmsCourseRights> GetRightsAsync(LmsActor actor, LmsCourse course)
		{
			if (actor == null || course == null)
				return new LmsCourseRights();
			return actor.RightsOn(course, await GetOwnersAsync(actor.DepartmentId, course.LmsCourseId));
		}

		#endregion Reading

		#region Course and draft authoring

		public async Task<LmsResult> CreateAsync(LmsActor actor, LmsCourseInput input, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			var title = Truncate(input?.Title?.Trim(), LmsCourse.TitleMaxLength);
			if (string.IsNullOrWhiteSpace(title))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var now = DateTime.UtcNow;
			var course = new LmsCourse
			{
				DepartmentId = actor.DepartmentId,
				PublisherKind = (int)LmsPublisherKinds.Department,
				Title = title,
				Code = Truncate(input.Code?.Trim(), 50),
				Summary = Truncate(input.Summary?.Trim(), LmsCourse.SummaryMaxLength),
				Tags = NormalizeTags(input.Tags),
				Status = (int)ProtocolStatuses.Draft,
				IsSelfEnrollable = input.IsSelfEnrollable,
				CreatedOn = now,
				CreatedByUserId = actor.UserId
			};

			LmsCourseVersion version;
			var owns = _unitOfWork?.Transaction == null;
			_unitOfWork?.CreateOrGetConnection();
			try
			{
				await _courses.InsertAsync(course, cancellationToken, true);
				version = new LmsCourseVersion
				{
					DepartmentId = actor.DepartmentId,
					LmsCourseId = course.LmsCourseId,
					Status = (int)ProtocolVersionStatuses.Draft,
					Title = title,
					ContentJson = ProtocolContentDocument.FromHtml(string.Empty).ToJson(),
					ContentHtml = string.Empty,
					Locale = null,
					CreatedOn = now,
					CreatedByUserId = actor.UserId
				};
				await _versions.InsertAsync(version, cancellationToken, true);
				var module = new LmsCourseModule { DepartmentId = actor.DepartmentId, LmsCourseVersionId = version.LmsCourseVersionId, SortOrder = 1 };
				await _modules.InsertAsync(module, cancellationToken, true);
				await _owners.InsertAsync(new LmsCourseOwner
				{
					DepartmentId = actor.DepartmentId, LmsCourseId = course.LmsCourseId, UserId = actor.UserId, CanEdit = true, CanAssign = true, CanReport = true,
					AddedOn = now, AddedByUserId = actor.UserId
				}, cancellationToken, true);

				if (input.Audience != null && input.Audience.Count > 0)
					await _audiences.ReplaceAsync(actor.DepartmentId, course.LmsCourseId, input.Audience, cancellationToken);

				version.ContentHash = LmsCourseContent.ComputeHash(version, new[] { module }, Enumerable.Empty<LmsLesson>());
				await _versions.UpdateAsync(version, cancellationToken, true);

				if (owns) _unitOfWork?.CommitChanges();
			}
			catch
			{
				if (owns) _unitOfWork?.DiscardChanges();
				throw;
			}

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseCreated, null, Snapshot(course, version));
			return new LmsResult { Course = course, Version = version };
		}

		public async Task<LmsResult> UpdateCourseAsync(LmsActor actor, int lmsCourseId, LmsCourseInput input, CancellationToken cancellationToken = default)
		{
			var course = await _courses.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsCourseId);
			if (course == null || course.IsSystem || course.IsDeleted)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (!(await GetRightsAsync(actor, course)).Edit)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			var title = Truncate(input?.Title?.Trim(), LmsCourse.TitleMaxLength);
			if (string.IsNullOrWhiteSpace(title))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var before = JsonConvert.SerializeObject(new { course.Title, course.Code, course.Summary, course.Tags, course.IsSelfEnrollable });
			var expected = course.RowVersion;
			course.Title = title;
			course.Code = Truncate(input.Code?.Trim(), 50);
			course.Summary = Truncate(input.Summary?.Trim(), LmsCourse.SummaryMaxLength);
			course.Tags = NormalizeTags(input.Tags);
			course.IsSelfEnrollable = input.IsSelfEnrollable;
			course.UpdatedOn = DateTime.UtcNow;
			course.UpdatedByUserId = actor.UserId;
			if (!await _courses.TryUpdateAsync(course, expected, cancellationToken))
				return LmsResult.Fail(LmsErrors.StaleCopy);

			if (input.Audience != null)
				await _audiences.ReplaceAsync(actor.DepartmentId, course.LmsCourseId, input.Audience, cancellationToken);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseChanged, before, JsonConvert.SerializeObject(new { course.Title, course.Code, course.Summary, course.Tags, course.IsSelfEnrollable }));
			await QueueKnowledgeReindexAsync(actor.DepartmentId, course.LmsCourseId, cancellationToken);
			return new LmsResult { Course = course };
		}

		public async Task<LmsResult> SaveVersionSettingsAsync(LmsActor actor, int lmsCourseVersionId, LmsVersionSettingsInput input, CancellationToken cancellationToken = default)
		{
			var (course, version, error) = await EditableDraftAsync(actor, lmsCourseVersionId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);
			if (input == null || input.Hours < 0 || input.Hours > 1000)
				return LmsResult.Fail(LmsErrors.InvalidInput);
			if (input.ExpectedRowVersion != version.RowVersion)
				return new LmsResult { Error = LmsErrors.StaleCopy, Course = course, Version = version };

			var document = ProtocolContentDocument.FromHtml(input.OverviewHtml ?? string.Empty);
			version.Title = Truncate(string.IsNullOrWhiteSpace(input.Title) ? course.Title : input.Title.Trim(), LmsCourse.TitleMaxLength);
			version.ContentJson = document.ToJson();
			version.ContentHtml = document.Render();
			version.Hours = Math.Round(input.Hours, 2);
			version.CategoryId = input.CategoryId > 0 ? input.CategoryId : null;
			version.CeProvider = Truncate(input.CeProvider?.Trim(), 200);
			version.CeCourseNumber = Truncate(input.CeCourseNumber?.Trim(), 100);
			version.CertificateTemplateId = input.CertificateTemplateId > 0 ? input.CertificateTemplateId : null;
			version.IssuesCertificationTypeId = input.IssuesCertificationTypeId > 0 ? input.IssuesCertificationTypeId : null;
			version.UpdatedOn = DateTime.UtcNow;
			version.UpdatedByUserId = actor.UserId;

			return await SaveDraftWithHashAsync(course, version, input.ExpectedRowVersion, cancellationToken);
		}

		public async Task<LmsResult> StartDraftAsync(LmsActor actor, int lmsCourseId, CancellationToken cancellationToken = default)
		{
			var course = await _courses.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsCourseId);
			if (course == null || course.IsSystem || course.IsDeleted)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (!(await GetRightsAsync(actor, course)).Edit)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);

			var versions = await GetVersionsAsync(actor.DepartmentId, course.LmsCourseId);
			var open = versions.FirstOrDefault(IsOpenDraft);
			if (open != null)
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.DraftExists, Course = course, Version = open };

			var source = versions.FirstOrDefault(v => v.LmsCourseVersionId == course.CurrentVersionId) ?? versions.FirstOrDefault();
			var now = DateTime.UtcNow;
			var draft = new LmsCourseVersion
			{
				DepartmentId = actor.DepartmentId,
				LmsCourseId = course.LmsCourseId,
				Status = (int)ProtocolVersionStatuses.Draft,
				Title = source?.Title ?? course.Title,
				ContentJson = source?.ContentJson ?? ProtocolContentDocument.FromHtml(string.Empty).ToJson(),
				ContentHtml = source?.ContentHtml ?? string.Empty,
				Locale = source?.Locale,
				Hours = source?.Hours ?? 0,
				CategoryId = source?.CategoryId,
				CeProvider = source?.CeProvider,
				CeCourseNumber = source?.CeCourseNumber,
				CertificateTemplateId = source?.CertificateTemplateId,
				IssuesCertificationTypeId = source?.IssuesCertificationTypeId,
				CreatedOn = now,
				CreatedByUserId = actor.UserId
			};

			var owns = _unitOfWork?.Transaction == null;
			_unitOfWork?.CreateOrGetConnection();
			try
			{
				await _versions.InsertAsync(draft, cancellationToken, true);
				var (modules, lessons) = await CopyStructureAsync(actor.DepartmentId, source, draft, cancellationToken);
				draft.ContentHash = LmsCourseContent.ComputeHash(draft, modules, lessons);
				await _versions.UpdateAsync(draft, cancellationToken, true);
				if (owns) _unitOfWork?.CommitChanges();
			}
			catch
			{
				if (owns) _unitOfWork?.DiscardChanges();
				throw;
			}

			return new LmsResult { Course = course, Version = draft };
		}

		/// <summary>Copies the modules and lessons of <paramref name="source"/> into <paramref name="target"/> (new rows, same content).</summary>
		internal async Task<(List<LmsCourseModule>, List<LmsLesson>)> CopyStructureAsync(int departmentId, LmsCourseVersion source, LmsCourseVersion target, CancellationToken cancellationToken)
		{
			var modules = new List<LmsCourseModule>();
			var lessons = new List<LmsLesson>();
			if (source == null)
			{
				var empty = new LmsCourseModule { DepartmentId = target.DepartmentId, LmsCourseVersionId = target.LmsCourseVersionId, SortOrder = 1 };
				await _modules.InsertAsync(empty, cancellationToken, true);
				modules.Add(empty);
				return (modules, lessons);
			}

			var sourceLessons = await _lessons.GetForVersionAsync(departmentId, source.LmsCourseVersionId) ?? new List<LmsLesson>();
			foreach (var module in await _modules.GetForVersionAsync(departmentId, source.LmsCourseVersionId) ?? new List<LmsCourseModule>())
			{
				var copy = new LmsCourseModule { DepartmentId = target.DepartmentId, LmsCourseVersionId = target.LmsCourseVersionId, Title = module.Title, SortOrder = module.SortOrder };
				await _modules.InsertAsync(copy, cancellationToken, true);
				modules.Add(copy);
				foreach (var lesson in sourceLessons.Where(l => l.LmsCourseModuleId == module.LmsCourseModuleId))
				{
					var clone = JsonConvert.DeserializeObject<LmsLesson>(JsonConvert.SerializeObject(lesson));
					clone.LmsLessonId = 0;
					clone.DepartmentId = target.DepartmentId;
					clone.LmsCourseVersionId = target.LmsCourseVersionId;
					clone.LmsCourseModuleId = copy.LmsCourseModuleId;
					await _lessons.InsertAsync(clone, cancellationToken, true);
					lessons.Add(clone);
				}
			}

			return (modules, lessons);
		}

		public async Task<LmsResult> DiscardDraftAsync(LmsActor actor, int lmsCourseVersionId, CancellationToken cancellationToken = default)
		{
			var (course, version, error) = await EditableDraftAsync(actor, lmsCourseVersionId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);
			if ((await GetApprovalsAsync(actor.DepartmentId, lmsCourseVersionId)).Count > 0)
				return LmsResult.Fail(ProtocolLifecycleErrors.RetentionProtected);

			var owns = _unitOfWork?.Transaction == null;
			_unitOfWork?.CreateOrGetConnection();
			try
			{
				foreach (var lesson in await _lessons.GetForVersionAsync(actor.DepartmentId, lmsCourseVersionId) ?? new List<LmsLesson>())
					await _lessons.DeleteAsync(lesson, cancellationToken);
				foreach (var module in await _modules.GetForVersionAsync(actor.DepartmentId, lmsCourseVersionId) ?? new List<LmsCourseModule>())
					await _modules.DeleteAsync(module, cancellationToken);
				await _versions.DeleteAsync(version, cancellationToken);

				if (!course.CurrentVersionId.HasValue && (await GetVersionsAsync(actor.DepartmentId, course.LmsCourseId)).All(v => v.LmsCourseVersionId == lmsCourseVersionId))
				{
					var expected = course.RowVersion;
					course.IsDeleted = true;
					course.UpdatedOn = DateTime.UtcNow;
					course.UpdatedByUserId = actor.UserId;
					await _courses.TryUpdateAsync(course, expected, cancellationToken);
				}

				if (owns) _unitOfWork?.CommitChanges();
			}
			catch
			{
				if (owns) _unitOfWork?.DiscardChanges();
				throw;
			}

			return new LmsResult { Course = course };
		}

		public async Task<LmsResult> SaveModuleAsync(LmsActor actor, int lmsCourseVersionId, int lmsCourseModuleId, string title, CancellationToken cancellationToken = default)
		{
			var (course, version, error) = await EditableDraftAsync(actor, lmsCourseVersionId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);

			var modules = await _modules.GetForVersionAsync(actor.DepartmentId, lmsCourseVersionId) ?? new List<LmsCourseModule>();
			LmsCourseModule module;
			if (lmsCourseModuleId > 0)
			{
				module = modules.FirstOrDefault(m => m.LmsCourseModuleId == lmsCourseModuleId);
				if (module == null)
					return LmsResult.Fail(LmsErrors.NotFound);
				module.Title = Truncate(title?.Trim(), 250);
				await _modules.UpdateAsync(module, cancellationToken, true);
			}
			else
			{
				module = new LmsCourseModule
				{
					DepartmentId = actor.DepartmentId,
					LmsCourseVersionId = lmsCourseVersionId,
					Title = Truncate(title?.Trim(), 250),
					SortOrder = modules.Select(m => m.SortOrder).DefaultIfEmpty(0).Max() + 1
				};
				await _modules.InsertAsync(module, cancellationToken, true);
			}

			var result = await RehashAsync(course, version, cancellationToken);
			result.Value = module;
			return result;
		}

		public async Task<LmsResult> DeleteModuleAsync(LmsActor actor, int lmsCourseModuleId, CancellationToken cancellationToken = default)
		{
			var module = await _modules.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsCourseModuleId);
			if (module == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			var (course, version, error) = await EditableDraftAsync(actor, module.LmsCourseVersionId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);

			var modules = await _modules.GetForVersionAsync(actor.DepartmentId, version.LmsCourseVersionId) ?? new List<LmsCourseModule>();
			if (modules.Count <= 1)
				return LmsResult.Fail(LmsErrors.InvalidInput);

			foreach (var lesson in (await _lessons.GetForVersionAsync(actor.DepartmentId, version.LmsCourseVersionId) ?? new List<LmsLesson>()).Where(l => l.LmsCourseModuleId == lmsCourseModuleId))
				await _lessons.DeleteAsync(lesson, cancellationToken);
			await _modules.DeleteAsync(module, cancellationToken);
			return await RehashAsync(course, version, cancellationToken);
		}

		public async Task<LmsResult> SaveLessonAsync(LmsActor actor, LmsLessonInput input, CancellationToken cancellationToken = default)
		{
			if (input == null)
				return LmsResult.Fail(LmsErrors.InvalidInput);
			var (course, version, error) = await EditableDraftAsync(actor, input.LmsCourseVersionId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);

			var modules = await _modules.GetForVersionAsync(actor.DepartmentId, version.LmsCourseVersionId) ?? new List<LmsCourseModule>();
			if (!modules.Any(m => m.LmsCourseModuleId == input.LmsCourseModuleId))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var lessons = await _lessons.GetForVersionAsync(actor.DepartmentId, version.LmsCourseVersionId) ?? new List<LmsLesson>();
			LmsLesson lesson;
			if (input.LmsLessonId > 0)
			{
				lesson = lessons.FirstOrDefault(l => l.LmsLessonId == input.LmsLessonId);
				if (lesson == null)
					return LmsResult.Fail(LmsErrors.NotFound);
				if (lesson.LmsCourseModuleId != input.LmsCourseModuleId)
					lesson.SortOrder = lessons.Where(l => l.LmsCourseModuleId == input.LmsCourseModuleId).Select(l => l.SortOrder).DefaultIfEmpty(0).Max() + 1;
			}
			else
			{
				lesson = new LmsLesson
				{
					DepartmentId = actor.DepartmentId,
					LmsCourseVersionId = version.LmsCourseVersionId,
					SortOrder = lessons.Where(l => l.LmsCourseModuleId == input.LmsCourseModuleId).Select(l => l.SortOrder).DefaultIfEmpty(0).Max() + 1
				};
			}

			var validation = await ApplyLessonInputAsync(actor, lesson, input);
			if (validation != LmsErrors.None)
				return LmsResult.Fail(validation);

			await _lessons.SaveOrUpdateAsync(lesson, cancellationToken, true);
			var result = await RehashAsync(course, version, cancellationToken);
			result.Value = lesson;
			return result;
		}

		/// <summary>Validates the builder's input per lesson kind and copies it onto the lesson. None when it is acceptable.</summary>
		internal async Task<LmsErrors> ApplyLessonInputAsync(LmsActor actor, LmsLesson lesson, LmsLessonInput input)
		{
			var kind = (LmsLessonKinds)input.Kind;
			if (!Enum.IsDefined(typeof(LmsLessonKinds), input.Kind) || kind == LmsLessonKinds.Scorm || kind == LmsLessonKinds.Cmi5 || kind == LmsLessonKinds.LiveSession)
				return LmsErrors.InvalidInput;

			var title = Truncate(input.Title?.Trim(), LmsLesson.TitleMaxLength);
			if (string.IsNullOrWhiteSpace(title))
				return LmsErrors.InvalidInput;

			lesson.Kind = input.Kind;
			lesson.Title = title;
			lesson.LmsCourseModuleId = input.LmsCourseModuleId;
			lesson.IsRequired = kind == LmsLessonKinds.Survey ? input.IsRequired : input.IsRequired || kind == LmsLessonKinds.Assessment && !input.IsPreTest;
			lesson.MinimumSeatSeconds = Math.Max(0, Math.Min(24 * 3600, input.MinimumSeatSeconds));
			lesson.TranscriptText = Truncate(input.TranscriptText, 200000);

			// Reset everything kind-specific, then fill what this kind uses.
			lesson.ContentHtml = null;
			lesson.ContentJson = null;
			lesson.VideoSource = (int)LmsVideoSources.None;
			lesson.VideoUrl = null;
			lesson.LmsMediaAssetId = null;
			lesson.RequiredWatchPercent = 0;
			lesson.AvailableOffline = false;
			lesson.OfflineMaxBytes = null;
			lesson.OfflineExpiryDays = null;
			lesson.DocumentUrl = null;
			lesson.DocumentName = null;
			lesson.ProtocolVersionId = null;
			lesson.FreeResponsePrompt = null;
			lesson.FileRequestPrompt = null;
			lesson.LmsQuestionBankId = null;
			lesson.SurveyJson = null;
			lesson.IsPreTest = false;

			switch (kind)
			{
				case LmsLessonKinds.Blocks:
					var document = ProtocolContentDocument.FromHtml(input.ContentHtml ?? string.Empty);
					lesson.ContentJson = document.ToJson();
					lesson.ContentHtml = document.Render();
					lesson.FreeResponsePrompt = Truncate(input.FreeResponsePrompt?.Trim(), 2000);
					lesson.FileRequestPrompt = Truncate(input.FileRequestPrompt?.Trim(), 2000);
					if (string.IsNullOrWhiteSpace(StringHelpers.HtmlToPlainText(lesson.ContentHtml ?? string.Empty)) && string.IsNullOrWhiteSpace(lesson.FreeResponsePrompt) && string.IsNullOrWhiteSpace(lesson.FileRequestPrompt))
						return LmsErrors.InvalidInput;
					break;

				case LmsLessonKinds.Video:
					lesson.RequiredWatchPercent = Math.Max(0, Math.Min(100, input.RequiredWatchPercent));
					if (input.VideoSource == (int)LmsVideoSources.Embed)
					{
						var url = EmbedAllowList.NormalizeEmbedUrl(input.VideoUrl);
						if (url == null)
							return LmsErrors.InvalidInput;
						lesson.VideoSource = input.VideoSource;
						lesson.VideoUrl = url;
						// A third-party player reports no position, so a watched percent cannot be required of it.
						lesson.RequiredWatchPercent = 0;
					}
					else if (input.VideoSource == (int)LmsVideoSources.Hosted)
					{
						var asset = input.LmsMediaAssetId.HasValue ? await _media.GetByIdForDepartmentAsync(actor.DepartmentId, input.LmsMediaAssetId.Value) : null;
						if (asset == null || asset.Kind != (int)LmsMediaKinds.Video)
							return LmsErrors.InvalidInput;
						lesson.VideoSource = input.VideoSource;
						lesson.LmsMediaAssetId = asset.LmsMediaAssetId;
						lesson.AvailableOffline = input.AvailableOffline;
						if (input.AvailableOffline)
						{
							lesson.OfflineMaxBytes = input.OfflineMaxBytes > 0 ? input.OfflineMaxBytes : Config.LmsConfig.DefaultOfflineMaxBytes;
							lesson.OfflineExpiryDays = input.OfflineExpiryDays > 0 ? Math.Min(input.OfflineExpiryDays.Value, 90) : Config.LmsConfig.DefaultOfflineExpiryDays;
						}
					}
					else
					{
						// Academy videos are the system publisher's alone.
						return LmsErrors.InvalidInput;
					}
					break;

				case LmsLessonKinds.Document:
					if (!IsHttpsUrl(input.DocumentUrl))
						return LmsErrors.InvalidInput;
					lesson.DocumentUrl = input.DocumentUrl.Trim();
					lesson.DocumentName = Truncate(input.DocumentName?.Trim(), 250);
					break;

				case LmsLessonKinds.ProtocolVersion:
					var protocolVersion = input.ProtocolVersionId.HasValue ? await _protocolVersions.GetByIdForDepartmentAsync(actor.DepartmentId, input.ProtocolVersionId.Value) : null;
					if (protocolVersion == null || protocolVersion.Status != (int)ProtocolVersionStatuses.Published && protocolVersion.Status != (int)ProtocolVersionStatuses.Superseded)
						return LmsErrors.InvalidInput;
					lesson.ProtocolVersionId = protocolVersion.ProtocolVersionId;
					break;

				case LmsLessonKinds.Assessment:
					var bank = input.LmsQuestionBankId.HasValue ? await _banks.GetVisibleByIdAsync(actor.DepartmentId, input.LmsQuestionBankId.Value) : null;
					if (bank == null || bank.IsDeleted || bank.DepartmentId != actor.DepartmentId || !LmsQuestionBankService.MayUse(actor, bank))
						return LmsErrors.InvalidInput;
					if (input.PassMark < 0 || input.PassMark > 100 || input.AttemptLimit < 0 || input.AttemptLimit > 99 || input.RetakeCooldownHours < 0 ||
						input.TimeLimitMinutes < 0 || input.TimeLimitMinutes > 600 || input.DrawCount < 0 || !Enum.IsDefined(typeof(LmsShowAnswersPolicies), input.ShowAnswersPolicy))
						return LmsErrors.InvalidInput;
					lesson.LmsQuestionBankId = bank.LmsQuestionBankId;
					lesson.DrawCount = input.DrawCount;
					lesson.DrawTags = NormalizeTags(input.DrawTags);
					lesson.PassMark = input.PassMark;
					lesson.AttemptLimit = input.AttemptLimit;
					lesson.RetakeCooldownHours = input.RetakeCooldownHours;
					lesson.TimeLimitMinutes = input.TimeLimitMinutes;
					lesson.ShowAnswersPolicy = input.ShowAnswersPolicy;
					lesson.IsPreTest = input.IsPreTest;
					lesson.RandomizeAnswers = input.RandomizeAnswers;
					lesson.RequireAttestation = input.RequireAttestation;
					break;

				case LmsLessonKinds.Survey:
					var questions = (input.SurveyQuestions ?? new List<LmsSurveyQuestion>())
						.Where(q => q != null && !string.IsNullOrWhiteSpace(q.Text) && Enum.IsDefined(typeof(LmsSurveyQuestionKinds), q.Kind)).ToList();
					if (questions.Count == 0)
						return LmsErrors.InvalidInput;
					for (var i = 0; i < questions.Count; i++)
					{
						questions[i].Key = "s" + (i + 1).ToString(CultureInfo.InvariantCulture);
						questions[i].Text = Truncate(questions[i].Text.Trim(), 500);
						questions[i].Options = questions[i].Kind == (int)LmsSurveyQuestionKinds.Choice
							? (questions[i].Options ?? new List<string>()).Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => Truncate(o.Trim(), 200)).Take(10).ToList()
							: new List<string>();
						if (questions[i].Kind == (int)LmsSurveyQuestionKinds.Choice && questions[i].Options.Count < 2)
							return LmsErrors.InvalidInput;
					}
					lesson.SurveyJson = JsonConvert.SerializeObject(questions);
					break;
			}

			return LmsErrors.None;
		}

		public async Task<LmsResult> DeleteLessonAsync(LmsActor actor, int lmsLessonId, CancellationToken cancellationToken = default)
		{
			var lesson = await _lessons.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsLessonId);
			if (lesson == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			var (course, version, error) = await EditableDraftAsync(actor, lesson.LmsCourseVersionId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);

			await _lessons.DeleteAsync(lesson, cancellationToken);
			return await RehashAsync(course, version, cancellationToken);
		}

		public async Task<LmsResult> MoveAsync(LmsActor actor, int lmsCourseVersionId, bool isModule, int id, bool up, CancellationToken cancellationToken = default)
		{
			var (course, version, error) = await EditableDraftAsync(actor, lmsCourseVersionId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);

			if (isModule)
			{
				var modules = (await _modules.GetForVersionAsync(actor.DepartmentId, lmsCourseVersionId) ?? new List<LmsCourseModule>()).OrderBy(m => m.SortOrder).ThenBy(m => m.LmsCourseModuleId).ToList();
				var index = modules.FindIndex(m => m.LmsCourseModuleId == id);
				var other = index < 0 ? -1 : (up ? index - 1 : index + 1);
				if (index < 0 || other < 0 || other >= modules.Count)
					return new LmsResult { Course = course, Version = version };
				Renumber(modules, index, other, m => m.SortOrder, (m, v) => m.SortOrder = v);
				foreach (var module in modules)
					await _modules.UpdateAsync(module, cancellationToken, true);
			}
			else
			{
				var all = await _lessons.GetForVersionAsync(actor.DepartmentId, lmsCourseVersionId) ?? new List<LmsLesson>();
				var target = all.FirstOrDefault(l => l.LmsLessonId == id);
				if (target == null)
					return LmsResult.Fail(LmsErrors.NotFound);
				var siblings = all.Where(l => l.LmsCourseModuleId == target.LmsCourseModuleId).OrderBy(l => l.SortOrder).ThenBy(l => l.LmsLessonId).ToList();
				var index = siblings.FindIndex(l => l.LmsLessonId == id);
				var other = up ? index - 1 : index + 1;
				if (other < 0 || other >= siblings.Count)
					return new LmsResult { Course = course, Version = version };
				Renumber(siblings, index, other, l => l.SortOrder, (l, v) => l.SortOrder = v);
				foreach (var lesson in siblings)
					await _lessons.UpdateAsync(lesson, cancellationToken, true);
			}

			return await RehashAsync(course, version, cancellationToken);
		}

		private static void Renumber<T>(List<T> items, int index, int other, Func<T, int> get, Action<T, int> set)
		{
			(items[index], items[other]) = (items[other], items[index]);
			for (var i = 0; i < items.Count; i++)
				set(items[i], i + 1);
		}

		#endregion Course and draft authoring

		#region Review and publish (the shared lifecycle)

		public async Task<LmsResult> SubmitAsync(LmsActor actor, int lmsCourseVersionId, string changeSummary, CancellationToken cancellationToken = default)
		{
			var (course, version, error) = await EditableDraftAsync(actor, lmsCourseVersionId);
			if (error == LmsErrors.NotDraft)
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.NotDraft };
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);
			if (!ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.InReview))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.NotDraft, Course = course, Version = version };

			var outline = await GetOutlineAsync(actor.DepartmentId, lmsCourseVersionId);
			if (outline == null || !outline.Lessons.Any())
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.EmptyContent, Course = course, Version = version };

			var existing = await GetApprovalsAsync(actor.DepartmentId, lmsCourseVersionId);
			var round = existing.Count == 0 ? 1 : existing.Max(a => a.Round) + 1;
			var now = DateTime.UtcNow;
			var expected = version.RowVersion;
			version.Status = (int)ProtocolVersionStatuses.InReview;
			version.SubmittedOn = now;
			version.SubmittedByUserId = actor.UserId;
			version.ContentHash = Hash(outline);
			if (!string.IsNullOrWhiteSpace(changeSummary))
				version.ChangeSummary = Truncate(changeSummary.Trim(), ProtocolVersion.ChangeSummaryMaxLength);

			var owns = _unitOfWork?.Transaction == null;
			_unitOfWork?.CreateOrGetConnection();
			try
			{
				if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				{
					if (owns) _unitOfWork?.DiscardChanges();
					return new LmsResult { Error = LmsErrors.StaleCopy, Course = course };
				}

				// One step for any holder of Manage Courses (P1's default when a category names no steps).
				await _approvals.InsertAsync(new LmsCourseVersionApproval
				{
					DepartmentId = actor.DepartmentId,
					LmsCourseVersionId = lmsCourseVersionId,
					Round = round,
					StepOrder = 1,
					Decision = (int)ProtocolApprovalDecisions.Pending,
					CreatedOn = now
				}, cancellationToken, true);

				if (owns) _unitOfWork?.CommitChanges();
			}
			catch
			{
				if (owns) _unitOfWork?.DiscardChanges();
				throw;
			}

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseVersionSubmitted, null, Snapshot(course, version));
			return new LmsResult { Course = course, Version = version };
		}

		public async Task<LmsResult> WithdrawAsync(LmsActor actor, int lmsCourseVersionId, CancellationToken cancellationToken = default)
		{
			var (course, version) = await DepartmentVersionAsync(actor, lmsCourseVersionId);
			if (version == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			var submitter = string.Equals(version.SubmittedByUserId, actor.UserId, StringComparison.OrdinalIgnoreCase);
			if (!actor.MayManage && !(submitter && (await GetRightsAsync(actor, course)).Edit))
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (version.Status == (int)ProtocolVersionStatuses.Draft || !ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.Draft))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.NotInReview, Course = course, Version = version };

			var round = (await GetApprovalsAsync(actor.DepartmentId, lmsCourseVersionId)).Select(a => a.Round).DefaultIfEmpty(0).Max();
			var expected = version.RowVersion;
			version.Status = (int)ProtocolVersionStatuses.Draft;
			version.ApprovedOn = null;
			version.ApprovedByUserId = null;
			if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				return new LmsResult { Error = LmsErrors.StaleCopy, Course = course };

			await _approvals.WithdrawPendingAsync(actor.DepartmentId, lmsCourseVersionId, round, DateTime.UtcNow, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseVersionWithdrawn, null, Snapshot(course, version));
			return new LmsResult { Course = course, Version = version };
		}

		public async Task<LmsResult> ApproveAsync(LmsActor actor, int lmsCourseVersionId, string comment, SignatureProof proof, CancellationToken cancellationToken = default)
		{
			var (course, version, round, step, error) = await DecidableAsync(actor, lmsCourseVersionId);
			if (error != ProtocolLifecycleErrors.None || step == null)
				return new LmsResult { LifecycleError = error == ProtocolLifecycleErrors.None ? ProtocolLifecycleErrors.NotInReview : error, Course = course, Version = version };

			// The signature binds to the stored hash, which must still be the version's (a changed lesson fails here, not later).
			var outline = await GetOutlineAsync(actor.DepartmentId, lmsCourseVersionId);
			if (!string.Equals(Hash(outline), version.ContentHash, StringComparison.Ordinal))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.ArtifactChanged, Course = course, Version = version };

			if (proof == null || !string.Equals(proof.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.SignatureRequired, Course = course, Version = version };

			var signed = await _signatures.SignAsync(new SignatureRequest
			{
				DepartmentId = actor.DepartmentId,
				Use = SignatureUse.CourseApproval,
				SubjectId = lmsCourseVersionId.ToString(CultureInfo.InvariantCulture),
				RevisionId = "round-" + step.Round.ToString(CultureInfo.InvariantCulture) + "/step-" + step.StepOrder.ToString(CultureInfo.InvariantCulture),
				ArtifactSha256 = version.ContentHash,
				StatementText = LmsCourseStatements.Approval(course, version)
			}, proof, cancellationToken);

			if (!signed.Succeeded)
				return new LmsResult { LifecycleError = MapSignature(signed.Outcome), Course = course, Version = version };

			step.Decision = (int)ProtocolApprovalDecisions.Approved;
			step.DecidedByUserId = actor.UserId;
			step.DecidedOn = DateTime.UtcNow;
			step.Comment = Truncate(comment?.Trim(), 2000);
			step.SignatureRecordId = signed.Record.SignatureRecordId;
			if (!await _approvals.TryDecideAsync(step, cancellationToken))
			{
				await _signatures.VoidAsync(actor.DepartmentId, signed.Record.SignatureRecordId, actor.UserId, "The approval step was decided by someone else first.", cancellationToken);
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.AlreadyDecided, Course = course, Version = version };
			}

			if (round.All(a => a.LmsCourseVersionApprovalId == step.LmsCourseVersionApprovalId || a.Decision == (int)ProtocolApprovalDecisions.Approved) &&
				ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.Approved))
			{
				for (var attempt = 0; attempt < 3 && version != null && version.Status == (int)ProtocolVersionStatuses.InReview; attempt++)
				{
					var expected = version.RowVersion;
					version.Status = (int)ProtocolVersionStatuses.Approved;
					version.ApprovedOn = step.DecidedOn;
					version.ApprovedByUserId = actor.UserId;
					if (await _versions.TryUpdateAsync(version, expected, cancellationToken))
						break;
					version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, lmsCourseVersionId);
				}
			}

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseVersionApproved, null, Snapshot(course, version));
			return new LmsResult { Course = course, Version = version, Signature = signed.Record };
		}

		public async Task<LmsResult> RejectAsync(LmsActor actor, int lmsCourseVersionId, string comment, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(comment))
				return LmsResult.Fail(ProtocolLifecycleErrors.InvalidInput);
			var (course, version, _, step, error) = await DecidableAsync(actor, lmsCourseVersionId);
			if (error != ProtocolLifecycleErrors.None || step == null)
				return new LmsResult { LifecycleError = error == ProtocolLifecycleErrors.None ? ProtocolLifecycleErrors.NotInReview : error, Course = course, Version = version };

			var now = DateTime.UtcNow;
			step.Decision = (int)ProtocolApprovalDecisions.Rejected;
			step.DecidedByUserId = actor.UserId;
			step.DecidedOn = now;
			step.Comment = Truncate(comment.Trim(), 2000);
			if (!await _approvals.TryDecideAsync(step, cancellationToken))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.AlreadyDecided, Course = course, Version = version };

			await _approvals.WithdrawPendingAsync(actor.DepartmentId, lmsCourseVersionId, step.Round, now, cancellationToken);
			var expected = version.RowVersion;
			version.Status = (int)ProtocolVersionStatuses.Draft;
			await _versions.TryUpdateAsync(version, expected, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseVersionRejected, null, Snapshot(course, version));
			return new LmsResult { Course = course, Version = version };
		}

		private async Task<(LmsCourse, LmsCourseVersion, List<LmsCourseVersionApproval>, LmsCourseVersionApproval, ProtocolLifecycleErrors)> DecidableAsync(LmsActor actor, int lmsCourseVersionId)
		{
			if (actor == null || !actor.MayManage)
				return (null, null, null, null, ProtocolLifecycleErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return (null, null, null, null, ProtocolLifecycleErrors.NotAuthorized);

			var (course, version) = await DepartmentVersionAsync(actor, lmsCourseVersionId);
			if (version == null)
				return (null, null, null, null, ProtocolLifecycleErrors.NotFound);
			if (version.Status != (int)ProtocolVersionStatuses.InReview)
				return (course, version, null, null, ProtocolLifecycleErrors.NotInReview);

			var round = CurrentRound(await GetApprovalsAsync(actor.DepartmentId, lmsCourseVersionId));
			var step = round.FirstOrDefault(a => a.Decision == (int)ProtocolApprovalDecisions.Pending);
			if (step == null)
				return (course, version, round, null, ProtocolLifecycleErrors.NotInReview);
			if (round.Any(a => a.Decision == (int)ProtocolApprovalDecisions.Approved && string.Equals(a.DecidedByUserId, actor.UserId, StringComparison.OrdinalIgnoreCase)))
				return (course, version, round, step, ProtocolLifecycleErrors.AlreadyDecided);
			return (course, version, round, step, ProtocolLifecycleErrors.None);
		}

		public async Task<LmsResult> PublishAsync(LmsActor actor, int lmsCourseVersionId, bool major, string changeSummary, LmsMajorChangePolicies policy, int? reassignDueDays,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);

			var (course, version) = await DepartmentVersionAsync(actor, lmsCourseVersionId);
			if (version == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (version.Status != (int)ProtocolVersionStatuses.Approved || !ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.Published))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.NotApproved, Course = course, Version = version };

			var summary = string.IsNullOrWhiteSpace(changeSummary) ? version.ChangeSummary : changeSummary.Trim();
			if (string.IsNullOrWhiteSpace(summary))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.ChangeSummaryRequired, Course = course, Version = version };
			if (!Enum.IsDefined(typeof(LmsMajorChangePolicies), policy) || (policy == LmsMajorChangePolicies.Reassign && (!reassignDueDays.HasValue || reassignDueDays < 1 || reassignDueDays > 730)))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			if (!await ApprovalsStillMatchAsync(actor.DepartmentId, version))
				return new LmsResult { LifecycleError = ProtocolLifecycleErrors.ArtifactChanged, Course = course, Version = version };

			var versions = await GetVersionsAsync(actor.DepartmentId, course.LmsCourseId);
			var current = versions.FirstOrDefault(v => v.LmsCourseVersionId == course.CurrentVersionId && v.Status == (int)ProtocolVersionStatuses.Published);
			var last = versions.Where(v => v.Major > 0).OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).FirstOrDefault();
			var (nextMajor, nextMinor) = ProtocolLifecycle.NextNumber(last?.Major ?? 0, last?.Minor ?? 0, major);
			var isMajorChange = last != null && nextMinor == 0;
			var now = DateTime.UtcNow;

			var owns = _unitOfWork?.Transaction == null;
			_unitOfWork?.CreateOrGetConnection();
			try
			{
				var expected = version.RowVersion;
				version.Status = (int)ProtocolVersionStatuses.Published;
				version.Major = nextMajor;
				version.Minor = nextMinor;
				version.ChangeSummary = Truncate(summary, ProtocolVersion.ChangeSummaryMaxLength);
				version.EffectiveOn = now;
				version.PublishedOn = now;
				version.PublishedByUserId = actor.UserId;
				version.MajorChangePolicy = isMajorChange ? (int)policy : (int)LmsMajorChangePolicies.Nothing;
				version.ReassignDueDays = isMajorChange && policy == LmsMajorChangePolicies.Reassign ? reassignDueDays : null;
				if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				{
					if (owns) _unitOfWork?.DiscardChanges();
					return new LmsResult { Error = LmsErrors.StaleCopy, Course = course };
				}

				if (current != null && ProtocolLifecycle.CanMove(current.Status, (int)ProtocolVersionStatuses.Superseded))
				{
					var expectedCurrent = current.RowVersion;
					current.Status = (int)ProtocolVersionStatuses.Superseded;
					current.SupersededOn = now;
					await _versions.TryUpdateAsync(current, expectedCurrent, cancellationToken);
				}

				var expectedCourse = course.RowVersion;
				course.CurrentVersionId = version.LmsCourseVersionId;
				course.Status = (int)ProtocolStatuses.Published;
				course.Title = version.Title ?? course.Title;
				course.UpdatedOn = now;
				course.UpdatedByUserId = actor.UserId;
				if (!await _courses.TryUpdateAsync(course, expectedCourse, cancellationToken))
				{
					if (owns) _unitOfWork?.DiscardChanges();
					return new LmsResult { Error = LmsErrors.StaleCopy, Course = course };
				}

				if (owns) _unitOfWork?.CommitChanges();
			}
			catch
			{
				if (owns) _unitOfWork?.DiscardChanges();
				throw;
			}

			var moved = await ApplyNewVersionToLearnersAsync(actor.DepartmentId, actor.UserId, course, version, isMajorChange ? policy : LmsMajorChangePolicies.Nothing,
				reassignDueDays, now, cancellationToken);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseVersionPublished, null, Snapshot(course, version));
			await QueueKnowledgeReindexAsync(actor.DepartmentId, course.LmsCourseId, cancellationToken);
			return new LmsResult { Course = course, Version = version, Count = moved };
		}

		/// <summary>
		/// R-L12 on publish: unstarted enrollments move to the new version; in-progress learners keep theirs; on a major version the
		/// completed learners are left alone, notified, or re-assigned with a new due date (active members only). Returns the number of
		/// learners re-pinned, notified or re-assigned.
		/// </summary>
		internal async Task<int> ApplyNewVersionToLearnersAsync(int departmentId, string actingUserId, LmsCourse course, LmsCourseVersion version, LmsMajorChangePolicies policy,
			int? reassignDueDays, DateTime utcNow, CancellationToken cancellationToken)
		{
			var count = 0;
			var enrollments = await _enrollments.GetForCourseAsync(departmentId, course.LmsCourseId) ?? new List<LmsEnrollment>();
			foreach (var enrollment in enrollments.Where(e => e.IsOpen && !e.StartedOn.HasValue && e.LmsCourseVersionId != version.LmsCourseVersionId))
			{
				var expected = enrollment.RowVersion;
				enrollment.LmsCourseVersionId = version.LmsCourseVersionId;
				if (await _enrollments.TryUpdateAsync(enrollment, expected, cancellationToken))
					count++;
			}

			if (policy == LmsMajorChangePolicies.Nothing)
				return count;

			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var open = new HashSet<string>(enrollments.Where(e => e.IsOpen).Select(e => e.UserId), StringComparer.OrdinalIgnoreCase);
			var completedUsers = enrollments
				.Where(e => e.Status == (int)LmsEnrollmentStatuses.Completed && e.LmsCourseVersionId != version.LmsCourseVersionId)
				.Select(e => e.UserId).Where(u => !string.IsNullOrWhiteSpace(u) && active.Contains(u) && !open.Contains(u))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			if (policy == LmsMajorChangePolicies.Reassign)
			{
				var due = utcNow.AddDays(reassignDueDays ?? 30);
				foreach (var userId in completedUsers)
				{
					var (_, created) = await _enrollments.InsertOpenAsync(new LmsEnrollment
					{
						DepartmentId = departmentId,
						LmsCourseId = course.LmsCourseId,
						LmsCourseVersionId = version.LmsCourseVersionId,
						UserId = userId,
						Source = (int)LmsEnrollmentSources.Reassigned,
						AssignedByUserId = actingUserId,
						AssignedOn = utcNow,
						DueOn = due,
						Status = (int)LmsEnrollmentStatuses.NotStarted
					}, cancellationToken);
					if (created)
						count++;
				}
			}

			if (policy == LmsMajorChangePolicies.Notify || policy == LmsMajorChangePolicies.Reassign)
				count += await NotifyAsync(departmentId, completedUsers, policy == LmsMajorChangePolicies.Reassign ? "Notify.CourseReassigned" : "Notify.CourseNewVersion", course.Title, version.Label);

			return count;
		}

		private async Task<int> NotifyAsync(int departmentId, IEnumerable<string> userIds, string key, params object[] args)
		{
			var sent = 0;
			if (!ConfigHelper.CanTransmit(departmentId))
				return 0;
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				if (department == null)
					return 0;
				var number = await _settingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
				foreach (var userId in userIds.Distinct(StringComparer.OrdinalIgnoreCase))
				{
					var language = (await _profiles.GetProfileByUserIdAsync(userId))?.Language;
					var message = string.Format(LmsText.Culture(language), LmsText.Get(key, language), args);
					await _communicationService.Value.SendNotificationAsync(userId, departmentId, message, number, department, LmsText.Get(key + ".Title", language));
					sent++;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "LMS course notifications failed.");
			}
			return sent;
		}

		private async Task<bool> ApprovalsStillMatchAsync(int departmentId, LmsCourseVersion version)
		{
			var outline = await GetOutlineAsync(departmentId, version.LmsCourseVersionId);
			var hash = Hash(outline);
			if (!string.Equals(hash, version.ContentHash, StringComparison.Ordinal))
				return false;

			var round = CurrentRound(await GetApprovalsAsync(departmentId, version.LmsCourseVersionId));
			if (round.Count == 0 || round.Any(a => a.Decision != (int)ProtocolApprovalDecisions.Approved || !a.SignatureRecordId.HasValue))
				return false;

			foreach (var step in round)
			{
				var signature = await _signatures.GetByIdAsync(departmentId, step.SignatureRecordId.Value);
				var verification = _signatures.Verify(signature, hash);
				if (signature == null || verification == null || !verification.IsValid || signature.Level < (int)SignatureLevels.StepUp ||
					signature.SubjectKind != (int)SignatureSubjectKinds.CourseVersion)
					return false;
			}

			return true;
		}

		public async Task<LmsResult> RetireAsync(LmsActor actor, int lmsCourseId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, lmsCourseId);
			if (course == null || course.IsSystem || course.IsDeleted)
				return LmsResult.Fail(LmsErrors.NotFound);

			var now = DateTime.UtcNow;
			var current = course.CurrentVersionId.HasValue ? await _versions.GetVisibleByIdAsync(actor.DepartmentId, course.CurrentVersionId.Value) : null;
			if (current != null && ProtocolLifecycle.CanMove(current.Status, (int)ProtocolVersionStatuses.Retired))
			{
				var expectedVersion = current.RowVersion;
				current.Status = (int)ProtocolVersionStatuses.Retired;
				current.RetiredOn = now;
				await _versions.TryUpdateAsync(current, expectedVersion, cancellationToken);
			}

			var expected = course.RowVersion;
			course.Status = (int)ProtocolStatuses.Retired;
			course.IsSelfEnrollable = false;
			course.UpdatedOn = now;
			course.UpdatedByUserId = actor.UserId;
			if (!await _courses.TryUpdateAsync(course, expected, cancellationToken))
				return LmsResult.Fail(LmsErrors.StaleCopy);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseRetired, null, Snapshot(course, current));
			await QueueKnowledgeReindexAsync(actor.DepartmentId, course.LmsCourseId, cancellationToken);
			return new LmsResult { Course = course, Version = current };
		}

		#endregion Review and publish

		#region Owners

		public async Task<LmsResult> SaveOwnerAsync(LmsActor actor, int lmsCourseId, string userId, bool canEdit, bool canAssign, bool canReport, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, lmsCourseId);
			if (course == null || course.IsSystem || course.IsDeleted)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (string.IsNullOrWhiteSpace(userId) || !(await _departmentsService.GetActiveMemberUserIdsAsync(actor.DepartmentId) ?? new HashSet<string>()).Contains(userId))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var owners = await GetOwnersAsync(actor.DepartmentId, lmsCourseId);
			var row = owners.FirstOrDefault(o => string.Equals(o.UserId, userId, StringComparison.OrdinalIgnoreCase)) ?? new LmsCourseOwner
			{
				DepartmentId = actor.DepartmentId, LmsCourseId = lmsCourseId, UserId = userId, AddedOn = DateTime.UtcNow, AddedByUserId = actor.UserId
			};
			row.CanEdit = canEdit;
			row.CanAssign = canAssign;
			row.CanReport = canReport;
			await _owners.SaveOrUpdateAsync(row, cancellationToken, true);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseChanged, null, JsonConvert.SerializeObject(new { owner = userId, lmsCourseId, canEdit, canAssign, canReport }));
			return new LmsResult { Course = course, Value = row };
		}

		public async Task<LmsResult> RemoveOwnerAsync(LmsActor actor, int lmsCourseId, int lmsCourseOwnerId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			var row = (await GetOwnersAsync(actor.DepartmentId, lmsCourseId)).FirstOrDefault(o => o.LmsCourseOwnerId == lmsCourseOwnerId);
			if (row == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			await _owners.DeleteAsync(row, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseChanged, JsonConvert.SerializeObject(new { owner = row.UserId, lmsCourseId }), null);
			return new LmsResult();
		}

		#endregion Owners

		#region Helpers

		/// <summary>A department course's draft the actor may edit now (rights, add-on, Draft status).</summary>
		private async Task<(LmsCourse, LmsCourseVersion, LmsErrors)> EditableDraftAsync(LmsActor actor, int lmsCourseVersionId)
		{
			if (actor == null || !actor.IsCurrentMember)
				return (null, null, LmsErrors.NotAuthorized);
			var (course, version) = await DepartmentVersionAsync(actor, lmsCourseVersionId);
			if (version == null)
				return (null, null, LmsErrors.NotFound);
			if (!(await GetRightsAsync(actor, course)).Edit)
				return (course, version, LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return (course, version, LmsErrors.AddonRequired);
			if (!ProtocolLifecycle.IsContentEditable(version.Status))
				return (course, version, LmsErrors.NotDraft);
			return (course, version, LmsErrors.None);
		}

		/// <summary>A version of one of the department's own (not system, not deleted) courses.</summary>
		private async Task<(LmsCourse, LmsCourseVersion)> DepartmentVersionAsync(LmsActor actor, int lmsCourseVersionId)
		{
			if (actor == null || actor.DepartmentId <= 0)
				return (null, null);
			var version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, lmsCourseVersionId);
			if (version == null || version.DepartmentId != actor.DepartmentId)
				return (null, null);
			var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
			if (course == null || course.IsSystem || course.IsDeleted || course.DepartmentId != actor.DepartmentId)
				return (null, null);
			return (course, version);
		}

		private async Task<LmsResult> SaveDraftWithHashAsync(LmsCourse course, LmsCourseVersion version, long expected, CancellationToken cancellationToken)
		{
			var modules = await _modules.GetForVersionAsync(version.DepartmentId, version.LmsCourseVersionId) ?? new List<LmsCourseModule>();
			var lessons = await _lessons.GetForVersionAsync(version.DepartmentId, version.LmsCourseVersionId) ?? new List<LmsLesson>();
			version.ContentHash = LmsCourseContent.ComputeHash(version, modules, lessons);
			if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				return new LmsResult { Error = LmsErrors.StaleCopy, Course = course };
			return new LmsResult { Course = course, Version = version };
		}

		/// <summary>After a module or lesson change: the draft's hash follows its content (and its RowVersion moves).</summary>
		private async Task<LmsResult> RehashAsync(LmsCourse course, LmsCourseVersion version, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < 3; attempt++)
			{
				var result = await SaveDraftWithHashAsync(course, version, version.RowVersion, cancellationToken);
				if (result.Succeeded)
					return result;
				version = await _versions.GetVisibleByIdAsync(version.DepartmentId, version.LmsCourseVersionId);
				if (version == null)
					break;
			}
			return new LmsResult { Error = LmsErrors.StaleCopy, Course = course };
		}

		private static string Hash(LmsCourseOutline outline) =>
			outline == null ? null : LmsCourseContent.ComputeHash(outline.Version, outline.Modules.Select(m => m.Module), outline.Lessons);

		internal static List<LmsCourseVersionApproval> CurrentRound(IEnumerable<LmsCourseVersionApproval> steps)
		{
			var list = (steps ?? Enumerable.Empty<LmsCourseVersionApproval>()).ToList();
			if (list.Count == 0)
				return list;
			var round = list.Max(a => a.Round);
			return list.Where(a => a.Round == round).OrderBy(a => a.StepOrder).ToList();
		}

		private static ProtocolLifecycleErrors MapSignature(SignatureOutcomes outcome)
		{
			switch (outcome)
			{
				case SignatureOutcomes.PasskeyRequired: return ProtocolLifecycleErrors.PasskeyRequired;
				case SignatureOutcomes.ArtifactMismatch: return ProtocolLifecycleErrors.ArtifactChanged;
				case SignatureOutcomes.InvalidRequest: return ProtocolLifecycleErrors.InvalidInput;
				default: return ProtocolLifecycleErrors.SignatureRequired;
			}
		}

		private static bool IsHttpsUrl(string url) =>
			!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

		internal static string NormalizeTags(string tags)
		{
			var list = LmsAssessmentEngine.SplitTags(tags).Select(t => t.Length > 40 ? t.Substring(0, 40) : t).Take(20).ToList();
			return list.Count == 0 ? null : string.Join(",", list);
		}

		internal static string Truncate(string value, int max) =>
			value == null ? null : value.Length <= max ? value : value.Substring(0, max);

		private static string Snapshot(LmsCourse course, LmsCourseVersion version) => JsonConvert.SerializeObject(new
		{
			course?.LmsCourseId,
			course?.Title,
			version?.LmsCourseVersionId,
			version?.Label,
			version?.Status,
			version?.ContentHash
		});

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
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

		#endregion Helpers
	}

	/// <summary>The LMS's server-side text (notifications and reminders) in the recipient's language (Courses area, 10 locales).</summary>
	public static class LmsText
	{
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.Courses.Courses", typeof(Resgrid.Localization.SupportedLocales).Assembly);

		public static CultureInfo Culture(string language)
		{
			try
			{
				return string.IsNullOrWhiteSpace(language) ? new CultureInfo("en") : new CultureInfo(language.Trim());
			}
			catch (CultureNotFoundException)
			{
				return new CultureInfo("en");
			}
		}

		public static string Get(string key, string language) => Lookup(Strings, key, language);

		/// <summary>
		/// The resource in the requested language, else in English, else the key itself. These areas ship culture resources
		/// only (no neutral resource), so each lookup is tried on its own: a culture missing the key must not stop the
		/// English fallback.
		/// </summary>
		public static string Lookup(ResourceManager resources, string key, string language)
		{
			return TryGet(resources, key, Culture(language)) ?? TryGet(resources, key, new CultureInfo("en")) ?? key;
		}

		public static string TryGet(ResourceManager resources, string key, CultureInfo culture)
		{
			try
			{
				return resources.GetString(key, culture);
			}
			catch (MissingManifestResourceException)
			{
				return null;
			}
		}
	}
}
