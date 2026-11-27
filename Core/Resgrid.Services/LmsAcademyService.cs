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
using Resgrid.Model.Academy;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The Resgrid Academy as a system-published course set (A1, plan §15, R-S2). The system publisher has no department: its
	/// courses, versions, lessons, banks and questions carry DepartmentId 0 and PublisherKind System. Since A2 they are seeded from the
	/// versioned lesson manifests (<see cref="AcademyManifests"/>, one JSON file per lesson shipped with the release) on first use and by
	/// worker 88, idempotently by SystemKey: a lesson whose revision rises gets a new minor version (completions stand, learners in
	/// progress keep theirs, unstarted enrollments move) or, for a major change, a new major version recorded for worker 88, which notifies
	/// or re-assigns the completed learners of departments that assigned it (<see cref="IAcademyManifestUpdateService"/>). A moved media
	/// base re-links the stored videos and captions. Every department runs the courses for free with no seat, assigns lessons and role
	/// paths with Assign Training (126), and sees only lessons its modules, flags and add-ons allow. Academy completions never count
	/// against plan limits or storage (nothing is stored per department but enrollments, progress and attempts).
	/// </summary>
	public class LmsAcademyService : ILmsAcademyService
	{
		/// <summary>The pass mark of a three-question check: two of three.</summary>
		public const int CheckPassMark = 66;

		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.Academy.AcademyCourses", typeof(Resgrid.Localization.SupportedLocales).Assembly);
		private static readonly SemaphoreSlim SeedLock = new SemaphoreSlim(1, 1);
		private static int _seededVersion;

		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCourseModulesRepository _modules;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsQuestionBanksRepository _banks;
		private readonly ILmsQuestionsRepository _questions;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsPathAssignmentsRepository _paths;
		private readonly ILmsEntitlementService _entitlements;
		private readonly ILmsLearnerService _learner;
		private readonly IContentAudienceService _audienceService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IAcademyLessonUpdatesRepository _updates;

		public LmsAcademyService(ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions, ILmsCourseModulesRepository modules, ILmsLessonsRepository lessons,
			ILmsQuestionBanksRepository banks, ILmsQuestionsRepository questions, ILmsEnrollmentsRepository enrollments, ILmsPathAssignmentsRepository paths,
			ILmsEntitlementService entitlements, ILmsLearnerService learner, IContentAudienceService audienceService, IEventAggregator eventAggregator,
			IAcademyLessonUpdatesRepository updates)
		{
			_updates = updates;
			_courses = courses;
			_versions = versions;
			_modules = modules;
			_lessons = lessons;
			_banks = banks;
			_questions = questions;
			_enrollments = enrollments;
			_paths = paths;
			_entitlements = entitlements;
			_learner = learner;
			_audienceService = audienceService;
			_eventAggregator = eventAggregator;
		}

		/// <summary>For tests: forget that this process already seeded.</summary>
		internal static void ResetSeedMemo() => _seededVersion = 0;

		/// <summary>
		/// Academy text by key: a lesson's title, question and option keys ("05-calls.Title", "05-calls.q1", "05-calls.q1.a") come from the
		/// lesson manifest; the shared texts ("Check.Title", "TrueFalse.t", the role paths) from the Academy resources.
		/// </summary>
		public string Text(string key, string locale) => ManifestText(key, locale, true) ?? LmsText.Lookup(Strings, key, locale);

		/// <summary>The manifest's text for a lesson key, or null when the key is not a lesson key (or that locale lacks it and no fallback).</summary>
		internal static string ManifestText(string key, string locale, bool fallBack)
		{
			var map = ManifestMap(key);
			if (map == null)
				return null;
			if (fallBack)
				return AcademyManifests.Text(map, locale);
			return map.TryGetValue(locale ?? AcademyManifests.DefaultLocale, out var exact) && !string.IsNullOrWhiteSpace(exact) ? exact : null;
		}

		private static Dictionary<string, string> ManifestMap(string key)
		{
			if (string.IsNullOrWhiteSpace(key))
				return null;
			var parts = key.Split('.');
			var lesson = AcademyManifests.FindLesson(parts[0]);
			if (lesson == null || parts.Length < 2)
				return null;
			if (parts.Length == 2 && parts[1] == "Title")
				return lesson.Title;
			var question = (lesson.Check ?? new List<AcademyManifestQuestion>()).FirstOrDefault(q => string.Equals(q.Key, parts[1], StringComparison.OrdinalIgnoreCase));
			if (question == null)
				return null;
			if (parts.Length == 2)
				return question.Text;
			return parts.Length == 3 ? (question.Options ?? new List<AcademyManifestOption>()).FirstOrDefault(o => string.Equals(o.Key, parts[2], StringComparison.Ordinal))?.Text : null;
		}

		/// <summary>The text in English (the stored base text) and in every other product locale that translates it.</summary>
		private (string English, Dictionary<string, string> Others) Localized(string key)
		{
			var english = Text(key, "en");
			var others = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var locale in Resgrid.Localization.SupportedLocales.GetSupportedCultures().Where(l => l != "en"))
			{
				var value = ManifestMap(key) != null ? ManifestText(key, locale, false) : LmsText.TryGet(Strings, key, new CultureInfo(locale));
				if (!string.IsNullOrWhiteSpace(value) && value != english)
					others[locale] = value;
			}
			return (english, others);
		}

		#region Seeding

		public async Task<int> EnsureSeededAsync(CancellationToken cancellationToken = default)
		{
			var mediaBase = AcademyManifests.MediaBase;
			var fingerprint = AcademyManifests.SeedFingerprint(mediaBase);
			if (_seededVersion == fingerprint)
				return 0;

			await SeedLock.WaitAsync(cancellationToken);
			try
			{
				if (_seededVersion == fingerprint)
					return 0;

				var changed = 0;
				foreach (var definition in AcademyCourseManifest.Courses)
				{
					try
					{
						changed += await SeedCourseAsync(definition, cancellationToken) ? 1 : 0;
					}
					catch (Exception ex)
					{
						// One lesson failing (a concurrent seeder won the unique key) does not stop the others; the next run retries.
						Logging.LogException(ex, $"Seeding Academy course {definition.LessonId} failed.");
						return changed;
					}
				}

				_seededVersion = fingerprint;
				return changed;
			}
			finally
			{
				SeedLock.Release();
			}
		}

		/// <summary>
		/// Creates the system course, or re-seeds it when its lesson's manifest revision is ahead of the stored one (a new minor version, or a
		/// major one when any revision since is a major change). True when a version was written. An up-to-date course only has its media
		/// re-linked when the media base moved (not counted).
		/// </summary>
		public async Task<bool> SeedCourseAsync(AcademyCourseDefinition definition, CancellationToken cancellationToken)
		{
			var mediaBase = AcademyManifests.MediaBase;
			var course = await _courses.GetBySystemKeyAsync(definition.SystemKey);
			if (course != null && course.ManifestVersion >= definition.Revision)
			{
				await RelinkMediaAsync(course, definition, mediaBase, cancellationToken);
				return false;
			}

			var now = DateTime.UtcNow;
			var bank = await EnsureBankAsync(definition, now, cancellationToken);
			var title = Text(definition.LessonId + ".Title", "en");

			if (course == null)
			{
				course = new LmsCourse
				{
					DepartmentId = 0,
					PublisherKind = (int)LmsPublisherKinds.System,
					SystemKey = definition.SystemKey,
					// Stamped only once the version below is in force, so a seed that stops half-way is finished by the next run.
					ManifestVersion = 0,
					Title = title,
					Code = "RG-ACADEMY-" + definition.LessonId,
					Status = (int)ProtocolStatuses.Draft,
					IsSelfEnrollable = true,
					VisibilityKeys = string.IsNullOrWhiteSpace(definition.VisibilityKeys) ? null : definition.VisibilityKeys,
					CreatedOn = now
				};
				await _courses.InsertAsync(course, cancellationToken, true);
			}

			var versions = await _versions.GetForCourseAsync(0, course.LmsCourseId) ?? new List<LmsCourseVersion>();
			var current = versions.FirstOrDefault(v => v.Status == (int)ProtocolVersionStatuses.Published);
			// A major change since the stored revision (never for a first seed) opens a new major version with its policy (B's R-L12 rules).
			var majorChange = current != null && definition.Manifest != null ? definition.Manifest.LatestMajorSince(course.ManifestVersion) : null;
			var policy = majorChange == null ? LmsMajorChangePolicies.Nothing
				: string.Equals(majorChange.Policy, AcademyManifestRevision.PolicyReassign, StringComparison.OrdinalIgnoreCase) ? LmsMajorChangePolicies.Reassign : LmsMajorChangePolicies.Notify;
			var (major, minor) = ProtocolLifecycle.NextNumber(current?.Major ?? 0, current?.Minor ?? 0, majorChange != null);
			var revisionNote = definition.Manifest?.Current;
			var version = new LmsCourseVersion
			{
				DepartmentId = 0,
				LmsCourseId = course.LmsCourseId,
				Major = major,
				Minor = minor,
				Status = (int)ProtocolVersionStatuses.Published,
				Title = title,
				ContentJson = ProtocolContentDocument.FromHtml(string.Empty).ToJson(),
				ContentHtml = string.Empty,
				ChangeSummary = Truncate("Resgrid Academy " + definition.LessonId + " revision " + definition.Revision.ToString(CultureInfo.InvariantCulture) +
					(string.IsNullOrWhiteSpace(revisionNote?.Release) ? string.Empty : " (release " + revisionNote.Release + ")") +
					(string.IsNullOrWhiteSpace(revisionNote?.Note) ? string.Empty : ": " + revisionNote.Note), ProtocolVersion.ChangeSummaryMaxLength),
				Locale = "en",
				Hours = definition.Hours,
				ManifestVersion = definition.Revision,
				MajorChangePolicy = (int)policy,
				ReassignDueDays = policy == LmsMajorChangePolicies.Reassign ? Math.Clamp(majorChange.ReassignDueDays ?? 30, 1, 730) : (int?)null,
				EffectiveOn = now,
				CreatedOn = now,
				SubmittedOn = now,
				ApprovedOn = now,
				PublishedOn = now
			};
			await _versions.InsertAsync(version, cancellationToken, true);

			var module = new LmsCourseModule { DepartmentId = 0, LmsCourseVersionId = version.LmsCourseVersionId, Title = title, SortOrder = 1 };
			await _modules.InsertAsync(module, cancellationToken, true);

			var video = new LmsLesson
			{
				DepartmentId = 0,
				LmsCourseVersionId = version.LmsCourseVersionId,
				LmsCourseModuleId = module.LmsCourseModuleId,
				Kind = (int)LmsLessonKinds.Video,
				Title = title,
				SortOrder = 1,
				IsRequired = true,
				VideoSource = (int)LmsVideoSources.Academy,
				VideoUrl = AcademyCourseManifest.VideoUrlFor(mediaBase, definition),
				CaptionTracksJson = JsonConvert.SerializeObject(AcademyCourseManifest.CaptionTracksFor(mediaBase, definition)),
				// Watch most of it: the Academy player reports position, so the check follows the video, not a skip to the end.
				RequiredWatchPercent = 80
			};
			var check = new LmsLesson
			{
				DepartmentId = 0,
				LmsCourseVersionId = version.LmsCourseVersionId,
				LmsCourseModuleId = module.LmsCourseModuleId,
				Kind = (int)LmsLessonKinds.Assessment,
				Title = Text("Check.Title", "en"),
				SortOrder = 2,
				IsRequired = true,
				LmsQuestionBankId = bank.LmsQuestionBankId,
				DrawCount = 0,
				PassMark = CheckPassMark,
				AttemptLimit = 0,
				TimeLimitMinutes = 0,
				ShowAnswersPolicy = (int)LmsShowAnswersPolicies.Always,
				RandomizeAnswers = true
			};
			await _lessons.InsertAsync(video, cancellationToken, true);
			await _lessons.InsertAsync(check, cancellationToken, true);

			version.ContentHash = LmsCourseContent.ComputeHash(version, new[] { module }, new[] { video, check });
			await _versions.UpdateAsync(version, cancellationToken, true);

			if (current != null && ProtocolLifecycle.CanMove(current.Status, (int)ProtocolVersionStatuses.Superseded))
			{
				current.Status = (int)ProtocolVersionStatuses.Superseded;
				current.SupersededOn = now;
				await _versions.TryUpdateAsync(current, current.RowVersion, cancellationToken);
			}

			course.CurrentVersionId = version.LmsCourseVersionId;
			course.Status = (int)ProtocolStatuses.Published;
			course.ManifestVersion = definition.Revision;
			course.Title = title;
			course.VisibilityKeys = string.IsNullOrWhiteSpace(definition.VisibilityKeys) ? null : definition.VisibilityKeys;
			course.UpdatedOn = now;
			var won = await _courses.TryUpdateAsync(course, course.RowVersion, cancellationToken);

			// A major change is recorded once (unique per course and revision) for worker 88, which applies its policy department by department.
			if (won && majorChange != null && _updates != null)
				await _updates.InsertIfNewAsync(new AcademyLessonUpdate
				{
					SystemKey = definition.SystemKey,
					LmsCourseId = course.LmsCourseId,
					LmsCourseVersionId = version.LmsCourseVersionId,
					Revision = definition.Revision,
					Policy = (int)policy,
					ReassignDueDays = version.ReassignDueDays,
					CreatedOn = now
				}, cancellationToken);

			// R-L12: a re-seed is a minor version; unstarted enrollments (any department) move to it, started ones keep theirs.
			if (current != null)
				foreach (var enrollment in (await _enrollments.GetOpenForCourseAnyDepartmentAsync(course.LmsCourseId) ?? new List<LmsEnrollment>()).Where(e => !e.StartedOn.HasValue))
				{
					var expected = enrollment.RowVersion;
					enrollment.LmsCourseVersionId = version.LmsCourseVersionId;
					await _enrollments.TryUpdateAsync(enrollment, expected, cancellationToken);
				}

			return true;
		}

		/// <summary>
		/// The stored video and caption tracks of an up-to-date course follow the media base (an air-gapped media pack set after the first
		/// seed): a moved base rewrites them in place and re-hashes the version. Nothing is written when they already match.
		/// </summary>
		private async Task RelinkMediaAsync(LmsCourse course, AcademyCourseDefinition definition, string mediaBase, CancellationToken cancellationToken)
		{
			if (!course.CurrentVersionId.HasValue || mediaBase == null)
				return;
			var lessons = await _lessons.GetForVersionAsync(0, course.CurrentVersionId.Value) ?? new List<LmsLesson>();
			var video = lessons.FirstOrDefault(l => l.Kind == (int)LmsLessonKinds.Video && l.VideoSource == (int)LmsVideoSources.Academy);
			if (video == null)
				return;
			var url = AcademyCourseManifest.VideoUrlFor(mediaBase, definition);
			var captions = JsonConvert.SerializeObject(AcademyCourseManifest.CaptionTracksFor(mediaBase, definition));
			if (string.Equals(video.VideoUrl, url, StringComparison.Ordinal) && string.Equals(video.CaptionTracksJson, captions, StringComparison.Ordinal))
				return;

			video.VideoUrl = url;
			video.CaptionTracksJson = captions;
			await _lessons.UpdateAsync(video, cancellationToken, true);
			var version = await _versions.GetAnyByIdAsync(course.CurrentVersionId.Value);
			var modules = version == null ? null : await _modules.GetForVersionAsync(0, version.LmsCourseVersionId);
			if (version != null && modules != null)
			{
				version.ContentHash = LmsCourseContent.ComputeHash(version, modules, lessons);
				await _versions.TryUpdateAsync(version, version.RowVersion, cancellationToken);
			}
		}

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		/// <summary>The lesson's question bank and its three questions (English text with the 9 other locales as translations).</summary>
		private async Task<LmsQuestionBank> EnsureBankAsync(AcademyCourseDefinition definition, DateTime now, CancellationToken cancellationToken)
		{
			var bank = await _banks.GetBySystemKeyAsync(definition.SystemKey);
			if (bank == null)
			{
				bank = new LmsQuestionBank
				{
					DepartmentId = 0,
					Name = "Academy " + definition.LessonId,
					IsShared = false,
					SystemKey = definition.SystemKey,
					CreatedOn = now
				};
				await _banks.InsertAsync(bank, cancellationToken, true);
			}

			var existing = (await _questions.GetForBankAsync(0, bank.LmsQuestionBankId) ?? new List<LmsQuestion>()).Where(q => q.SystemKey != null)
				.ToDictionary(q => q.SystemKey, StringComparer.OrdinalIgnoreCase);
			var order = 0;
			foreach (var definitionQuestion in definition.Questions)
			{
				order++;
				var key = definition.LessonId + "." + definitionQuestion.Key;
				var question = existing.TryGetValue(key, out var stored) ? stored : new LmsQuestion
				{
					DepartmentId = 0,
					LmsQuestionBankId = bank.LmsQuestionBankId,
					SystemKey = key,
					CreatedOn = now
				};
				var (text, textTranslations) = Localized(key);
				var options = new List<LmsQuestionOption>();
				var optionTranslations = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
				foreach (var optionKey in definitionQuestion.OptionKeys)
				{
					var resource = definitionQuestion.Kind == LmsQuestionKinds.TrueFalse ? "TrueFalse." + optionKey : key + "." + optionKey;
					var (optionText, translated) = Localized(resource);
					options.Add(new LmsQuestionOption { Key = optionKey, Text = optionText, IsCorrect = string.Equals(optionKey, definitionQuestion.Correct, StringComparison.Ordinal) });
					foreach (var pair in translated)
					{
						if (!optionTranslations.TryGetValue(pair.Key, out var map))
							optionTranslations[pair.Key] = map = new Dictionary<string, string>(StringComparer.Ordinal);
						map[optionKey] = pair.Value;
					}
				}

				var translations = new Dictionary<string, LmsQuestionTranslation>(StringComparer.OrdinalIgnoreCase);
				foreach (var locale in textTranslations.Keys.Union(optionTranslations.Keys, StringComparer.OrdinalIgnoreCase))
					translations[locale] = new LmsQuestionTranslation
					{
						Text = textTranslations.TryGetValue(locale, out var t) ? t : null,
						Options = optionTranslations.TryGetValue(locale, out var o) ? o : new Dictionary<string, string>()
					};

				question.Kind = (int)definitionQuestion.Kind;
				question.Text = text;
				question.Options = options;
				question.TranslationsJson = translations.Count == 0 ? null : JsonConvert.SerializeObject(translations);
				question.Points = 1;
				question.SortOrder = order;
				question.IsRetired = false;
				question.UpdatedOn = stored == null ? (DateTime?)null : now;
				await _questions.SaveOrUpdateAsync(question, cancellationToken, true);
			}

			// A question the manifest no longer has is retired (attempts that drew it keep their snapshot).
			foreach (var orphan in existing.Values.Where(q => !definition.Questions.Any(d => string.Equals(definition.LessonId + "." + d.Key, q.SystemKey, StringComparison.OrdinalIgnoreCase)) && !q.IsRetired))
			{
				orphan.IsRetired = true;
				await _questions.UpdateAsync(orphan, cancellationToken, true);
			}

			return bank;
		}

		#endregion Seeding

		#region Catalog, assignment and paths

		public async Task<LmsCourse> GetCourseForLessonAsync(string lessonId)
		{
			var definition = AcademyCourseManifest.Find(lessonId);
			if (definition == null)
				return null;
			await EnsureSeededAsync();
			return await _courses.GetBySystemKeyAsync(definition.SystemKey);
		}

		public async Task<AcademyCatalogView> GetCatalogAsync(LmsActor actor)
		{
			var view = new AcademyCatalogView();
			if (actor == null || !actor.IsCurrentMember)
				return Fail(view, LmsErrors.NotAuthorized);
			if (actor.Gate == null || !actor.Gate.ModuleOn)
				return Fail(view, LmsErrors.NotAuthorized);

			await EnsureSeededAsync();
			var courses = (await _courses.GetSystemCoursesAsync() ?? new List<LmsCourse>()).Where(c => c.SystemKey != null)
				.ToDictionary(c => c.SystemKey, StringComparer.OrdinalIgnoreCase);
			var entitlements = await _entitlements.GetEntitlementsAsync(actor.DepartmentId, courses.Values.Select(c => c.VisibilityKeys));
			var versions = (await _versions.GetVisibleByIdsAsync(actor.DepartmentId, courses.Values.Where(c => c.CurrentVersionId.HasValue).Select(c => c.CurrentVersionId.Value))
				?? new List<LmsCourseVersion>()).ToDictionary(v => v.LmsCourseVersionId);
			var mine = await _enrollments.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsEnrollment>();

			foreach (var definition in AcademyCourseManifest.Courses)
			{
				if (!courses.TryGetValue(definition.SystemKey, out var course) || !entitlements.Allows(course.VisibilityKeys))
					continue;
				var enrollments = mine.Where(e => e.LmsCourseId == course.LmsCourseId).ToList();
				view.Lessons.Add(new AcademyLessonView
				{
					Definition = definition,
					Course = course,
					Version = course.CurrentVersionId.HasValue && versions.TryGetValue(course.CurrentVersionId.Value, out var v) ? v : null,
					MyEnrollment = enrollments.FirstOrDefault(e => e.IsOpen) ?? enrollments.FirstOrDefault(e => e.Status == (int)LmsEnrollmentStatuses.Completed),
					Completed = enrollments.Any(e => e.Status == (int)LmsEnrollmentStatuses.Completed)
				});
			}

			var visible = view.Lessons.ToDictionary(l => l.Definition.LessonId, StringComparer.OrdinalIgnoreCase);
			var assignments = actor.MayAssignAcademy ? await _paths.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsPathAssignment>() : new List<LmsPathAssignment>();
			var myPaths = await _paths.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsPathAssignment>();
			foreach (var path in AcademyCourseManifest.RolePaths)
			{
				var pathView = new AcademyPathView
				{
					Path = path,
					Lessons = path.LessonIds.Where(visible.ContainsKey).Select(id => visible[id]).ToList(),
					AssignedMembers = assignments.Where(a => string.Equals(a.PathKey, path.Key, StringComparison.OrdinalIgnoreCase)).Select(a => a.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
					AssignedToMe = myPaths.Any(a => string.Equals(a.PathKey, path.Key, StringComparison.OrdinalIgnoreCase))
				};
				if (pathView.Lessons.Count > 0)
					view.Paths.Add(pathView);
			}

			view.CanAssign = actor.MayAssignAcademy;
			return view;
		}

		private static AcademyCatalogView Fail(AcademyCatalogView view, LmsErrors error)
		{
			view.Error = error;
			return view;
		}

		public async Task<LmsAssignOutcome> AssignLessonAsync(LmsActor actor, string lessonId, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc,
			CancellationToken cancellationToken = default)
		{
			var course = await GetCourseForLessonAsync(lessonId);
			if (course == null)
				return new LmsAssignOutcome { Error = LmsErrors.NotFound };
			// An officer's own assignment of a lesson: the direct-assignment workflow event (271) too.
			return await _learner.AssignDirectAsync(actor, course.LmsCourseId, who, dueOnUtc, cancellationToken);
		}

		public async Task<LmsAssignOutcome> AssignPathAsync(LmsActor actor, string pathKey, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc,
			CancellationToken cancellationToken = default)
		{
			var outcome = new LmsAssignOutcome();
			var path = AcademyCourseManifest.FindPath(pathKey);
			if (path == null)
				return new LmsAssignOutcome { Error = LmsErrors.NotFound };
			if (actor == null || !actor.MayAssignAcademy || actor.Gate == null || !actor.Gate.ModuleOn)
				return new LmsAssignOutcome { Error = LmsErrors.NotAuthorized };
			if (dueOnUtc.HasValue && dueOnUtc.Value < DateTime.UtcNow.AddMinutes(-5))
				return new LmsAssignOutcome { Error = LmsErrors.InvalidInput };

			var rules = ContentAudienceMatcher.Normalize(who);
			if (rules.Count == 0)
				return new LmsAssignOutcome { Error = LmsErrors.InvalidInput };

			await EnsureSeededAsync(cancellationToken);
			var resolution = await _audienceService.ResolveAsync(actor.DepartmentId, rules);
			outcome.Skipped = resolution?.Skipped ?? new List<ContentAudienceSkip>();
			var members = resolution?.MemberUserIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (members.Count == 0)
				return outcome;

			// The list is recorded per member (A1: plain ordered lists until T3 makes them learning paths, without re-assignment).
			var existing = (await _paths.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsPathAssignment>())
				.Where(a => string.Equals(a.PathKey, path.Key, StringComparison.OrdinalIgnoreCase)).Select(a => a.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			foreach (var userId in members.Where(u => !existing.Contains(u)))
				await _paths.InsertAsync(new LmsPathAssignment
				{
					DepartmentId = actor.DepartmentId,
					PathKey = path.Key,
					UserId = userId,
					AssignedByUserId = actor.UserId,
					AssignedOn = now,
					DueOn = dueOnUtc
				}, cancellationToken, true);

			var memberRules = members.Select(u => (IContentAudienceRule)new ContentAudienceRule(ContentAudienceKinds.User, u)).ToList();
			foreach (var lessonId in path.LessonIds)
			{
				var course = await _courses.GetBySystemKeyAsync(AcademyCourseManifest.SystemKeyFor(lessonId));
				if (course == null || !await _entitlements.IsVisibleAsync(actor.DepartmentId, course))
					continue;
				var lessonOutcome = await _learner.AssignAsync(actor, course.LmsCourseId, memberRules, dueOnUtc, LmsEnrollmentSources.RolePath, path.Key, cancellationToken);
				outcome.Created += lessonOutcome.Created;
				outcome.AlreadyEnrolled += lessonOutcome.AlreadyEnrolled;
				if (lessonOutcome.Error != LmsErrors.None && outcome.Error == LmsErrors.None)
					outcome.Error = lessonOutcome.Error;
			}

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.AcademyPathAssigned,
				After = JsonConvert.SerializeObject(new { path = path.Key, members = members.Count, dueOnUtc }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return outcome;
		}

		public async Task<List<AcademyPathProgressRow>> GetPathProgressAsync(LmsActor actor, string pathKey)
		{
			var rows = new List<AcademyPathProgressRow>();
			var path = AcademyCourseManifest.FindPath(pathKey);
			if (path == null || actor == null || !actor.MayAssignAcademy)
				return rows;

			var assignments = (await _paths.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsPathAssignment>())
				.Where(a => string.Equals(a.PathKey, path.Key, StringComparison.OrdinalIgnoreCase)).ToList();
			if (assignments.Count == 0)
				return rows;

			var courses = new Dictionary<string, LmsCourse>(StringComparer.OrdinalIgnoreCase);
			foreach (var lessonId in path.LessonIds)
			{
				var course = await _courses.GetBySystemKeyAsync(AcademyCourseManifest.SystemKeyFor(lessonId));
				if (course != null && await _entitlements.IsVisibleAsync(actor.DepartmentId, course))
					courses[lessonId] = course;
			}

			var enrollments = await _enrollments.GetForCoursesAsync(actor.DepartmentId, courses.Values.Select(c => c.LmsCourseId)) ?? new List<LmsEnrollment>();
			foreach (var assignment in assignments.GroupBy(a => a.UserId, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(a => a.AssignedOn).First()))
			{
				var row = new AcademyPathProgressRow { UserId = assignment.UserId, AssignedOn = assignment.AssignedOn, DueOn = assignment.DueOn, Total = courses.Count };
				foreach (var pair in courses)
				{
					var done = enrollments.Any(e => e.LmsCourseId == pair.Value.LmsCourseId && string.Equals(e.UserId, assignment.UserId, StringComparison.OrdinalIgnoreCase) &&
						e.Status == (int)LmsEnrollmentStatuses.Completed);
					row.Lessons[pair.Key] = done;
					if (done)
						row.Completed++;
				}
				rows.Add(row);
			}

			return rows;
		}

		#endregion Catalog, assignment and paths
	}
}
