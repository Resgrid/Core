using System;
using System.Collections.Concurrent;
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
	/// Round 10 A8 (owner request 2026-11-26): SCORM 1.2 / 2004, cmi5 and LTI 1.3 lessons in the Omni app's web view. The member's bearer call
	/// gets a launch link: 256 random bits (only the SHA-256 is stored), single use, at most five minutes, bound to the department, the member,
	/// the enrollment and the lesson (and a SCO or AU). The anonymous launch page exchanges it once for a page session of the launch hours that
	/// is bound the same way and can do three things only: relaunch that lesson's items, start that lesson's LTI tool, and read that lesson's
	/// state. It is not a sign-in (no cookie, no claims principal), so no other page or API accepts it. The content and LTI sessions it mints are
	/// the existing runtime's (the SCORM adapter's commits, the cmi5 fetch URL and LRS, the OIDC login and id_token form post), which re-apply the
	/// player's rules on every call. Every step here runs those rules again as the member and fails closed. No personal or protected values are
	/// put into the content: the runtime's pseudonymous learner ids and the learner's name for SCORM, as on the web.
	/// </summary>
	public class LmsAppLaunchService : ILmsAppLaunchService
	{
		private static readonly TimeSpan StateRulesCacheFor = TimeSpan.FromMinutes(1);

		/// <summary>App sessions whose lesson may still run (positive answers only): the kind they run and until when the answer holds.</summary>
		private static readonly ConcurrentDictionary<int, (LmsAppLaunchKinds Kind, DateTime Until)> StateRules = new ConcurrentDictionary<int, (LmsAppLaunchKinds, DateTime)>();

		private readonly ILmsLearnerService _learner;
		private readonly ILmsStandardsRuntimeService _runtime;
		private readonly ILmsLtiService _lti;
		private readonly ILmsRuntimeSessionsRepository _sessions;
		private readonly ILmsRuntimeRegistrationsRepository _registrations;
		private readonly ILmsContentPackagesRepository _packages;
		private readonly ILmsLtiToolsRepository _tools;
		private readonly ILmsMediaStore _store;
		private readonly IEventAggregator _eventAggregator;

		public LmsAppLaunchService(ILmsLearnerService learner, ILmsStandardsRuntimeService runtime, ILmsLtiService lti, ILmsRuntimeSessionsRepository sessions,
			ILmsRuntimeRegistrationsRepository registrations, ILmsContentPackagesRepository packages, ILmsLtiToolsRepository tools, ILmsMediaStore store,
			IEventAggregator eventAggregator)
		{
			_learner = learner;
			_runtime = runtime;
			_lti = lti;
			_sessions = sessions;
			_registrations = registrations;
			_packages = packages;
			_tools = tools;
			_store = store;
			_eventAggregator = eventAggregator;
		}

		/// <summary>The clock (a seam for tests).</summary>
		public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

		/// <summary>Forgets the cached rule answers (tests).</summary>
		public static void ClearCache() => StateRules.Clear();

		#region The lesson check

		private sealed class LessonCheck
		{
			public LmsErrors Error;
			public LmsPlayerView Player;
			public LmsLesson Lesson;
			public LmsAppLaunchKinds Kind;
			public LmsContentPackage Package;
			public LmsLtiTool Tool;

			/// <summary>The kind when the lesson's content was looked at (a refusal before that names none).</summary>
			public LmsAppLaunchKinds? KnownKind => Package != null || Tool != null ? Kind : (LmsAppLaunchKinds?)null;
		}

		/// <summary>
		/// The player's rules for the lesson as the member (module-independent: the callers check the module): the enrollment is theirs, the
		/// course may run (gate, lapse window, LMS seat), the lesson is open (order), it is a SCORM, cmi5 or LTI lesson, and its package is ready
		/// in a configured store or its tool is active.
		/// </summary>
		private async Task<LessonCheck> CheckLessonAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId)
		{
			var check = new LessonCheck { Error = LmsErrors.NotFound };
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || lmsEnrollmentId <= 0 || lmsLessonId <= 0)
				return check;

			var player = await _learner.GetPlayerAsync(departmentId, userId, lmsEnrollmentId, lmsLessonId);
			check.Player = player;
			if (player == null)
				return check;
			var inCourse = player.Outline?.Lessons?.FirstOrDefault(l => l.LmsLessonId == lmsLessonId);
			if (player.Error != LmsErrors.None)
			{
				check.Error = player.Error;
				check.Lesson = inCourse;
				return check;
			}
			if (inCourse == null)
				return check;
			check.Lesson = inCourse;
			if (player.Lesson == null || player.Lesson.LmsLessonId != lmsLessonId)
			{
				check.Error = LmsErrors.LessonLocked;
				return check;
			}

			var lesson = player.Lesson;
			switch ((LmsLessonKinds)lesson.Kind)
			{
				case LmsLessonKinds.Scorm:
				case LmsLessonKinds.Cmi5:
					var package = lesson.LmsContentPackageId.HasValue ? await _packages.GetByIdForDepartmentAsync(departmentId, lesson.LmsContentPackageId.Value) : null;
					var kind = LmsAppLaunchRules.KindFor(lesson, package);
					if (package == null || kind == null || package.Status != (int)LmsContentPackageStatuses.Ready || _store?.IsConfigured != true ||
						LmsPackageManifest.Parse(package.ManifestJson).Items.Count == 0)
					{
						check.Error = LmsErrors.MediaUnavailable;
						return check;
					}
					check.Package = package;
					check.Kind = kind.Value;
					break;
				case LmsLessonKinds.LtiLink:
					var tool = lesson.LmsLtiToolId.HasValue ? await _tools.GetByIdForDepartmentAsync(departmentId, lesson.LmsLtiToolId.Value) : null;
					if (tool == null || tool.IsDeleted || !tool.IsActive || !LmsStandardsUrls.IsAcceptableHttpsUrl(tool.OidcLoginUrl))
					{
						check.Error = LmsErrors.MediaUnavailable;
						return check;
					}
					check.Tool = tool;
					check.Kind = LmsAppLaunchKinds.Lti13;
					break;
				default:
					return check;
			}
			check.Error = LmsErrors.None;
			return check;
		}

		/// <summary>A live page session for its token (the kind checked: a content token, an LTI hint or a launch link is never one).</summary>
		private async Task<LmsRuntimeSession> AppSessionAsync(string sessionToken)
		{
			if (string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length > 100)
				return null;
			var session = await _sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken(sessionToken));
			if (session == null || session.Kind != (int)LmsRuntimeKinds.AppSession || session.ExpiresOn <= Clock() || string.IsNullOrWhiteSpace(session.UserId))
				return null;
			return session;
		}

		#endregion The lesson check

		#region Issue and exchange

		public async Task<LmsAppLaunchIssue> IssueAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, string itemIdentifier,
			CancellationToken cancellationToken = default)
		{
			var check = await CheckLessonAsync(departmentId, userId, lmsEnrollmentId, lmsLessonId);
			var issue = new LmsAppLaunchIssue { Error = check.Error, Kind = check.Kind, EnrollmentId = lmsEnrollmentId, LessonId = lmsLessonId };
			if (check.Error != LmsErrors.None)
				return issue;

			// A SCO or AU is kept only when the package names it (the column holds 500 characters; the launch falls back to the next unsatisfied one).
			string item = null;
			if (check.Package != null && !string.IsNullOrWhiteSpace(itemIdentifier) && itemIdentifier.Length <= 500 &&
				LmsPackageManifest.Parse(check.Package.ManifestJson).Items.Any(i => string.Equals(i.Identifier, itemIdentifier, StringComparison.Ordinal)))
				item = itemIdentifier;

			var now = Clock();
			var registration = await _registrations.GetAsync(departmentId, check.Player.Enrollment.LmsEnrollmentId, lmsLessonId);
			var token = LmsLearnerIdentity.NewToken();
			var expires = now.Add(LmsAppLaunchRules.LaunchLifetime);
			await _sessions.InsertAsync(new LmsRuntimeSession
			{
				DepartmentId = departmentId,
				LmsRuntimeRegistrationId = registration?.LmsRuntimeRegistrationId ?? 0,
				LmsEnrollmentId = check.Player.Enrollment.LmsEnrollmentId,
				LmsLessonId = lmsLessonId,
				UserId = check.Player.Enrollment.UserId,
				Kind = (int)LmsRuntimeKinds.AppLaunch,
				ItemIdentifier = item,
				TokenHash = LmsLearnerIdentity.HashToken(token),
				CreatedOn = now,
				ExpiresOn = expires
			}, cancellationToken, true);

			issue.Token = token;
			issue.ExpiresOn = expires;
			return issue;
		}

		public async Task<LmsAppPage> ExchangeAsync(string launchToken, string siteBase, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var expired = new LmsAppPage { Error = LmsErrors.CodeExpired };
			if (string.IsNullOrWhiteSpace(launchToken) || launchToken.Length > 100)
				return expired;
			var now = Clock();
			var launch = await _sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken(launchToken));
			if (launch == null || launch.Kind != (int)LmsRuntimeKinds.AppLaunch || launch.ExpiresOn <= now || launch.UsedOn.HasValue || string.IsNullOrWhiteSpace(launch.UserId))
				return expired;
			// Single use: the conditional update settles a concurrent twin (a reload, a prefetch); the loser is refused even if the winner fails later.
			if (!await _sessions.TryMarkUsedAsync(launch.LmsRuntimeSessionId, now, cancellationToken))
				return expired;

			// The rules may have changed in the minutes since the link was issued.
			var check = await CheckLessonAsync(launch.DepartmentId, launch.UserId, launch.LmsEnrollmentId, launch.LmsLessonId);
			if (check.Error != LmsErrors.None)
				return Refused(launch, check);

			var sessionToken = LmsLearnerIdentity.NewToken();
			var appSession = await _sessions.InsertAsync(new LmsRuntimeSession
			{
				DepartmentId = launch.DepartmentId,
				LmsRuntimeRegistrationId = launch.LmsRuntimeRegistrationId,
				LmsEnrollmentId = launch.LmsEnrollmentId,
				LmsLessonId = launch.LmsLessonId,
				UserId = launch.UserId,
				Kind = (int)LmsRuntimeKinds.AppSession,
				TokenHash = LmsLearnerIdentity.HashToken(sessionToken),
				CreatedOn = now,
				ExpiresOn = now.Add(LmsAppLaunchRules.SessionLifetime)
			}, cancellationToken, true);

			Audit(launch, appSession, check, ipAddress, userAgent);
			return await PageAsync(appSession, sessionToken, check, launch.ItemIdentifier, siteBase, cancellationToken);
		}

		public async Task<LmsAppPage> OpenItemAsync(string sessionToken, string itemIdentifier, string siteBase, CancellationToken cancellationToken = default)
		{
			var appSession = await AppSessionAsync(sessionToken);
			if (appSession == null)
				return new LmsAppPage { Error = LmsErrors.CodeExpired };
			var check = await CheckLessonAsync(appSession.DepartmentId, appSession.UserId, appSession.LmsEnrollmentId, appSession.LmsLessonId);
			if (check.Error != LmsErrors.None)
				return Refused(appSession, check);
			var item = string.IsNullOrWhiteSpace(itemIdentifier) || itemIdentifier.Length > 500 ? null : itemIdentifier;
			return await PageAsync(appSession, sessionToken, check, item, siteBase, cancellationToken);
		}

		public async Task<LmsLtiLaunchStep> BeginLtiAsync(string sessionToken, CancellationToken cancellationToken = default)
		{
			var appSession = await AppSessionAsync(sessionToken);
			if (appSession == null)
				return new LmsLtiLaunchStep { Error = LmsErrors.CodeExpired };
			var check = await CheckLessonAsync(appSession.DepartmentId, appSession.UserId, appSession.LmsEnrollmentId, appSession.LmsLessonId);
			if (check.Error != LmsErrors.None)
				return new LmsLtiLaunchStep { Error = check.Error };
			if (check.Kind != LmsAppLaunchKinds.Lti13)
				return new LmsLtiLaunchStep { Error = LmsErrors.NotFound };
			// The LTI service runs the player's rules once more and binds the hint to this app session (the return URL).
			return await _lti.BeginAppLaunchAsync(appSession, cancellationToken);
		}

		/// <summary>The page of a lesson that may run: the SCORM / cmi5 content launch (bound to the app session) or the LTI tool, and the state.</summary>
		private async Task<LmsAppPage> PageAsync(LmsRuntimeSession appSession, string sessionToken, LessonCheck check, string itemIdentifier, string siteBase,
			CancellationToken cancellationToken)
		{
			var page = new LmsAppPage
			{
				Kind = check.Kind,
				DepartmentId = appSession.DepartmentId,
				EnrollmentId = appSession.LmsEnrollmentId,
				LessonId = appSession.LmsLessonId,
				LessonTitle = check.Lesson?.Title,
				SessionToken = sessionToken,
				SessionExpiresOn = appSession.ExpiresOn
			};
			var before = await StateOfAsync(appSession, check.Kind);
			if (check.Kind == LmsAppLaunchKinds.Lti13)
			{
				page.ToolName = check.Tool?.Name;
				page.ToolGradePassback = check.Tool?.AllowGradePassback == true;
				page.State = before;
				return page;
			}

			var launch = await _runtime.LaunchForAppAsync(appSession, itemIdentifier, siteBase, cancellationToken);
			if (launch == null || !launch.Succeeded)
			{
				page.Error = launch?.Error ?? LmsErrors.MediaUnavailable;
				page.State = LmsAppLaunchRules.Message(page.EnrollmentId, page.LessonId, check.Kind, launch?.Registration, null, page.Error);
				return page;
			}
			page.Launch = launch;
			page.State = await StateOfAsync(appSession, check.Kind);
			// A launch that recorded a result itself (cmi5 moveOn NotApplicable) is news the page posts at once.
			page.PostInitial = LmsAppLaunchRules.ResultStatuses.Contains(page.State.Status) &&
				(before.Status != page.State.Status || before.Satisfied != page.State.Satisfied);
			return page;
		}

		private static LmsAppPage Refused(LmsRuntimeSession session, LessonCheck check) => new LmsAppPage
		{
			Error = check.Error,
			Kind = check.Kind,
			DepartmentId = session.DepartmentId,
			EnrollmentId = session.LmsEnrollmentId,
			LessonId = session.LmsLessonId,
			LessonTitle = check.Lesson?.Title,
			State = LmsAppLaunchRules.Message(session.LmsEnrollmentId, session.LmsLessonId, check.KnownKind, null, null, check.Error)
		};

		private void Audit(LmsRuntimeSession launch, LmsRuntimeSession appSession, LessonCheck check, string ipAddress, string userAgent)
		{
			try
			{
				_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = launch.DepartmentId,
					UserId = launch.UserId,
					Type = AuditLogTypes.LmsAppLaunchOpened,
					After = JsonConvert.SerializeObject(new
					{
						EnrollmentId = launch.LmsEnrollmentId,
						LessonId = launch.LmsLessonId,
						Kind = check.Kind.ToString(),
						Item = launch.ItemIdentifier,
						LaunchId = launch.LmsRuntimeSessionId,
						AppSessionId = appSession.LmsRuntimeSessionId
					}),
					IpAddress = Truncate(ipAddress, 100),
					UserAgent = Truncate(userAgent, 500),
					Successful = true,
					ServerName = Environment.MachineName
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An in-app LMS launch could not be audited.");
			}
		}

		private static string Truncate(string value, int length) => value == null ? null : value.Length > length ? value.Substring(0, length) : value;

		#endregion Issue and exchange

		#region State

		public async Task<LmsAppLessonMessage> GetStateAsync(string sessionToken)
		{
			var appSession = await AppSessionAsync(sessionToken);
			return appSession == null ? null : await CheckedStateAsync(appSession);
		}

		public async Task<LmsAppLessonMessage> GetReturnStateAsync(int departmentId, int appSessionId)
		{
			if (departmentId <= 0 || appSessionId <= 0)
				return null;
			var appSession = await _sessions.GetByIdAsync(appSessionId);
			if (appSession == null || appSession.DepartmentId != departmentId || appSession.Kind != (int)LmsRuntimeKinds.AppSession || appSession.ExpiresOn <= Clock() ||
				string.IsNullOrWhiteSpace(appSession.UserId))
				return null;
			return await CheckedStateAsync(appSession);
		}

		/// <summary>
		/// The lesson's state after the player's rules (as the member; a positive answer kept a minute per app session and server, a refusal never):
		/// a lesson that may no longer run (a removed seat, an ended lapse window) reports status unavailable with the reason.
		/// </summary>
		private async Task<LmsAppLessonMessage> CheckedStateAsync(LmsRuntimeSession appSession)
		{
			var now = Clock();
			LmsAppLaunchKinds kind;
			if (StateRules.TryGetValue(appSession.LmsRuntimeSessionId, out var cached) && cached.Until > now)
				kind = cached.Kind;
			else
			{
				var check = await CheckLessonAsync(appSession.DepartmentId, appSession.UserId, appSession.LmsEnrollmentId, appSession.LmsLessonId);
				if (check.Error != LmsErrors.None)
				{
					StateRules.TryRemove(appSession.LmsRuntimeSessionId, out _);
					var registration = await _registrations.GetAsync(appSession.DepartmentId, appSession.LmsEnrollmentId, appSession.LmsLessonId);
					return LmsAppLaunchRules.Message(appSession.LmsEnrollmentId, appSession.LmsLessonId, check.KnownKind, registration, null, check.Error);
				}
				kind = check.Kind;
				if (StateRules.Count > 5000)
					StateRules.Clear();
				StateRules[appSession.LmsRuntimeSessionId] = (kind, now.Add(StateRulesCacheFor));
			}
			return await StateOfAsync(appSession, kind);
		}

		private async Task<LmsAppLessonMessage> StateOfAsync(LmsRuntimeSession appSession, LmsAppLaunchKinds kind)
		{
			var registration = await _registrations.GetAsync(appSession.DepartmentId, appSession.LmsEnrollmentId, appSession.LmsLessonId);
			var latest = await _sessions.GetLatestForAppSessionAsync(appSession.DepartmentId, appSession.LmsRuntimeSessionId);
			// The registration must be the session's own (the same member): a mismatch reports nothing recorded rather than another's results.
			if (registration != null && !string.Equals(registration.UserId, appSession.UserId, StringComparison.OrdinalIgnoreCase))
				registration = null;
			return LmsAppLaunchRules.Message(appSession.LmsEnrollmentId, appSession.LmsLessonId, kind, registration, latest);
		}

		#endregion State
	}
}
