using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The SCORM and cmi5 runtime (T5 item 3). A launch is a token-bound session: the sandboxed frame reads the package's files under
	/// /lms-content/{token}/ (opaque origin, strict CSP, no cookies used), the SCORM adapter commits its data model with the same token, and a
	/// cmi5 AU fetches an auth token once and sends its statements to the minimal LRS with it. Writes are accepted only for the launch's own
	/// learner, registration, session and SCO/AU; satisfaction completes the lesson through the learner service, which applies the course's
	/// gate and lapse rules (fails closed).
	/// </summary>
	public class LmsStandardsRuntimeService : ILmsStandardsRuntimeService
	{
		private const int MaxStatementsPerRequest = 100;
		private const int MaxStatementBodyChars = 1024 * 1024;
		private static readonly TimeSpan ContentCacheFor = TimeSpan.FromMinutes(1);
		private static readonly ConcurrentDictionary<string, (LmsContentSession Session, DateTime Until)> ContentCache = new ConcurrentDictionary<string, (LmsContentSession, DateTime)>(StringComparer.Ordinal);

		/// <summary>Round 10 A8: sessions whose learner may still run the lesson (positive answers only), by session id, for <see cref="ContentCacheFor"/>.</summary>
		private static readonly ConcurrentDictionary<int, DateTime> RunChecks = new ConcurrentDictionary<int, DateTime>();

		private readonly ILmsContentPackagesRepository _packages;
		private readonly ILmsRuntimeRegistrationsRepository _registrations;
		private readonly ILmsRuntimeSessionsRepository _sessions;
		private readonly ILmsCmi5StatementsRepository _statements;
		private readonly ILmsCmi5StateDocumentsRepository _stateDocuments;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsXapiStatementQueue _xapi;
		private readonly IUserProfileService _profiles;
		private readonly ILmsMediaStore _store;

		public LmsStandardsRuntimeService(ILmsContentPackagesRepository packages, ILmsRuntimeRegistrationsRepository registrations, ILmsRuntimeSessionsRepository sessions,
			ILmsCmi5StatementsRepository statements, ILmsCmi5StateDocumentsRepository stateDocuments, ILmsLearnerService learner, ILmsXapiStatementQueue xapi,
			IUserProfileService profiles, ILmsMediaStore store)
		{
			_store = store;
			_packages = packages;
			_registrations = registrations;
			_sessions = sessions;
			_statements = statements;
			_stateDocuments = stateDocuments;
			_learner = learner;
			_xapi = xapi;
			_profiles = profiles;
		}

		/// <summary>The clock (a seam for tests).</summary>
		public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

		/// <summary>Forgets the cached lookups (tests).</summary>
		public static void ClearCache()
		{
			ContentCache.Clear();
			RunChecks.Clear();
		}

		/// <summary>The kinds of session a content token, the SCORM adapter and the minimal LRS accept (never an LTI hint or an app launch row).</summary>
		internal static bool IsContentKind(int kind) =>
			kind == (int)LmsRuntimeKinds.Scorm12 || kind == (int)LmsRuntimeKinds.Scorm2004 || kind == (int)LmsRuntimeKinds.Cmi5;

		#region Launch

		public Task<LmsContentLaunch> LaunchAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, string itemIdentifier, string siteBase,
			CancellationToken cancellationToken = default) =>
			LaunchCoreAsync(departmentId, userId, lmsEnrollmentId, lmsLessonId, itemIdentifier, siteBase, null, cancellationToken);

		public Task<LmsContentLaunch> LaunchForAppAsync(LmsRuntimeSession appSession, string itemIdentifier, string siteBase, CancellationToken cancellationToken = default)
		{
			// Only a live app session, and only its own member, enrollment and lesson: nothing the page sends can widen that.
			if (appSession == null || appSession.Kind != (int)LmsRuntimeKinds.AppSession || appSession.ExpiresOn <= Clock() || string.IsNullOrWhiteSpace(appSession.UserId) ||
				appSession.LmsRuntimeSessionId <= 0)
				return Task.FromResult(new LmsContentLaunch { Error = LmsErrors.CodeExpired });
			return LaunchCoreAsync(appSession.DepartmentId, appSession.UserId, appSession.LmsEnrollmentId, appSession.LmsLessonId, itemIdentifier, siteBase, appSession,
				cancellationToken);
		}

		private async Task<LmsContentLaunch> LaunchCoreAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, string itemIdentifier, string siteBase,
			LmsRuntimeSession appSession, CancellationToken cancellationToken)
		{
			// The learner service decides whether the lesson may be opened now (gate, lapse rules, lesson order): fail closed on anything else.
			var player = await _learner.GetPlayerAsync(departmentId, userId, lmsEnrollmentId, lmsLessonId);
			if (player == null || player.Error != LmsErrors.None)
				return new LmsContentLaunch { Error = player?.Error ?? LmsErrors.NotFound };
			var lesson = player.Lesson;
			if (lesson == null || lesson.LmsLessonId != lmsLessonId)
				return new LmsContentLaunch { Error = LmsErrors.LessonLocked };
			if (lesson.Kind != (int)LmsLessonKinds.Scorm && lesson.Kind != (int)LmsLessonKinds.Cmi5)
				return new LmsContentLaunch { Error = LmsErrors.NotFound };

			var package = lesson.LmsContentPackageId.HasValue ? await _packages.GetByIdForDepartmentAsync(departmentId, lesson.LmsContentPackageId.Value) : null;
			if (package == null)
				return new LmsContentLaunch { Error = LmsErrors.NotFound };
			if (package.Status != (int)LmsContentPackageStatuses.Ready)
				return new LmsContentLaunch { Error = LmsErrors.MediaUnavailable, Package = package };

			var manifest = LmsPackageManifest.Parse(package.ManifestJson);
			if (manifest.Items.Count == 0)
				return new LmsContentLaunch { Error = LmsErrors.MediaUnavailable, Package = package };

			var now = Clock();
			var kind = KindFor(package);
			var registration = await _registrations.GetAsync(departmentId, player.Enrollment.LmsEnrollmentId, lesson.LmsLessonId);
			if (registration == null)
				(registration, _) = await _registrations.InsertOrGetAsync(new LmsRuntimeRegistration
				{
					DepartmentId = departmentId,
					LmsEnrollmentId = player.Enrollment.LmsEnrollmentId,
					LmsLessonId = lesson.LmsLessonId,
					UserId = player.Enrollment.UserId,
					Kind = (int)kind,
					LmsContentPackageId = package.LmsContentPackageId,
					RegistrationKey = kind == LmsRuntimeKinds.Cmi5 ? Guid.NewGuid().ToString() : null,
					CompletionStatus = "not attempted",
					SuccessStatus = "unknown",
					CreatedOn = now
				}, cancellationToken);

			var satisfiedItems = SatisfiedItems(registration, manifest);
			var item = manifest.Items.FirstOrDefault(i => itemIdentifier != null && string.Equals(i.Identifier, itemIdentifier, StringComparison.Ordinal))
				?? manifest.Items.FirstOrDefault(i => !satisfiedItems.Contains(i.Identifier))
				?? manifest.Items.First();

			var token = LmsLearnerIdentity.NewToken();
			var expires = now.AddHours(Math.Max(1, Config.LmsStandardsConfig.LaunchTokenHours));
			var session = new LmsRuntimeSession
			{
				DepartmentId = departmentId,
				LmsRuntimeRegistrationId = registration.LmsRuntimeRegistrationId,
				LmsEnrollmentId = registration.LmsEnrollmentId,
				LmsLessonId = registration.LmsLessonId,
				UserId = registration.UserId,
				Kind = registration.Kind,
				ItemIdentifier = item.Identifier,
				TokenHash = LmsLearnerIdentity.HashToken(token),
				CreatedOn = now,
				// Round 10 A8: a launch from the app's page outlives neither that page's session nor the launch hours.
				ExpiresOn = appSession != null && appSession.ExpiresOn < expires ? appSession.ExpiresOn : expires,
				AppSessionId = appSession?.LmsRuntimeSessionId
			};

			string fetchToken = null;
			if (kind == LmsRuntimeKinds.Cmi5)
			{
				fetchToken = LmsLearnerIdentity.NewToken();
				session.FetchTokenHash = LmsLearnerIdentity.HashToken(fetchToken);
				session.SessionKey = Guid.NewGuid().ToString();
			}
			await _sessions.InsertAsync(session, cancellationToken, true);
			await BumpLaunchCountAsync(registration, cancellationToken);

			var url = ContentUrl(token, item.Href);
			if (kind == LmsRuntimeKinds.Cmi5)
			{
				var actor = LmsLearnerIdentity.AccountActor(departmentId, registration.UserId);
				var root = (siteBase ?? string.Empty).TrimEnd('/');
				url += (url.Contains('?') ? "&" : "?") +
					"endpoint=" + Uri.EscapeDataString(root + "/lms-content/xapi/") +
					"&fetch=" + Uri.EscapeDataString(root + "/lms-content/cmi5/fetch/" + fetchToken) +
					"&actor=" + Uri.EscapeDataString(actor.ToString(Formatting.None)) +
					"&registration=" + Uri.EscapeDataString(registration.RegistrationKey) +
					"&activityId=" + Uri.EscapeDataString(item.Identifier);
				await RecordLaunchedAsync(departmentId, registration, session, item, manifest, actor, now, cancellationToken);
				// moveOn NotApplicable: the AU is satisfied by its launch (cmi5 §9.6.2.5).
				registration = await ApplyCmi5Async(registration, manifest, item, null, null, session, now, cancellationToken) ?? registration;
				satisfiedItems = SatisfiedItems(registration, manifest);
			}

			return new LmsContentLaunch
			{
				Url = url,
				Package = package,
				Registration = registration,
				Item = item,
				Items = manifest.Items,
				SatisfiedItems = satisfiedItems,
				ExpiresOn = session.ExpiresOn
			};
		}

		internal static LmsRuntimeKinds KindFor(LmsContentPackage package) =>
			package.Standard == (int)LmsContentStandards.Cmi5 ? LmsRuntimeKinds.Cmi5 : package.Standard == (int)LmsContentStandards.Scorm2004 ? LmsRuntimeKinds.Scorm2004 : LmsRuntimeKinds.Scorm12;

		/// <summary>/lms-content/{token}/{href}: every path segment escaped, the manifest's own query kept.</summary>
		internal static string ContentUrl(string token, string href)
		{
			var path = href ?? string.Empty;
			var query = string.Empty;
			var cut = path.IndexOfAny(new[] { '?', '#' });
			if (cut >= 0)
			{
				query = path.Substring(cut);
				path = path.Substring(0, cut);
			}
			var safe = LmsPackageInspector.LaunchPath(path) ?? string.Empty;
			return "/lms-content/" + token + "/" + string.Join("/", safe.Split('/').Select(Uri.EscapeDataString)) + query;
		}

		private static HashSet<string> SatisfiedItems(LmsRuntimeRegistration registration, LmsPackageManifest manifest)
		{
			var result = new HashSet<string>(StringComparer.Ordinal);
			if (registration == null)
				return result;
			if (registration.Kind == (int)LmsRuntimeKinds.Cmi5)
			{
				foreach (var au in LmsCmi5State.Parse(registration.StateJson).Aus.Where(a => a.Value.Satisfied))
					result.Add(au.Key);
			}
			else
			{
				foreach (var sco in LmsScormState.Parse(registration.StateJson).Scos.Where(s => s.Value.Satisfied))
					result.Add(sco.Key);
			}
			return result;
		}

		private async Task BumpLaunchCountAsync(LmsRuntimeRegistration registration, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < 3 && registration != null; attempt++)
			{
				var expected = registration.RowVersion;
				registration.LaunchCount++;
				registration.UpdatedOn = Clock();
				if (await _registrations.TryUpdateAsync(registration, expected, cancellationToken))
					return;
				var fresh = await _registrations.GetByIdForDepartmentAsync(registration.DepartmentId, registration.LmsRuntimeRegistrationId);
				if (fresh == null)
					return;
				CopyInto(fresh, registration);
			}
		}

		private static void CopyInto(LmsRuntimeRegistration source, LmsRuntimeRegistration target)
		{
			target.StateJson = source.StateJson;
			target.CompletionStatus = source.CompletionStatus;
			target.SuccessStatus = source.SuccessStatus;
			target.ScorePercent = source.ScorePercent;
			target.TotalSeconds = source.TotalSeconds;
			target.LaunchCount = source.LaunchCount;
			target.Satisfied = source.Satisfied;
			target.SatisfiedOn = source.SatisfiedOn;
			target.LastResultOn = source.LastResultOn;
			target.UpdatedOn = source.UpdatedOn;
			target.RowVersion = source.RowVersion;
		}

		public Task<LmsRuntimeRegistration> GetRegistrationAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId) => _registrations.GetAsync(departmentId, lmsEnrollmentId, lmsLessonId);

		#endregion Launch

		#region Content

		/// <summary>
		/// A live SCORM or cmi5 session for a content token (not expired). Round 10 A8: the kind is checked against the content kinds, so an LTI
		/// hint, an in-app launch link or an app page session (rows of the same table) is never taken for a content token.
		/// </summary>
		private async Task<LmsRuntimeSession> SessionForTokenAsync(string token)
		{
			if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
				return null;
			var session = await _sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken(token));
			if (session == null || session.ExpiresOn <= Clock() || !IsContentKind(session.Kind))
				return null;
			return session;
		}

		/// <summary>
		/// Round 10 A8, extended to every session (owner decision 2026-11-26: close the seat gap everywhere): the runtime calls (the SCORM
		/// config and commits, the cmi5 fetch URL, statements and State API) of a web or app-launched session re-apply the player's rules like
		/// the package files do (<see cref="MayStillRunAsync"/>): the gate, the lapse window, the learner's LMS seat and lesson order, as the
		/// session's learner, and are refused outright when they fail. A positive answer is kept for <see cref="ContentCacheFor"/> per session
		/// and server; a refusal never.
		/// </summary>
		private async Task<bool> StillRunsAsync(LmsRuntimeSession session) => await RunRefusalAsync(session) == LmsErrors.None;

		/// <summary>Why the session's learner may no longer run the lesson (<see cref="LmsErrors.None"/> when they may), cached like
		/// <see cref="StillRunsAsync"/>; the refusal names the player's own reason (NotLicensed, AddonRequired, LapseWindowEnded, ...).</summary>
		private async Task<LmsErrors> RunRefusalAsync(LmsRuntimeSession session)
		{
			if (session == null)
				return LmsErrors.NotAuthorized;
			var now = Clock();
			if (RunChecks.TryGetValue(session.LmsRuntimeSessionId, out var until) && until > now)
				return LmsErrors.None;
			var refusal = await RunCheckAsync(session);
			if (refusal != LmsErrors.None)
			{
				RunChecks.TryRemove(session.LmsRuntimeSessionId, out _);
				return refusal;
			}
			if (RunChecks.Count > 5000)
				RunChecks.Clear();
			RunChecks[session.LmsRuntimeSessionId] = now.Add(ContentCacheFor);
			return LmsErrors.None;
		}

		public async Task<LmsContentSession> ResolveContentAsync(string token)
		{
			if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
				return null;
			var hash = LmsLearnerIdentity.HashToken(token);
			var now = Clock();
			if (ContentCache.TryGetValue(hash, out var cached) && cached.Until > now && cached.Session.Session.ExpiresOn > now)
				return cached.Session;

			var session = await SessionForTokenAsync(token);
			if (session == null)
				return null;
			var registration = await _registrations.GetByIdForDepartmentAsync(session.DepartmentId, session.LmsRuntimeRegistrationId);
			var package = registration?.LmsContentPackageId == null ? null : await _packages.GetByIdForDepartmentAsync(session.DepartmentId, registration.LmsContentPackageId.Value);
			if (package == null || package.Status != (int)LmsContentPackageStatuses.Ready)
				return null;
			if (!await MayStillRunAsync(session))
				return null;

			var resolved = new LmsContentSession { Session = session, Package = package };
			if (ContentCache.Count > 5000)
				ContentCache.Clear();
			ContentCache[hash] = (resolved, now.Add(ContentCacheFor));
			return resolved;
		}

		/// <summary>
		/// Round 10 A7: a launch token outlives neither the learner's right to run the lesson nor its session. On every cache miss (at most once
		/// per <see cref="ContentCacheFor"/> per session and server) the learner service re-applies the player's rules (the gate, the lapse window,
		/// the learner's LMS seat, lesson order), so a removed seat or an ended lapse window stops the package's files within that minute.
		/// Cheaper than revoking sessions on every seat or billing change, and it covers the lapse window ending by the clock. Fails closed.
		/// </summary>
		private async Task<bool> MayStillRunAsync(LmsRuntimeSession session) => await RunCheckAsync(session) == LmsErrors.None;

		private async Task<LmsErrors> RunCheckAsync(LmsRuntimeSession session)
		{
			try
			{
				var player = await _learner.GetPlayerAsync(session.DepartmentId, session.UserId, session.LmsEnrollmentId, session.LmsLessonId);
				if (player == null)
					return LmsErrors.NotAuthorized;
				if (player.Error != LmsErrors.None)
					return player.Error;
				return player.Lesson?.LmsLessonId == session.LmsLessonId ? LmsErrors.None : LmsErrors.NotAuthorized;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The learner's right to run LMS runtime session {session.LmsRuntimeSessionId} could not be checked.");
				return LmsErrors.NotAuthorized;
			}
		}

		public async Task<Stream> OpenContentAsync(LmsContentSession session, string path, CancellationToken cancellationToken = default)
		{
			var safe = LmsPackageInspector.SafePath(path);
			if (session?.Package == null || safe == null || _store?.IsConfigured != true || string.IsNullOrWhiteSpace(session.Package.ContentPrefix))
				return null;
			try
			{
				return await _store.OpenReadAsync(session.Package.ContentPrefix + safe, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception)
			{
				// Not in the package (the store answers 404) or the store is down: the frame gets a 404 either way.
				return null;
			}
		}

		#endregion Content

		#region SCORM

		public async Task<LmsScormLaunchConfig> GetScormConfigAsync(string token, string commitUrl)
		{
			var session = await SessionForTokenAsync(token);
			if (session == null || session.Kind == (int)LmsRuntimeKinds.Cmi5 || !await StillRunsAsync(session))
				return null;
			var registration = await _registrations.GetByIdForDepartmentAsync(session.DepartmentId, session.LmsRuntimeRegistrationId);
			var package = registration?.LmsContentPackageId == null ? null : await _packages.GetByIdForDepartmentAsync(session.DepartmentId, registration.LmsContentPackageId.Value);
			if (package == null)
				return null;

			var item = LmsPackageManifest.Parse(package.ManifestJson).Items.FirstOrDefault(i => i.Identifier == session.ItemIdentifier);
			var state = LmsScormState.Parse(registration.StateJson);
			state.Scos.TryGetValue(session.ItemIdentifier ?? string.Empty, out var sco);
			var profile = await _profiles.GetProfileByUserIdAsync(session.UserId);
			var name = profile == null ? string.Empty : ((profile.LastName ?? string.Empty).Trim() + ", " + (profile.FirstName ?? string.Empty).Trim()).Trim(',', ' ');
			return new LmsScormLaunchConfig
			{
				Version = session.Kind == (int)LmsRuntimeKinds.Scorm2004 ? "2004" : "1.2",
				CommitUrl = commitUrl,
				Values = LmsScormRuntime.LaunchValues(session.Kind, sco, item, LmsLearnerIdentity.Subject(session.DepartmentId, session.UserId), name,
					firstLaunch: sco == null || sco.Values.Count == 0)
			};
		}

		public async Task<LmsRuntimeOutcome> CommitScormAsync(string token, LmsScormCommit commit, CancellationToken cancellationToken = default)
		{
			var session = await SessionForTokenAsync(token);
			if (session == null || session.Kind == (int)LmsRuntimeKinds.Cmi5)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			if (session.EndedOn.HasValue)
				return LmsRuntimeOutcome.Refuse(409, LmsErrors.AlreadySubmitted);
			if (commit == null || (commit.Values?.Count ?? 0) > LmsScormRuntime.MaxValuesPerSco)
				return LmsRuntimeOutcome.Refuse(400);
			var runRefusal = await RunRefusalAsync(session);
			if (runRefusal != LmsErrors.None)
				return LmsRuntimeOutcome.Refuse(403, runRefusal);

			var registration = await _registrations.GetByIdForDepartmentAsync(session.DepartmentId, session.LmsRuntimeRegistrationId);
			var package = registration?.LmsContentPackageId == null ? null : await _packages.GetByIdForDepartmentAsync(session.DepartmentId, registration.LmsContentPackageId.Value);
			if (package == null)
				return LmsRuntimeOutcome.Refuse(404, LmsErrors.NotFound);
			var manifest = LmsPackageManifest.Parse(package.ManifestJson);
			var item = manifest.Items.FirstOrDefault(i => i.Identifier == session.ItemIdentifier);

			var now = Clock();
			var wasSatisfied = registration.Satisfied;
			List<string> refused = null;
			var saved = false;
			for (var attempt = 0; attempt < 3 && registration != null; attempt++)
			{
				var expected = registration.RowVersion;
				var state = LmsScormState.Parse(registration.StateJson);
				if (!state.Scos.TryGetValue(session.ItemIdentifier ?? string.Empty, out var sco))
					state.Scos[session.ItemIdentifier ?? string.Empty] = sco = new LmsScoState();
				try
				{
					refused = LmsScormRuntime.Apply(session.Kind, sco, commit.Values, commit.Finish, item,
						(int)Math.Max(0, Math.Min(int.MaxValue, (now - session.CreatedOn).TotalSeconds)));
				}
				catch (InvalidOperationException)
				{
					return LmsRuntimeOutcome.Refuse(413);
				}
				Summarize(registration, state, manifest, now);
				registration.StateJson = state.ToJson();
				registration.UpdatedOn = now;
				if (await _registrations.TryUpdateAsync(registration, expected, cancellationToken))
				{
					saved = true;
					break;
				}
				registration = await _registrations.GetByIdForDepartmentAsync(session.DepartmentId, session.LmsRuntimeRegistrationId);
			}
			if (!saved)
				return LmsRuntimeOutcome.Refuse(409);

			if (commit.Finish)
			{
				await _sessions.MarkEndedAsync(session.LmsRuntimeSessionId, now, cancellationToken);
				ContentCache.TryRemove(LmsLearnerIdentity.HashToken(token), out _);
			}

			var outcome = new LmsRuntimeOutcome { Registration = registration, Value = refused ?? new List<string>() };
			if ((registration.Satisfied && !wasSatisfied) || commit.Finish)
				await ReportAsync(registration, session, package, wasSatisfied, outcome, cancellationToken);
			return outcome;
		}

		/// <summary>The registration's summary from its SCOs: completed when all are, failed when any failed, the mean score, the total time.</summary>
		internal static void Summarize(LmsRuntimeRegistration registration, LmsScormState state, LmsPackageManifest manifest, DateTime now)
		{
			var scos = manifest.Items.Select(i => state.Scos.TryGetValue(i.Identifier ?? string.Empty, out var s) ? s : null).ToList();
			var known = scos.Where(s => s != null).ToList();
			registration.TotalSeconds = state.Scos.Values.Sum(s => s.TotalSeconds);
			registration.CompletionStatus = scos.All(s => s != null && s.Completion == "completed") ? "completed" : known.Count == 0 ? "not attempted" : "incomplete";
			registration.SuccessStatus = known.Any(s => s.Success == "failed") ? "failed" : scos.All(s => s != null && s.Success == "passed") ? "passed" : "unknown";
			var scores = known.Where(s => s.ScorePercent.HasValue).Select(s => s.ScorePercent.Value).ToList();
			registration.ScorePercent = scores.Count == 0 ? (decimal?)null : Math.Round(scores.Average(), 2);
			var satisfied = LmsScormRuntime.AllSatisfied(manifest, state);
			if (satisfied && !registration.Satisfied)
				registration.SatisfiedOn = now;
			registration.Satisfied = satisfied;
		}

		#endregion SCORM

		#region cmi5

		private async Task<(LmsRuntimeSession Session, LmsRuntimeRegistration Registration, LmsContentPackage Package, LmsPackageManifest Manifest, LmsPackageItem Item)> Cmi5ContextAsync(string authToken)
		{
			if (string.IsNullOrWhiteSpace(authToken) || authToken.Length > 100)
				return default;
			var session = await _sessions.GetByAuthTokenHashAsync(LmsLearnerIdentity.HashToken(authToken));
			if (session == null || session.Kind != (int)LmsRuntimeKinds.Cmi5 || session.ExpiresOn <= Clock() || !await StillRunsAsync(session))
				return default;
			var registration = await _registrations.GetByIdForDepartmentAsync(session.DepartmentId, session.LmsRuntimeRegistrationId);
			var package = registration?.LmsContentPackageId == null ? null : await _packages.GetByIdForDepartmentAsync(session.DepartmentId, registration.LmsContentPackageId.Value);
			if (package == null)
				return default;
			var manifest = LmsPackageManifest.Parse(package.ManifestJson);
			var item = manifest.Items.FirstOrDefault(i => i.Identifier == session.ItemIdentifier);
			return item == null ? default : (session, registration, package, manifest, item);
		}

		public async Task<LmsRuntimeOutcome> FetchCmi5TokenAsync(string fetchToken, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(fetchToken) || fetchToken.Length > 100)
				return new LmsRuntimeOutcome { Status = 401, Error = LmsErrors.NotAuthorized, Value = Cmi5Error("2", "Unknown or expired fetch URL.") };
			var session = await _sessions.GetByFetchTokenHashAsync(LmsLearnerIdentity.HashToken(fetchToken));
			if (session == null || session.Kind != (int)LmsRuntimeKinds.Cmi5 || session.ExpiresOn <= Clock())
				return new LmsRuntimeOutcome { Status = 401, Error = LmsErrors.NotAuthorized, Value = Cmi5Error("2", "Unknown or expired fetch URL.") };
			if (!await StillRunsAsync(session))
				return new LmsRuntimeOutcome { Status = 403, Error = LmsErrors.NotAuthorized, Value = Cmi5Error("3", "This lesson can no longer run.") };

			var authToken = LmsLearnerIdentity.NewToken();
			// cmi5 §8.2.3: the fetch URL hands out its token once; a second call is an error.
			if (!await _sessions.TryMarkFetchedAsync(session.LmsRuntimeSessionId, LmsLearnerIdentity.HashToken(authToken), Clock(), cancellationToken))
				return new LmsRuntimeOutcome { Status = 400, Error = LmsErrors.AlreadySubmitted, Value = Cmi5Error("1", "The fetch URL was already used.") };
			return new LmsRuntimeOutcome { Value = new JObject { ["auth-token"] = authToken } };
		}

		private static JObject Cmi5Error(string code, string text) => new JObject { ["error-code"] = code, ["error-text"] = text };

		public async Task<LmsRuntimeOutcome> SaveCmi5StatementsAsync(string authToken, string body, string statementId, CancellationToken cancellationToken = default)
		{
			var (session, registration, package, manifest, item) = await Cmi5ContextAsync(authToken);
			if (session == null)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			if (session.EndedOn.HasValue)
				return LmsRuntimeOutcome.Refuse(403, LmsErrors.AlreadySubmitted);
			if (string.IsNullOrWhiteSpace(body) || body.Length > MaxStatementBodyChars)
				return LmsRuntimeOutcome.Refuse(400);

			var parsed = LmsStandardsJson.Parse(body);
			var list = parsed is JArray array ? array.OfType<JObject>().ToList() : parsed is JObject single ? new List<JObject> { single } : null;
			if (list == null || list.Count == 0 || list.Count > MaxStatementsPerRequest || (parsed is JArray a && a.Count != list.Count))
				return LmsRuntimeOutcome.Refuse(400);
			if (statementId != null)
			{
				if (list.Count != 1 || !Guid.TryParse(statementId, out _))
					return LmsRuntimeOutcome.Refuse(400);
				var own = (string)list[0]["id"];
				if (own != null && !string.Equals(own, statementId, StringComparison.OrdinalIgnoreCase))
					return LmsRuntimeOutcome.Refuse(400);
				list[0]["id"] = statementId;
			}

			var now = Clock();
			var homePage = LmsLearnerIdentity.HomePage(session.DepartmentId);
			var accountName = LmsLearnerIdentity.Subject(session.DepartmentId, session.UserId);
			var checks = new List<LmsCmi5Rules.StatementCheck>();
			foreach (var statement in list)
			{
				var check = LmsCmi5Rules.Check(statement, homePage, accountName, registration.RegistrationKey, item.Identifier, session.SessionKey, homePage, now);
				if (check.Status != 0)
					return LmsRuntimeOutcome.Refuse(check.Status, check.Status == 403 ? LmsErrors.NotAuthorized : LmsErrors.InvalidInput);
				if (check.Statement.ToString(Formatting.None).Length > LmsCmi5Statement.MaxStatementBytes)
					return LmsRuntimeOutcome.Refuse(413);
				checks.Add(check);
			}
			if (await _statements.CountForRegistrationAsync(session.DepartmentId, registration.LmsRuntimeRegistrationId) + checks.Count > Config.LmsStandardsConfig.MaxCmi5StatementsPerRegistration)
				return LmsRuntimeOutcome.Refuse(413);

			var ids = new List<string>();
			var defined = new List<LmsCmi5Rules.StatementCheck>();
			foreach (var check in checks)
			{
				var stored = await _statements.TryInsertAsync(new LmsCmi5Statement
				{
					DepartmentId = session.DepartmentId,
					LmsRuntimeRegistrationId = registration.LmsRuntimeRegistrationId,
					LmsRuntimeSessionId = session.LmsRuntimeSessionId,
					StatementId = check.StatementId,
					VerbId = LmsCourseService.Truncate(check.VerbId, 250),
					StatementJson = check.Statement.ToString(Formatting.None),
					StoredOn = now
				}, cancellationToken);
				ids.Add(check.StatementId);
				// A re-sent statement (same id) is accepted again without counting twice.
				if (stored && check.IsCmi5Defined)
					defined.Add(check);
			}

			var outcome = new LmsRuntimeOutcome { Value = ids, Registration = registration };
			if (defined.Count == 0)
				return outcome;

			var wasSatisfied = registration.Satisfied;
			var terminated = defined.Any(d => d.VerbId == LmsCmi5Rules.Terminated);
			registration = await ApplyCmi5Async(registration, manifest, item, defined, session, session, now, cancellationToken) ?? registration;
			outcome.Registration = registration;
			if (terminated)
			{
				await _sessions.MarkEndedAsync(session.LmsRuntimeSessionId, now, cancellationToken);
				ContentCache.TryRemove(session.TokenHash ?? string.Empty, out _);
			}

			foreach (var result in defined.Where(d => d.VerbId == LmsCmi5Rules.Passed || d.VerbId == LmsCmi5Rules.Failed))
				await QueueLessonXapiAsync(registration, package, result.VerbId == LmsCmi5Rules.Passed ? LmsXapiVerbs.Passed : LmsXapiVerbs.Failed,
					result.VerbId == LmsCmi5Rules.Passed, registration.ScorePercent, cancellationToken);
			if ((registration.Satisfied && !wasSatisfied) || terminated)
				await ReportAsync(registration, session, package, wasSatisfied, outcome, cancellationToken);
			return outcome;
		}

		/// <summary>
		/// Applies cmi5-defined statements (or, with none, the launch itself) to the AU's results and the registration's satisfaction, with an
		/// optimistic retry. A terminated session adds its wall-clock time once. A newly satisfied AU gets the LMS's "satisfied" statement.
		/// </summary>
		private async Task<LmsRuntimeRegistration> ApplyCmi5Async(LmsRuntimeRegistration registration, LmsPackageManifest manifest, LmsPackageItem item,
			List<LmsCmi5Rules.StatementCheck> defined, LmsRuntimeSession endedSession, LmsRuntimeSession session, DateTime now, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < 3 && registration != null; attempt++)
			{
				var expected = registration.RowVersion;
				var state = LmsCmi5State.Parse(registration.StateJson);
				if (!state.Aus.TryGetValue(item.Identifier, out var au))
					state.Aus[item.Identifier] = au = new LmsCmi5AuState();
				var auWasSatisfied = au.Satisfied;
				foreach (var check in defined ?? new List<LmsCmi5Rules.StatementCheck>())
				{
					LmsCmi5Rules.Apply(au, check.VerbId, check.Statement);
					if (check.VerbId == LmsCmi5Rules.Terminated && endedSession != null)
						registration.TotalSeconds += (int)Math.Max(0, Math.Min(int.MaxValue / 2, (now - endedSession.CreatedOn).TotalSeconds));
				}
				au.Satisfied = au.Satisfied || LmsCmi5Rules.IsSatisfied(item.MoveOn, au);

				var aus = manifest.Items.Select(i => state.Aus.TryGetValue(i.Identifier, out var s) ? s : null).ToList();
				registration.CompletionStatus = aus.All(s => s != null && s.Completed) ? "completed" : aus.Any(s => s != null) ? "incomplete" : "not attempted";
				registration.SuccessStatus = aus.Any(s => s != null && s.Failed && !s.Passed) ? "failed" : aus.All(s => s != null && s.Passed) ? "passed" : "unknown";
				var scores = aus.Where(s => s?.ScoreScaled != null).Select(s => s.ScoreScaled.Value * 100m).ToList();
				registration.ScorePercent = scores.Count == 0 ? (decimal?)null : Math.Round(Math.Max(0m, Math.Min(100m, scores.Average())), 2);
				var satisfied = aus.All(s => s != null && s.Satisfied);
				if (satisfied && !registration.Satisfied)
					registration.SatisfiedOn = now;
				registration.Satisfied = satisfied;
				registration.StateJson = state.ToJson();
				registration.UpdatedOn = now;

				if (await _registrations.TryUpdateAsync(registration, expected, cancellationToken))
				{
					if (au.Satisfied && !auWasSatisfied)
						await RecordLmsStatementAsync(registration, session, item.Identifier, LmsCmi5Rules.Satisfied, "satisfied", now, cancellationToken);
					return registration;
				}
				var fresh = await _registrations.GetByIdForDepartmentAsync(registration.DepartmentId, registration.LmsRuntimeRegistrationId);
				if (fresh == null)
					return null;
				registration = fresh;
			}
			return registration;
		}

		private async Task RecordLaunchedAsync(int departmentId, LmsRuntimeRegistration registration, LmsRuntimeSession session, LmsPackageItem item, LmsPackageManifest manifest,
			JObject actor, DateTime now, CancellationToken cancellationToken)
		{
			var statement = LmsCmi5Rules.LmsStatement(LmsCmi5Rules.Launched, "launched", actor, item.Identifier, registration.RegistrationKey, session.SessionKey,
				LmsLearnerIdentity.HomePage(departmentId), now);
			statement["context"]["extensions"]["https://w3id.org/xapi/cmi5/context/extensions/launchmode"] = "Normal";
			statement["context"]["extensions"]["https://w3id.org/xapi/cmi5/context/extensions/moveon"] = item.MoveOn ?? "NotApplicable";
			await StoreLmsStatementAsync(registration, session, statement, LmsCmi5Rules.Launched, cancellationToken);
		}

		private Task RecordLmsStatementAsync(LmsRuntimeRegistration registration, LmsRuntimeSession session, string objectId, string verbId, string display, DateTime now,
			CancellationToken cancellationToken)
		{
			var statement = LmsCmi5Rules.LmsStatement(verbId, display, LmsLearnerIdentity.AccountActor(registration.DepartmentId, registration.UserId), objectId,
				registration.RegistrationKey, session?.SessionKey, LmsLearnerIdentity.HomePage(registration.DepartmentId), now);
			((JObject)statement["context"]["contextActivities"])["category"] = new JArray(new JObject { ["id"] = LmsCmi5Rules.CategoryCmi5 }, new JObject { ["id"] = LmsCmi5Rules.CategoryMoveOn });
			return StoreLmsStatementAsync(registration, session, statement, verbId, cancellationToken);
		}

		private async Task StoreLmsStatementAsync(LmsRuntimeRegistration registration, LmsRuntimeSession session, JObject statement, string verbId, CancellationToken cancellationToken)
		{
			try
			{
				await _statements.TryInsertAsync(new LmsCmi5Statement
				{
					DepartmentId = registration.DepartmentId,
					LmsRuntimeRegistrationId = registration.LmsRuntimeRegistrationId,
					LmsRuntimeSessionId = session?.LmsRuntimeSessionId ?? 0,
					StatementId = (string)statement["id"],
					VerbId = verbId,
					StatementJson = statement.ToString(Formatting.None),
					StoredOn = Clock()
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A cmi5 LMS statement could not be recorded.");
			}
		}

		public async Task<LmsRuntimeOutcome> GetCmi5StatementAsync(string authToken, string statementId)
		{
			var (session, registration, _, _, _) = await Cmi5ContextAsync(authToken);
			if (session == null)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			if (string.IsNullOrWhiteSpace(statementId) || !Guid.TryParse(statementId, out _))
				return LmsRuntimeOutcome.Refuse(400);
			var statement = await _statements.GetByStatementIdAsync(session.DepartmentId, registration.LmsRuntimeRegistrationId, statementId.Trim().ToLowerInvariant());
			return statement == null ? LmsRuntimeOutcome.Refuse(404, LmsErrors.NotFound) : new LmsRuntimeOutcome { Value = statement.StatementJson };
		}

		/// <summary>The State API checks: the AU's activity, the launch's registration (when given) and the launch's agent.</summary>
		private static bool StateRequestMatches(LmsRuntimeSession session, LmsRuntimeRegistration registration, LmsPackageItem item, string activityId, string registrationKey, string agent)
		{
			if (!string.Equals(activityId, item.Identifier, StringComparison.Ordinal))
				return false;
			if (!string.IsNullOrWhiteSpace(registrationKey) && !string.Equals(registrationKey.Trim(), registration.RegistrationKey, StringComparison.OrdinalIgnoreCase))
				return false;
			var parsed = LmsStandardsJson.ParseObject(agent);
			var account = parsed?["account"] as JObject;
			return account != null && string.Equals((string)account["homePage"], LmsLearnerIdentity.HomePage(session.DepartmentId), StringComparison.Ordinal) &&
				string.Equals((string)account["name"], LmsLearnerIdentity.Subject(session.DepartmentId, session.UserId), StringComparison.Ordinal);
		}

		public async Task<LmsRuntimeOutcome> GetCmi5StateAsync(string authToken, string activityId, string stateId, string registration, string agent)
		{
			var (session, reg, _, manifest, item) = await Cmi5ContextAsync(authToken);
			if (session == null)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			if (!StateRequestMatches(session, reg, item, activityId, registration, agent) || string.IsNullOrWhiteSpace(stateId))
				return LmsRuntimeOutcome.Refuse(403, LmsErrors.NotAuthorized);
			if (stateId == LmsCmi5Rules.LaunchDataStateId)
				return new LmsRuntimeOutcome { Value = LmsCmi5Rules.LaunchData(item, session.SessionKey, manifest.Identifier).ToString(Formatting.None) };
			var document = await _stateDocuments.GetAsync(session.DepartmentId, reg.LmsRuntimeRegistrationId, activityId, Bound(stateId));
			return document == null ? LmsRuntimeOutcome.Refuse(404, LmsErrors.NotFound) : new LmsRuntimeOutcome { Value = document.Content };
		}

		private static string Bound(string stateId) => stateId == null ? null : stateId.Length > 250 ? stateId.Substring(0, 250) : stateId;

		public async Task<LmsRuntimeOutcome> SaveCmi5StateAsync(string authToken, string activityId, string stateId, string registration, string agent, string contentType, string body, bool merge,
			CancellationToken cancellationToken = default)
		{
			var (session, reg, _, _, item) = await Cmi5ContextAsync(authToken);
			if (session == null)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			if (!StateRequestMatches(session, reg, item, activityId, registration, agent) || string.IsNullOrWhiteSpace(stateId) || stateId.Length > 250)
				return LmsRuntimeOutcome.Refuse(403, LmsErrors.NotAuthorized);
			if (stateId == LmsCmi5Rules.LaunchDataStateId)
				return LmsRuntimeOutcome.Refuse(403, LmsErrors.NotAuthorized);
			if ((body?.Length ?? 0) > LmsCmi5StateDocument.MaxContentBytes)
				return LmsRuntimeOutcome.Refuse(413);

			var existing = await _stateDocuments.GetAsync(session.DepartmentId, reg.LmsRuntimeRegistrationId, activityId, stateId);
			var content = body ?? string.Empty;
			if (merge && existing != null)
			{
				// xAPI State API POST: both documents must be JSON objects; the new keys win.
				var stored = LmsStandardsJson.ParseObject(existing.Content);
				var incoming = LmsStandardsJson.ParseObject(content);
				if (stored == null || incoming == null)
					return LmsRuntimeOutcome.Refuse(400);
				foreach (var property in incoming.Properties())
					stored[property.Name] = property.Value;
				content = stored.ToString(Formatting.None);
				if (content.Length > LmsCmi5StateDocument.MaxContentBytes)
					return LmsRuntimeOutcome.Refuse(413);
			}

			var document = existing ?? new LmsCmi5StateDocument
			{
				DepartmentId = session.DepartmentId,
				LmsRuntimeRegistrationId = reg.LmsRuntimeRegistrationId,
				ActivityId = activityId,
				StateId = stateId
			};
			document.ContentType = LmsCourseService.Truncate(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(), 100);
			document.Content = content;
			document.UpdatedOn = Clock();
			if (existing == null)
				await _stateDocuments.InsertAsync(document, cancellationToken, true);
			else
				await _stateDocuments.UpdateAsync(document, cancellationToken, true);
			return new LmsRuntimeOutcome { Status = 204 };
		}

		public async Task<LmsRuntimeOutcome> DeleteCmi5StateAsync(string authToken, string activityId, string stateId, string registration, string agent, CancellationToken cancellationToken = default)
		{
			var (session, reg, _, _, item) = await Cmi5ContextAsync(authToken);
			if (session == null)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			if (!StateRequestMatches(session, reg, item, activityId, registration, agent) || stateId == LmsCmi5Rules.LaunchDataStateId)
				return LmsRuntimeOutcome.Refuse(403, LmsErrors.NotAuthorized);
			await _stateDocuments.DeleteAsync(session.DepartmentId, reg.LmsRuntimeRegistrationId, activityId, string.IsNullOrWhiteSpace(stateId) ? null : stateId, cancellationToken);
			return new LmsRuntimeOutcome { Status = 204 };
		}

		#endregion cmi5

		#region Reporting

		/// <summary>
		/// The registration's time and satisfaction go to the lesson (the learner service applies the gate, the lapse rules and lesson order;
		/// a refusal there leaves the runtime data stored but the lesson open). A newly satisfied lesson is an xAPI "completed".
		/// </summary>
		private async Task ReportAsync(LmsRuntimeRegistration registration, LmsRuntimeSession session, LmsContentPackage package, bool wasSatisfied, LmsRuntimeOutcome outcome,
			CancellationToken cancellationToken)
		{
			try
			{
				var result = await _learner.RecordRuntimeOutcomeAsync(registration.DepartmentId, registration.UserId, registration.LmsEnrollmentId, registration.LmsLessonId,
					registration.Satisfied, registration.TotalSeconds, cancellationToken);
				outcome.LessonSatisfied = result.Succeeded && registration.Satisfied && !wasSatisfied;
				outcome.CourseCompleted = result.Succeeded && result.Count > 0;
				if (!result.Succeeded)
					outcome.Error = result.Error;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The runtime outcome of LMS registration {registration.LmsRuntimeRegistrationId} could not be recorded.");
			}

			if (outcome.LessonSatisfied)
				await QueueLessonXapiAsync(registration, package, LmsXapiVerbs.Completed, registration.SuccessStatus == "passed" ? true : registration.SuccessStatus == "failed" ? false : (bool?)null,
					registration.ScorePercent, cancellationToken);
			else if (session != null && registration.Kind != (int)LmsRuntimeKinds.Cmi5 && registration.SuccessStatus == "failed" && session.EndedOn == null)
				await QueueLessonXapiAsync(registration, package, LmsXapiVerbs.Failed, false, registration.ScorePercent, cancellationToken);
		}

		private async Task QueueLessonXapiAsync(LmsRuntimeRegistration registration, LmsContentPackage package, string verb, bool? success, decimal? score, CancellationToken cancellationToken)
		{
			try
			{
				await _xapi.EnqueueAsync(new LmsXapiEvent
				{
					DepartmentId = registration.DepartmentId,
					UserId = registration.UserId,
					VerbId = verb,
					LmsLessonId = registration.LmsLessonId,
					LessonTitle = package?.Title,
					Completion = verb == LmsXapiVerbs.Completed ? true : (bool?)null,
					Success = success,
					ScorePercent = score,
					DurationSeconds = registration.TotalSeconds,
					OccurredOn = Clock()
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A runtime xAPI statement could not be queued.");
			}
		}

		public Task<int> PurgeExpiredSessionsAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
			_sessions.DeleteExpiredAsync(utcNow.AddDays(-7), cancellationToken);

		#endregion Reporting
	}
}
