using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Training T5 shared course sets (round 10 A3): the LMS side of a subscriber's managed course copy. The copy is the subscriber's own
	/// department course (so its runtime, enrolments, transcript and certificates are the subscriber's), built from the publisher's published
	/// version. Hosted video and SCORM/cmi5 packages travel as the subscriber's own rows over the publisher's stored files, an LTI lesson uses
	/// the subscriber's own registration with the same tool (<see cref="ILmsSetFiles"/>; prepared inactive, the managers are told to register it),
	/// and a live session (not built) refuses the course (<see cref="SetCopyProblem"/>); assessment lessons get a copy of their question bank in
	/// the subscriber's department; a protocol-version lesson becomes a rich-text lesson with that version's text. Certifications, categories
	/// and certificate templates are department ids and are not carried. A copy is not edited in the course editor
	/// (<see cref="ManagedBySetAsync"/>); its versions come from the set.
	/// </summary>
	public partial class LmsCourseService
	{
		public const string SetProblemNotPublished = "not_published";
		public const string SetProblemLessonKinds = "lesson_kinds";

		/// <summary>The tag a subscriber's copy of a publisher question bank carries ("set:{publisher}:{bank}"), so later updates refresh it.</summary>
		public static string SetBankTag(int publisherDepartmentId, int sourceBankId) =>
			"set:" + publisherDepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + sourceBankId.ToString(CultureInfo.InvariantCulture);

		private async Task<bool> ManagedBySetAsync(int departmentId, int lmsCourseId) =>
			_setLocks != null && await _setLocks.IsManagedCopyAsync(departmentId, ContentSetItemKinds.LmsCourse, lmsCourseId);

		/// <summary>Why a published course version cannot be copied into another department, or null.</summary>
		public static string SetCopyProblem(LmsCourse course, LmsCourseVersion version, IEnumerable<LmsLesson> lessons)
		{
			if (course == null || version == null || course.IsSystem || course.IsDeleted || version.Status != (int)ProtocolVersionStatuses.Published ||
				version.LmsCourseId != course.LmsCourseId || version.DepartmentId != course.DepartmentId)
				return SetProblemNotPublished;
			// Live sessions are reserved and not built; every other kind travels (stored files through ILmsSetFiles).
			if ((lessons ?? Enumerable.Empty<LmsLesson>()).Any(l => l.Kind == (int)LmsLessonKinds.LiveSession))
				return SetProblemLessonKinds;

			return null;
		}

		public async Task<string> GetSetCopyProblemAsync(int publisherDepartmentId, int lmsCourseVersionId)
		{
			var version = await _versions.GetVisibleByIdAsync(publisherDepartmentId, lmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(publisherDepartmentId, version.LmsCourseId);
			if (course == null || course.DepartmentId != publisherDepartmentId)
				return SetProblemNotPublished;
			var lessons = await _lessons.GetForVersionAsync(publisherDepartmentId, lmsCourseVersionId) ?? new List<LmsLesson>();
			return SetCopyProblem(course, version, lessons) ?? await _setFiles.GetProblemAsync(publisherDepartmentId, lessons);
		}

		public async Task<LmsResult> CreateSetCopyAsync(LmsSetCopyRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.SubscriberDepartmentId <= 0 || request.PublisherDepartmentId <= 0 || request.SubscriberDepartmentId == request.PublisherDepartmentId ||
				string.IsNullOrWhiteSpace(request.UserId))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var (sourceCourse, sourceVersion, sourceModules, sourceLessons, problem) = await SetSourceAsync(request);
			if (problem != null)
				return new LmsResult { Error = LmsErrors.NotFound, Value = problem };

			var now = DateTime.UtcNow;
			var course = await _courses.InsertAsync(new LmsCourse
			{
				DepartmentId = request.SubscriberDepartmentId,
				PublisherKind = (int)LmsPublisherKinds.Department,
				Title = Truncate(sourceCourse.Title, LmsCourse.TitleMaxLength),
				Code = Truncate(sourceCourse.Code, 50),
				Summary = Truncate(sourceCourse.Summary, LmsCourse.SummaryMaxLength),
				Tags = sourceCourse.Tags,
				Status = (int)ProtocolStatuses.Draft,
				IsSelfEnrollable = sourceCourse.IsSelfEnrollable,
				CreatedOn = now,
				CreatedByUserId = request.UserId,
				UpdatedOn = now,
				UpdatedByUserId = request.UserId
			}, cancellationToken, true);

			return await WriteSetVersionAsync(request, course, sourceVersion, sourceModules, sourceLessons, now, cancellationToken);
		}

		public async Task<LmsResult> PublishSetVersionAsync(LmsSetCopyRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.SubscriberDepartmentId <= 0 || request.LocalCourseId <= 0 || string.IsNullOrWhiteSpace(request.UserId))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var course = await _courses.GetVisibleByIdAsync(request.SubscriberDepartmentId, request.LocalCourseId);
			if (course == null || course.DepartmentId != request.SubscriberDepartmentId || course.IsSystem || course.IsDeleted)
				return LmsResult.Fail(LmsErrors.NotFound);

			var (_, sourceVersion, sourceModules, sourceLessons, problem) = await SetSourceAsync(request);
			if (problem != null)
				return new LmsResult { Error = LmsErrors.NotFound, Value = problem };

			return await WriteSetVersionAsync(request, course, sourceVersion, sourceModules, sourceLessons, DateTime.UtcNow, cancellationToken);
		}

		private async Task<(LmsCourse, LmsCourseVersion, List<LmsCourseModule>, List<LmsLesson>, string)> SetSourceAsync(LmsSetCopyRequest request)
		{
			var version = await _versions.GetVisibleByIdAsync(request.PublisherDepartmentId, request.SourceVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(request.PublisherDepartmentId, version.LmsCourseId);
			if (course == null || course.DepartmentId != request.PublisherDepartmentId)
				return (null, null, null, null, SetProblemNotPublished);
			var modules = (await _modules.GetForVersionAsync(request.PublisherDepartmentId, version.LmsCourseVersionId) ?? new List<LmsCourseModule>()).OrderBy(m => m.SortOrder).ToList();
			var lessons = (await _lessons.GetForVersionAsync(request.PublisherDepartmentId, version.LmsCourseVersionId) ?? new List<LmsLesson>()).ToList();
			var problem = SetCopyProblem(course, version, lessons) ?? await _setFiles.GetProblemAsync(request.PublisherDepartmentId, lessons);
			return (course, version, modules, lessons, problem);
		}

		/// <summary>Writes the subscriber's version from the publisher's (structure remapped), publishes it and moves not-started learners onto it.</summary>
		private async Task<LmsResult> WriteSetVersionAsync(LmsSetCopyRequest request, LmsCourse course, LmsCourseVersion source, List<LmsCourseModule> sourceModules,
			List<LmsLesson> sourceLessons, DateTime now, CancellationToken cancellationToken)
		{
			var departmentId = request.SubscriberDepartmentId;
			var versions = await GetVersionsAsync(departmentId, course.LmsCourseId);
			var current = versions.FirstOrDefault(v => v.LmsCourseVersionId == course.CurrentVersionId && v.Status == (int)ProtocolVersionStatuses.Published);
			var last = versions.Where(v => v.Major > 0).OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).FirstOrDefault();
			var (nextMajor, nextMinor) = ProtocolLifecycle.NextNumber(last?.Major ?? 0, last?.Minor ?? 0, request.Major);

			var version = await _versions.InsertAsync(new LmsCourseVersion
			{
				DepartmentId = departmentId,
				LmsCourseId = course.LmsCourseId,
				Status = (int)ProtocolVersionStatuses.Draft,
				Title = source.Title,
				ContentJson = source.ContentJson,
				ContentHtml = source.ContentHtml,
				ChangeSummary = Truncate(request.ChangeSummary ?? source.ChangeSummary, ProtocolVersion.ChangeSummaryMaxLength),
				Locale = source.Locale,
				Hours = source.Hours,
				CeProvider = source.CeProvider,
				CeCourseNumber = source.CeCourseNumber,
				MajorChangePolicy = (int)LmsMajorChangePolicies.Nothing,
				CreatedOn = now,
				CreatedByUserId = request.UserId,
				UpdatedOn = now,
				UpdatedByUserId = request.UserId
			}, cancellationToken, true);

			var banks = new Dictionary<int, int>();
			var notes = new LmsSetCopyNotes();
			var modules = new List<LmsCourseModule>();
			var lessons = new List<LmsLesson>();
			foreach (var sourceModule in sourceModules)
			{
				var module = await _modules.InsertAsync(new LmsCourseModule
				{
					DepartmentId = departmentId,
					LmsCourseVersionId = version.LmsCourseVersionId,
					Title = sourceModule.Title,
					SortOrder = sourceModule.SortOrder
				}, cancellationToken, true);
				modules.Add(module);

				foreach (var sourceLesson in sourceLessons.Where(l => l.LmsCourseModuleId == sourceModule.LmsCourseModuleId).OrderBy(l => l.SortOrder))
				{
					var lesson = JsonConvert.DeserializeObject<LmsLesson>(JsonConvert.SerializeObject(sourceLesson));
					lesson.LmsLessonId = 0;
					lesson.DepartmentId = departmentId;
					lesson.LmsCourseVersionId = version.LmsCourseVersionId;
					lesson.LmsCourseModuleId = module.LmsCourseModuleId;
					lesson.LmsMediaAssetId = null;
					lesson.LmsContentPackageId = null;
					lesson.LmsLtiToolId = null;

					// Stored files: the subscriber's own rows over the publisher's objects; the LTI tool is the subscriber's own registration.
					if (sourceLesson.LmsMediaAssetId.HasValue)
						lesson.LmsMediaAssetId = await _setFiles.ShareVideoAsync(request.PublisherDepartmentId, departmentId, sourceLesson.LmsMediaAssetId.Value, request.UserId, cancellationToken);
					if (sourceLesson.LmsContentPackageId.HasValue)
						lesson.LmsContentPackageId = await _setFiles.SharePackageAsync(request.PublisherDepartmentId, departmentId, sourceLesson.LmsContentPackageId.Value, request.UserId, cancellationToken);
					if (sourceLesson.LmsLtiToolId.HasValue)
					{
						var tool = await _setFiles.PrepareToolAsync(request.PublisherDepartmentId, departmentId, sourceLesson.LmsLtiToolId.Value, request.UserId, cancellationToken);
						lesson.LmsLtiToolId = tool?.LmsLtiToolId;
						// Without grade passback the tool never sends a score: the pass mark would lock the learner out (as the lesson validator rules).
						if (tool != null && !tool.AllowGradePassback)
							lesson.PassMark = 0;
						if (tool != null && !tool.IsActive && !notes.ToolsToRegister.Contains(tool.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase))
							notes.ToolsToRegister.Add(tool.Name ?? string.Empty);
					}

					if (sourceLesson.LmsQuestionBankId.HasValue)
					{
						if (!banks.TryGetValue(sourceLesson.LmsQuestionBankId.Value, out var bankId))
							banks[sourceLesson.LmsQuestionBankId.Value] = bankId = await CopyBankAsync(request, sourceLesson.LmsQuestionBankId.Value, now, cancellationToken);
						lesson.LmsQuestionBankId = bankId > 0 ? bankId : (int?)null;
					}

					if (sourceLesson.Kind == (int)LmsLessonKinds.ProtocolVersion)
					{
						// The publisher's protocol version cannot be cited from another department: its text travels as a rich-text lesson.
						var cited = sourceLesson.ProtocolVersionId.HasValue ? await _protocolVersions.GetByIdAsync(sourceLesson.ProtocolVersionId.Value) : null;
						lesson.Kind = (int)LmsLessonKinds.Blocks;
						lesson.ProtocolVersionId = null;
						lesson.ContentJson = null;
						lesson.ContentHtml = cited != null && cited.DepartmentId == request.PublisherDepartmentId ? cited.ContentHtml : sourceLesson.ContentHtml;
					}

					lessons.Add(await _lessons.InsertAsync(lesson, cancellationToken, true));
				}
			}

			var expected = version.RowVersion;
			version.ContentHash = LmsCourseContent.ComputeHash(version, modules, lessons);
			version.Status = (int)ProtocolVersionStatuses.Published;
			version.Major = nextMajor;
			version.Minor = nextMinor;
			version.EffectiveOn = now;
			version.ApprovedOn = now;
			version.ApprovedByUserId = request.UserId;
			version.PublishedOn = now;
			version.PublishedByUserId = request.UserId;
			await _versions.TryUpdateAsync(version, expected, cancellationToken);

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
			course.UpdatedByUserId = request.UserId;
			await _courses.TryUpdateAsync(course, expectedCourse, cancellationToken);

			var moved = current == null ? 0 : await ApplyNewVersionToLearnersAsync(departmentId, request.UserId, course, version, LmsMajorChangePolicies.Nothing, null, now, cancellationToken);
			Audit(departmentId, request.UserId, AuditLogTypes.LmsCourseVersionPublished, null, Snapshot(course, version));
			ContentWorkflowContext.Publish(_eventAggregator, LmsWorkflowEvents.VersionPublished(course, version, request.UserId, now));
			await QueueKnowledgeReindexAsync(departmentId, course.LmsCourseId, cancellationToken);
			await PublishedHooksAsync(new ContentVersionPublished
			{
				DepartmentId = departmentId, Kind = ContentVersionKinds.CourseVersion, ItemId = course.LmsCourseId, VersionId = version.LmsCourseVersionId,
				VersionLabel = version.Label, PublishedOnUtc = now
			}, cancellationToken);
			return new LmsResult { Course = course, Version = version, Count = moved, Value = notes };
		}

		/// <summary>
		/// The subscriber's copy of a publisher question bank (refreshed on each update: earlier copied questions are retired, the publisher's
		/// active ones copied again), so a learner's attempt draws from the publisher's current questions. Returns the subscriber's bank id.
		/// </summary>
		private async Task<int> CopyBankAsync(LmsSetCopyRequest request, int sourceBankId, DateTime now, CancellationToken cancellationToken)
		{
			if (_questions == null)
				return 0;
			var source = await _banks.GetVisibleByIdAsync(request.PublisherDepartmentId, sourceBankId);
			if (source == null || source.DepartmentId != request.PublisherDepartmentId || source.IsDeleted)
				return 0;

			var tag = SetBankTag(request.PublisherDepartmentId, sourceBankId);
			var bank = (await _banks.GetForDepartmentAsync(request.SubscriberDepartmentId) ?? new List<LmsQuestionBank>())
				.FirstOrDefault(b => b.DepartmentId == request.SubscriberDepartmentId && !b.IsDeleted && string.Equals(b.Tags, tag, StringComparison.Ordinal));
			if (bank == null)
			{
				bank = await _banks.InsertAsync(new LmsQuestionBank
				{
					DepartmentId = request.SubscriberDepartmentId,
					Name = Truncate(source.Name, 200),
					Description = source.Description,
					IsShared = true,
					Tags = tag,
					CreatedOn = now,
					CreatedByUserId = request.UserId
				}, cancellationToken, true);
			}
			else
			{
				foreach (var old in (await _questions.GetForBankAsync(request.SubscriberDepartmentId, bank.LmsQuestionBankId) ?? new List<LmsQuestion>()).Where(q => !q.IsRetired))
				{
					old.IsRetired = true;
					await _questions.UpdateAsync(old, cancellationToken, true);
				}
			}

			foreach (var question in (await _questions.GetForBankAsync(request.PublisherDepartmentId, sourceBankId) ?? new List<LmsQuestion>()).Where(q => !q.IsRetired))
			{
				var copy = JsonConvert.DeserializeObject<LmsQuestion>(JsonConvert.SerializeObject(question));
				copy.LmsQuestionId = 0;
				copy.DepartmentId = request.SubscriberDepartmentId;
				copy.LmsQuestionBankId = bank.LmsQuestionBankId;
				copy.SystemKey = null;
				copy.CreatedOn = now;
				copy.CreatedByUserId = request.UserId;
				await _questions.InsertAsync(copy, cancellationToken, true);
			}

			return bank.LmsQuestionBankId;
		}
	}
}
