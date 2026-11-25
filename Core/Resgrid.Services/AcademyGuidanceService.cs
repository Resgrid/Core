using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Academy;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// A3 in-product guidance (protocols-training-knowledge-plan §15): guided tours per role from the tour manifests (first-run tasks built on
	/// the walkthrough lessons), the release's "What's new" micro-lesson, both dismissible and remembered per member (AcademyGuidanceStates),
	/// and the free Help-menu keyword search over the Academy lessons the department can see plus the Admin Assist reference corpus (feature
	/// labels and purposes in the member's language, and the reviewed reference articles). No model is called here.
	/// </summary>
	public class AcademyGuidanceService : IAcademyGuidanceService
	{
		/// <summary>How long after its release a "What's new" lesson is offered on the dashboard.</summary>
		public const int WhatsNewDays = 60;

		public const int MaxLessonHits = 8;
		public const int MaxDocHits = 10;
		public const int MaxReferenceHits = 4;
		public const int MaxQueryLength = 200;

		private static readonly ResourceManager AdminAssistStrings = new ResourceManager(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
		private static readonly Regex Words = new Regex(@"[\p{L}\p{N}]+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly IAcademyGuidanceStatesRepository _states;
		private readonly ILmsEntitlementService _entitlements;
		private readonly IDispatchAccessService _dispatch;
		private readonly IAuthorizationService _authorization;
		private readonly IAdminAssistReferenceSearch _reference;
		private readonly IAdminAssistCatalog _catalog;

		public AcademyGuidanceService(IAcademyGuidanceStatesRepository states, ILmsEntitlementService entitlements, IDispatchAccessService dispatch,
			IAuthorizationService authorization, IAdminAssistReferenceSearch reference, IAdminAssistCatalog catalog)
		{
			_states = states;
			_entitlements = entitlements;
			_dispatch = dispatch;
			_authorization = authorization;
			_reference = reference;
			_catalog = catalog;
		}

		private static bool Valid(AcademyGuidanceActor actor) => actor != null && actor.DepartmentId > 0 && !string.IsNullOrWhiteSpace(actor.UserId);

		private static string Locale(AcademyGuidanceActor actor) => string.IsNullOrWhiteSpace(actor?.Locale) ? AcademyManifests.DefaultLocale : actor.Locale;

		private static string LessonUrl(string lessonId)
		{
			var lesson = AcademyCatalog.Find(lessonId);
			return lesson == null ? null : AcademyCatalog.LessonUrl(Config.SystemBehaviorConfig.AcademyUrl, lesson);
		}

		private static IEnumerable<string> TourKeys(IEnumerable<AcademyTourManifest> tours) =>
			tours.Where(t => t != null).SelectMany(t => t.Tasks ?? new List<AcademyTourTask>()).Select(t => string.Join(",", t.Visibility ?? new List<string>()));

		private static IEnumerable<string> ReleaseKeys(IEnumerable<AcademyWhatsNewManifest> releases) =>
			releases.Where(r => r != null).SelectMany(r => r.Items ?? new List<AcademyWhatsNewItem>()).Select(i => string.Join(",", i.Visibility ?? new List<string>()));

		private static IEnumerable<string> LessonKeys() => AcademyManifests.Lessons.Select(l => l.VisibilityKeys);

		/// <summary>
		/// What the department holds for the visibility keys a page needs (fails closed on error). An add-on key costs a billing read, so the
		/// dashboard card asks for module and flag keys only (<paramref name="withAddons"/> false): an add-on item stays hidden there.
		/// </summary>
		private async Task<AcademyEntitlements> EntitlementsAsync(int departmentId, IEnumerable<string> visibilityKeys, bool withAddons = true)
		{
			var keys = (visibilityKeys ?? Enumerable.Empty<string>()).SelectMany(k => (k ?? string.Empty).Split(','))
				.Select(k => k.Trim()).Where(k => k.Length > 0 && (withAddons || !k.StartsWith("addon:", StringComparison.OrdinalIgnoreCase)))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			try
			{
				return await _entitlements.GetEntitlementsAsync(departmentId, keys) ?? new AcademyEntitlements();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Academy entitlements could not be read for department {departmentId}.");
				return new AcademyEntitlements();
			}
		}

		private static bool Allows(AcademyEntitlements entitlements, IEnumerable<string> visibility) =>
			entitlements.Allows(string.Join(",", (visibility ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v))));

		private async Task<Dictionary<string, AcademyGuidanceState>> StatesAsync(AcademyGuidanceActor actor, CancellationToken cancellationToken) =>
			(await _states.GetForUserAsync(actor.DepartmentId, actor.UserId, cancellationToken) ?? new List<AcademyGuidanceState>())
				.Where(s => s != null && s.DepartmentId == actor.DepartmentId && string.Equals(s.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
				.GroupBy(s => s.GuidanceKey, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.UpdatedOn).First(), StringComparer.OrdinalIgnoreCase);

		#region Tours

		public async Task<string> GetDefaultTourKeyAsync(AcademyGuidanceActor actor)
		{
			if (!Valid(actor))
				return AcademyTourManifest.Member;
			if (actor.IsDepartmentAdmin)
				return AcademyTourManifest.Administrator;
			try
			{
				if (_dispatch != null && await _dispatch.CanUseDispatchAsync(actor.DepartmentId, actor.UserId))
					return AcademyTourManifest.Dispatcher;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatch access could not be read for the Academy tour.");
			}
			if (actor.MayAssignTraining)
				return AcademyTourManifest.TrainingOfficer;
			try
			{
				var scope = await _authorization.GetTrainingManagementScopeAsync(actor.UserId, actor.DepartmentId);
				if (scope != null && scope.IsManager)
					return AcademyTourManifest.TrainingOfficer;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training rights could not be read for the Academy tour.");
			}
			return AcademyTourManifest.Member;
		}

		/// <summary>A tour in the member's language with the tasks the department can see and the member's progress.</summary>
		public static AcademyTourView ToView(AcademyTourManifest tour, AcademyGuidanceState state, AcademyEntitlements entitlements, string locale)
		{
			var current = state != null && state.Revision >= tour.Revision;
			var done = current || state?.State == (int)AcademyGuidanceStatuses.Active ? state?.DoneTasks ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return new AcademyTourView
			{
				Key = tour.Key,
				Title = AcademyManifests.Text(tour.Title, locale),
				Intro = AcademyManifests.Text(tour.Intro, locale),
				Revision = tour.Revision,
				Dismissed = current && state.State == (int)AcademyGuidanceStatuses.Dismissed,
				Tasks = (tour.Tasks ?? new List<AcademyTourTask>()).Where(t => Allows(entitlements, t.Visibility)).Select(t => new AcademyTourTaskView
				{
					Key = t.Key,
					Title = AcademyManifests.Text(t.Title, locale),
					Hint = AcademyManifests.Text(t.Hint, locale),
					PageUrl = t.Page?.Url,
					LessonId = t.LessonId,
					LessonUrl = string.IsNullOrWhiteSpace(t.LessonId) ? null : LessonUrl(t.LessonId),
					Done = done.Contains(t.Key)
				}).ToList()
			};
		}

		public async Task<List<AcademyTourView>> GetToursAsync(AcademyGuidanceActor actor, CancellationToken cancellationToken = default)
		{
			var views = new List<AcademyTourView>();
			if (!Valid(actor))
				return views;
			var mine = await GetDefaultTourKeyAsync(actor);
			var states = await StatesAsync(actor, cancellationToken);
			var entitlements = await EntitlementsAsync(actor.DepartmentId, TourKeys(AcademyManifests.Tours));
			foreach (var tour in AcademyManifests.Tours)
			{
				states.TryGetValue(tour.GuidanceKey, out var state);
				var view = ToView(tour, state, entitlements, Locale(actor));
				view.IsMine = string.Equals(tour.Key, mine, StringComparison.OrdinalIgnoreCase);
				if (view.Tasks.Count > 0)
					views.Add(view);
			}
			return views.OrderByDescending(v => v.IsMine).ToList();
		}

		public async Task<bool> SetTaskDoneAsync(AcademyGuidanceActor actor, string tourKey, string taskKey, bool done, CancellationToken cancellationToken = default)
		{
			var tour = AcademyManifests.FindTour(tourKey);
			if (!Valid(actor) || tour == null || string.IsNullOrWhiteSpace(taskKey))
				return false;
			var task = (tour.Tasks ?? new List<AcademyTourTask>()).FirstOrDefault(t => string.Equals(t.Key, taskKey.Trim(), StringComparison.OrdinalIgnoreCase));
			if (task == null)
				return false;

			var states = await StatesAsync(actor, cancellationToken);
			states.TryGetValue(tour.GuidanceKey, out var state);
			var tasks = state?.DoneTasks ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (done)
				tasks.Add(task.Key);
			else
				tasks.Remove(task.Key);

			var entitlements = await EntitlementsAsync(actor.DepartmentId, TourKeys(new[] { tour }));
			var visible = (tour.Tasks ?? new List<AcademyTourTask>()).Where(t => Allows(entitlements, t.Visibility)).Select(t => t.Key).ToList();
			var complete = visible.Count > 0 && visible.All(tasks.Contains);
			await SaveAsync(actor, tour.GuidanceKey, complete ? AcademyGuidanceStatuses.Completed
				: state?.State == (int)AcademyGuidanceStatuses.Dismissed && state.Revision >= tour.Revision ? AcademyGuidanceStatuses.Dismissed : AcademyGuidanceStatuses.Active,
				tour.Revision, tasks, cancellationToken);
			return true;
		}

		public async Task<bool> ReopenTourAsync(AcademyGuidanceActor actor, string tourKey, CancellationToken cancellationToken = default)
		{
			var tour = AcademyManifests.FindTour(tourKey);
			if (!Valid(actor) || tour == null)
				return false;
			var states = await StatesAsync(actor, cancellationToken);
			states.TryGetValue(tour.GuidanceKey, out var state);
			await SaveAsync(actor, tour.GuidanceKey, AcademyGuidanceStatuses.Active, tour.Revision, state?.DoneTasks ?? new HashSet<string>(), cancellationToken);
			return true;
		}

		public async Task<bool> DismissAsync(AcademyGuidanceActor actor, string guidanceKey, CancellationToken cancellationToken = default)
		{
			if (!Valid(actor) || string.IsNullOrWhiteSpace(guidanceKey))
				return false;
			var key = guidanceKey.Trim();
			var states = await StatesAsync(actor, cancellationToken);
			states.TryGetValue(key, out var state);
			if (key.StartsWith(AcademyManifests.TourKeyPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var tour = AcademyManifests.FindTour(key.Substring(AcademyManifests.TourKeyPrefix.Length));
				if (tour == null)
					return false;
				await SaveAsync(actor, tour.GuidanceKey, AcademyGuidanceStatuses.Dismissed, tour.Revision, state?.DoneTasks ?? new HashSet<string>(), cancellationToken);
				return true;
			}
			if (key.StartsWith(AcademyManifests.WhatsNewKeyPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var release = AcademyManifests.FindRelease(key.Substring(AcademyManifests.WhatsNewKeyPrefix.Length));
				if (release == null)
					return false;
				await SaveAsync(actor, release.GuidanceKey, AcademyGuidanceStatuses.Dismissed, 1, new HashSet<string>(), cancellationToken);
				return true;
			}
			return false;
		}

		private Task<AcademyGuidanceState> SaveAsync(AcademyGuidanceActor actor, string key, AcademyGuidanceStatuses status, int revision, IEnumerable<string> tasks,
			CancellationToken cancellationToken)
		{
			var joined = string.Join(",", (tasks ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.Ordinal));
			return _states.UpsertAsync(new AcademyGuidanceState
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				GuidanceKey = key,
				State = (int)status,
				Revision = revision,
				TasksDone = joined.Length > AcademyGuidanceState.TasksDoneMaxLength ? joined.Substring(0, AcademyGuidanceState.TasksDoneMaxLength) : joined,
				UpdatedOn = DateTime.UtcNow
			}, cancellationToken);
		}

		#endregion Tours

		#region What's new

		public static AcademyWhatsNewView ToView(AcademyWhatsNewManifest release, AcademyGuidanceState state, AcademyEntitlements entitlements, string locale) => new AcademyWhatsNewView
		{
			Release = release.Release,
			PublishedOn = release.PublishedOn,
			Title = AcademyManifests.Text(release.Title, locale),
			Summary = AcademyManifests.Text(release.Summary, locale),
			Minutes = release.Minutes,
			Dismissed = state != null && state.State != (int)AcademyGuidanceStatuses.Active,
			Items = (release.Items ?? new List<AcademyWhatsNewItem>()).Where(i => Allows(entitlements, i.Visibility)).Select(i => new AcademyWhatsNewItemView
			{
				Key = i.Key,
				Title = AcademyManifests.Text(i.Title, locale),
				Body = AcademyManifests.Text(i.Body, locale),
				PageUrl = i.Page?.Url,
				LessonUrl = string.IsNullOrWhiteSpace(i.LessonId) ? null : LessonUrl(i.LessonId)
			}).ToList()
		};

		public async Task<AcademyWhatsNewView> GetWhatsNewAsync(AcademyGuidanceActor actor, string release, CancellationToken cancellationToken = default)
		{
			if (!Valid(actor))
				return null;
			var manifest = string.IsNullOrWhiteSpace(release) ? AcademyManifests.WhatsNew.FirstOrDefault(w => w.PublishedOn <= DateTime.UtcNow) : AcademyManifests.FindRelease(release);
			if (manifest == null)
				return null;
			var states = await StatesAsync(actor, cancellationToken);
			states.TryGetValue(manifest.GuidanceKey, out var state);
			return ToView(manifest, state, await EntitlementsAsync(actor.DepartmentId, ReleaseKeys(new[] { manifest })), Locale(actor));
		}

		#endregion What's new

		public async Task<AcademyGuidanceView> GetDashboardAsync(AcademyGuidanceActor actor, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var view = new AcademyGuidanceView();
			if (!Valid(actor))
				return view;

			var states = await StatesAsync(actor, cancellationToken);
			var tour = AcademyManifests.FindTour(await GetDefaultTourKeyAsync(actor));
			var release = AcademyManifests.WhatsNew.FirstOrDefault(w => w.PublishedOn <= utcNow && w.PublishedOn > utcNow.AddDays(-WhatsNewDays));
			AcademyGuidanceState tourState = null, releaseState = null;
			var tourOpen = tour != null && (!states.TryGetValue(tour.GuidanceKey, out tourState) || tourState.State == (int)AcademyGuidanceStatuses.Active || tourState.Revision < tour.Revision);
			var releaseOpen = release != null && (!states.TryGetValue(release.GuidanceKey, out releaseState) || releaseState.State == (int)AcademyGuidanceStatuses.Active);
			if (!tourOpen && !releaseOpen)
				return view;

			var entitlements = await EntitlementsAsync(actor.DepartmentId,
				TourKeys(tourOpen ? new[] { tour } : Array.Empty<AcademyTourManifest>()).Concat(ReleaseKeys(releaseOpen ? new[] { release } : Array.Empty<AcademyWhatsNewManifest>())), false);
			if (tourOpen)
			{
				var tourView = ToView(tour, tourState, entitlements, Locale(actor));
				tourView.IsMine = true;
				if (tourView.Tasks.Count > 0 && !tourView.Completed)
					view.Tour = tourView;
			}
			if (releaseOpen)
			{
				var releaseView = ToView(release, releaseState, entitlements, Locale(actor));
				if (releaseView.Items.Count > 0)
					view.WhatsNew = releaseView;
			}
			return view;
		}

		#region Help search

		public static List<string> Terms(string query)
		{
			var terms = new List<string>();
			if (string.IsNullOrWhiteSpace(query))
				return terms;
			foreach (Match match in Words.Matches(query.Length > MaxQueryLength ? query.Substring(0, MaxQueryLength) : query))
			{
				var term = match.Value.ToLowerInvariant();
				if (term.Length >= 2 && !terms.Contains(term))
					terms.Add(term);
			}
			return terms.Take(12).ToList();
		}

		/// <summary>A field's share of the query: each term found as a word prefix in the text, weighted.</summary>
		public static double Score(IReadOnlyList<string> terms, string text, double weight)
		{
			if (terms.Count == 0 || string.IsNullOrWhiteSpace(text))
				return 0;
			var words = Words.Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();
			var hits = terms.Count(t => words.Any(w => w.StartsWith(t, StringComparison.Ordinal) || t.Length >= 5 && w.Length >= 5 && t.StartsWith(w, StringComparison.Ordinal)));
			return weight * hits;
		}

		private static string AdminAssistText(string key, string locale)
		{
			if (string.IsNullOrWhiteSpace(key))
				return null;
			try
			{
				return AdminAssistStrings.GetString(key, CultureInfo.GetCultureInfo(locale)) ?? AdminAssistStrings.GetString(key, CultureInfo.GetCultureInfo("en"));
			}
			catch (CultureNotFoundException)
			{
				return AdminAssistStrings.GetString(key, CultureInfo.GetCultureInfo("en"));
			}
		}

		public async Task<AcademyHelpSearchResult> SearchAsync(AcademyGuidanceActor actor, string query, CancellationToken cancellationToken = default)
		{
			var clean = (query ?? string.Empty).Trim();
			if (clean.Length > MaxQueryLength)
				clean = clean.Substring(0, MaxQueryLength);
			var result = new AcademyHelpSearchResult { Query = clean };
			var terms = Terms(clean);
			if (!Valid(actor) || terms.Count == 0)
				return result;
			var locale = Locale(actor);
			var language = locale.Split('-', '_')[0].ToLowerInvariant();

			// Lessons the department can see, by title (member's language and English), topic and the features they teach.
			var entitlements = await EntitlementsAsync(actor.DepartmentId, LessonKeys());
			var capabilities = (_catalog?.Capabilities ?? new List<ProductCapability>()).ToDictionary(c => c.Id, StringComparer.Ordinal);
			foreach (var lesson in AcademyManifests.Lessons.Where(l => entitlements.Allows(l.VisibilityKeys)))
			{
				var featureText = string.Join(" ", (lesson.AdminAssist?.Capabilities ?? new List<string>())
					.Select(id => capabilities.TryGetValue(id, out var c) ? AdminAssistText(c.LabelKey, language) : null).Where(t => t != null));
				var score = Score(terms, AcademyManifests.Text(lesson.Title, language), 3) + Score(terms, AcademyManifests.Text(lesson.Title, "en"), 1) +
					Score(terms, lesson.Topic, 2) + Score(terms, featureText, 1);
				if (score <= 0)
					continue;
				result.Lessons.Add(new AcademyHelpHit
				{
					Kind = AcademyHelpHitKinds.Lesson,
					Id = lesson.Id,
					Title = AcademyManifests.Text(lesson.Title, language),
					Excerpt = lesson.Topic,
					Url = LessonUrl(lesson.Id),
					Minutes = lesson.Minutes,
					Score = score + (lesson.Walkthrough ? 0 : 0.1)
				});
			}
			result.Lessons = result.Lessons.OrderByDescending(h => h.Score).ThenBy(h => h.Title, StringComparer.CurrentCulture).Take(MaxLessonHits).ToList();

			// Product features (Admin Assist capabilities) in the member's language, with their page and public documentation.
			var docs = new List<AcademyHelpHit>();
			foreach (var capability in capabilities.Values)
			{
				var label = AdminAssistText(capability.LabelKey, language);
				var purpose = AdminAssistText(capability.PurposeKey, language);
				var score = Score(terms, label, 3) + Score(terms, purpose, 1);
				if (score <= 0)
					continue;
				docs.Add(new AcademyHelpHit
				{
					Kind = AcademyHelpHitKinds.Feature,
					Id = capability.Id,
					Title = label,
					Excerpt = purpose,
					Url = string.IsNullOrWhiteSpace(capability.DocsPath) ? null : ProductArea.DocsOrigin + capability.DocsPath,
					PageUrl = capability.Location?.Url,
					Score = score
				});
			}

			// The reviewed reference articles (BM25 over the release-pinned corpus; never tenant data).
			try
			{
				var settings = (_catalog?.Settings ?? new List<SettingCatalogEntry>()).Where(s => s.DocumentationId != null)
					.GroupBy(s => s.DocumentationId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
				var areas = (_catalog?.Areas ?? new List<ProductArea>()).ToDictionary(a => a.Id, StringComparer.Ordinal);
				var rank = 0;
				foreach (var hit in _reference?.Search(clean, language, MaxDocHits) ?? Array.Empty<AdminAssistSearchHit>())
				{
					rank++;
					settings.TryGetValue(hit.Id, out var setting);
					var area = setting != null && areas.TryGetValue(setting.AreaId, out var a) ? a : null;
					docs.Add(new AcademyHelpHit
					{
						Kind = AcademyHelpHitKinds.Reference,
						Id = hit.Id,
						Title = AdminAssistText(hit.TitleKey, language) ?? hit.TitleKey,
						Excerpt = hit.Excerpt,
						Url = area?.DocsPath == null ? null : ProductArea.DocsOrigin + area.DocsPath,
						// A setting's page is for administrators; members get the documentation.
						PageUrl = actor.IsDepartmentAdmin ? setting?.Location?.Url : null,
						Score = Math.Max(0.5, 3.5 - rank * 0.25)
					});
				}
			}
			catch (ArgumentException)
			{
				// A query the reference search refuses returns the other results.
			}

			// Features and reference articles score differently, so each keeps its own share: the best features, then the best articles.
			var distinct = docs.GroupBy(d => d.Kind + ":" + d.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
			var articles = distinct.Where(d => d.Kind == AcademyHelpHitKinds.Reference).OrderByDescending(d => d.Score).Take(MaxReferenceHits).ToList();
			var features = distinct.Where(d => d.Kind == AcademyHelpHitKinds.Feature).OrderByDescending(d => d.Score).ThenBy(d => d.Title, StringComparer.CurrentCulture)
				.Take(MaxDocHits - articles.Count).ToList();
			result.Docs = features.Concat(articles).ToList();
			return result;
		}

		#endregion Help search
	}

	/// <summary>
	/// Worker 88's Academy step (A2 release rule): applies each recorded major lesson revision to the departments that require the lesson
	/// (a live T3 requirement on it, directly or through a path; round 5F), read in ascending id order from a stored cursor (no sweep of
	/// every department, no billing gate). The department's own choice on those requirements decides what happens there: completed
	/// learners who are active members and have no open enrollment are notified of the new version, or (where a requirement chose
	/// re-assign) the requirement's members among them are re-assigned it with the requirement's due days; a department whose
	/// requirements all chose "nothing" hears nothing. The manifest's policy only decides whether a major revision is announced at all.
	/// Unstarted enrollments already moved to the new version when it was seeded.
	/// </summary>
	public class AcademyManifestUpdateService : IAcademyManifestUpdateService
	{
		public const int UpdatesPerRun = 10;
		public const int DepartmentsPerPage = 50;

		private readonly IAcademyLessonUpdatesRepository _updates;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserProfileService _profiles;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly ILmsRequirementMembersRepository _requirementMembers;

		public AcademyManifestUpdateService(IAcademyLessonUpdatesRepository updates, ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions,
			ILmsEnrollmentsRepository enrollments, IDepartmentsService departments, IDepartmentSettingsService settings, IUserProfileService profiles,
			Lazy<ICommunicationService> communication, ILmsRequirementMembersRepository requirementMembers)
		{
			_requirementMembers = requirementMembers;
			_updates = updates;
			_courses = courses;
			_versions = versions;
			_enrollments = enrollments;
			_departments = departments;
			_settings = settings;
			_profiles = profiles;
			_communication = communication;
		}

		public async Task<int> ApplyPendingAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var count = 0;
			foreach (var update in await _updates.GetPendingAsync(UpdatesPerRun, cancellationToken) ?? new List<AcademyLessonUpdate>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				var course = await _courses.GetBySystemKeyAsync(update.SystemKey);
				var version = await _versions.GetAnyByIdAsync(update.LmsCourseVersionId);
				var policy = Enum.IsDefined(typeof(LmsMajorChangePolicies), update.Policy) ? (LmsMajorChangePolicies)update.Policy : LmsMajorChangePolicies.Notify;
				if (course != null && version != null && course.LmsCourseId == update.LmsCourseId && policy != LmsMajorChangePolicies.Nothing)
				{
					var after = update.AppliedThroughDepartmentId ?? 0;
					while (true)
					{
						var departments = await _updates.GetAssigningDepartmentIdsAsync(update.LmsCourseId, after, DepartmentsPerPage, cancellationToken) ?? new List<int>();
						if (departments.Count == 0)
							break;
						foreach (var departmentId in departments.Where(d => d > after).OrderBy(d => d))
						{
							try
							{
								count += await ApplyToDepartmentAsync(departmentId, course, version, policy, update.ReassignDueDays, utcNow, cancellationToken);
							}
							catch (Exception ex) when (ex is not OperationCanceledException)
							{
								// One department failing does not hold the others; it is not retried (nobody is told twice).
								Logging.LogException(ex, $"Academy major update {update.AcademyLessonUpdateId} failed for department {departmentId}.");
							}
							after = departmentId;
							update.AppliedThroughDepartmentId = after;
							await _updates.SaveProgressAsync(update, cancellationToken);
						}
					}
				}

				update.CompletedOn = utcNow;
				await _updates.SaveProgressAsync(update, cancellationToken);
			}
			return count;
		}

		/// <summary>
		/// One department, by its own choice (round 5F): the live requirements that cover the lesson decide. Members of a requirement that
		/// chose re-assign who completed an earlier version are re-assigned it (due in that requirement's days); every other completed,
		/// active learner without an open enrollment is notified, unless every requirement chose nothing. The manifest's <paramref name="policy"/>
		/// and <paramref name="reassignDueDays"/> were the caller's question (a "nothing" revision never gets here); the department decides the rest.
		/// </summary>
		internal async Task<int> ApplyToDepartmentAsync(int departmentId, LmsCourse course, LmsCourseVersion version, LmsMajorChangePolicies policy, int? reassignDueDays,
			DateTime utcNow, CancellationToken cancellationToken)
		{
			var requirements = (await _updates.GetRequirementsOnLessonAsync(departmentId, course.LmsCourseId, cancellationToken) ?? new List<AcademyLessonRequirement>())
				.Where(r => r.DepartmentId == departmentId).ToList();
			// The cursor query found the department through a requirement that has since gone: the lesson is no longer required there.
			if (requirements.Count == 0)
				return 0;
			var choices = requirements.Select(r => Enum.IsDefined(typeof(LmsMajorChangePolicies), r.AcademyUpdatePolicy) ? (LmsMajorChangePolicies)r.AcademyUpdatePolicy : LmsMajorChangePolicies.Notify).ToList();
			if (choices.All(c => c == LmsMajorChangePolicies.Nothing))
				return 0;

			var reassigning = requirements.Where(r => r.AcademyUpdatePolicy == (int)LmsMajorChangePolicies.Reassign).ToList();
			var reassignIds = new HashSet<int>(reassigning.Select(r => r.LmsRequirementId));
			var mustReassign = reassignIds.Count == 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				: new HashSet<string>((await _requirementMembers.GetOpenForDepartmentAsync(departmentId) ?? new List<LmsRequirementMember>())
					.Where(m => m.DepartmentId == departmentId && reassignIds.Contains(m.LmsRequirementId) && !m.NotRequired && !m.LeftOn.HasValue && !string.IsNullOrWhiteSpace(m.UserId))
					.Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
			// The requirement's own due window (the shortest when several chose re-assign); 0 days still leaves one day to do it.
			var dueDays = reassigning.Count == 0 ? LmsRequirement.DefaultDueDays : reassigning.Min(r => r.DueDays);

			var enrollments = (await _enrollments.GetForCourseAsync(departmentId, course.LmsCourseId) ?? new List<LmsEnrollment>()).Where(e => e.DepartmentId == departmentId).ToList();
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var open = new HashSet<string>(enrollments.Where(e => e.IsOpen).Select(e => e.UserId), StringComparer.OrdinalIgnoreCase);
			var completed = enrollments
				.Where(e => e.Status == (int)LmsEnrollmentStatuses.Completed && e.LmsCourseVersionId != version.LmsCourseVersionId)
				.Select(e => e.UserId).Where(u => !string.IsNullOrWhiteSpace(u) && active.Contains(u) && !open.Contains(u))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (completed.Count == 0)
				return 0;

			var count = 0;
			var reassigned = new List<string>();
			if (mustReassign.Count > 0)
			{
				var due = utcNow.AddDays(Math.Clamp(dueDays, 1, 730));
				foreach (var userId in completed.Where(mustReassign.Contains))
				{
					var (_, created) = await _enrollments.InsertOpenAsync(new LmsEnrollment
					{
						DepartmentId = departmentId,
						LmsCourseId = course.LmsCourseId,
						LmsCourseVersionId = version.LmsCourseVersionId,
						UserId = userId,
						Source = (int)LmsEnrollmentSources.Reassigned,
						AssignedOn = utcNow,
						DueOn = due,
						Status = (int)LmsEnrollmentStatuses.NotStarted
					}, cancellationToken);
					if (created)
					{
						count++;
						reassigned.Add(userId);
					}
				}
			}

			if (reassigned.Count > 0)
				count += await NotifyAsync(departmentId, reassigned, "Notify.CourseReassigned", course, version);
			var told = new HashSet<string>(reassigned, StringComparer.OrdinalIgnoreCase);
			var notify = completed.Where(u => !told.Contains(u)).ToList();
			if (notify.Count > 0)
				count += await NotifyAsync(departmentId, notify, "Notify.CourseNewVersion", course, version);
			return count;
		}

		private async Task<int> NotifyAsync(int departmentId, IEnumerable<string> userIds, string key, LmsCourse course, LmsCourseVersion version)
		{
			var sent = 0;
			if (!ConfigHelper.CanTransmit(departmentId))
				return 0;
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				if (department == null)
					return 0;
				var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
				var lesson = AcademyManifests.FindLesson(AcademyCourseManifest.LessonIdFromSystemKey(course.SystemKey));
				foreach (var userId in userIds.Distinct(StringComparer.OrdinalIgnoreCase))
				{
					var language = (await _profiles.GetProfileByUserIdAsync(userId))?.Language;
					var title = lesson == null ? course.Title : AcademyManifests.Text(lesson.Title, string.IsNullOrWhiteSpace(language) ? "en" : language);
					var message = string.Format(LmsText.Culture(language), LmsText.Get(key, language), title, version.Label);
					await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, LmsText.Get(key + ".Title", language));
					sent++;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Academy update notifications failed.");
			}
			return sent;
		}
	}
}
