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
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Text for class notifications and reports composed off the request thread (worker 95 reminders, a notice to someone else, a scheduled
	/// report): read culture-explicitly from the LmsClasses resources in the recipient's language (outbound-message-localization-gap).
	/// </summary>
	public static class LmsClassText
	{
		private static readonly ResourceManager Strings = new("Resgrid.Localization.Areas.User.LmsClasses.LmsClasses", typeof(Resgrid.Localization.SupportedLocales).Assembly);

		public static string Get(string key, CultureInfo culture)
		{
			if (string.IsNullOrEmpty(key))
				return string.Empty;
			var language = CultureInfo.GetCultureInfo((culture ?? CultureInfo.GetCultureInfo("en")).TwoLetterISOLanguageName);
			try
			{
				return Strings.GetResourceSet(language, true, false)?.GetString(key)
					?? Strings.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)?.GetString(key)
					?? key;
			}
			catch (MissingManifestResourceException)
			{
				return key;
			}
		}

		public static string Format(string key, CultureInfo culture, params object[] args)
		{
			var text = Get(key, culture);
			try
			{
				return args == null || args.Length == 0 ? text : string.Format(culture ?? CultureInfo.InvariantCulture, text, args);
			}
			catch (FormatException)
			{
				return text;
			}
		}
	}

	/// <summary>Class notifications (EventTypes 64–65): only active members, each in their own language; one failing member never stops the rest.</summary>
	public class LmsClassNotifier : ILmsClassNotifier
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserProfileService _profiles;

		public LmsClassNotifier(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings, IUserProfileService profiles)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_profiles = profiles;
		}

		public async Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, string titleKey, string messageKey, Func<CultureInfo, object[]> args, string eventCode,
			CancellationToken cancellationToken = default)
		{
			var wanted = (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0 || !ConfigHelper.CanTransmit(departmentId))
				return 0;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var sent = 0;
			foreach (var userId in wanted.Where(active.Contains))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var profile = await _profiles.GetProfileByUserIdAsync(userId);
					var culture = LmsProgramText.CultureFor(profile?.Language);
					var title = LmsClassText.Get(titleKey, culture);
					var message = LmsClassText.Format(messageKey, culture, args?.Invoke(culture) ?? Array.Empty<object>());
					if (await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, profile, false, eventCode))
						sent++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Class notification to {userId} in department {departmentId} failed.");
				}
			}

			return sent;
		}
	}

	/// <summary>
	/// Worker 95 (§13 T4, round 5B): class reminders, waitlist promotion and the roster retention purge. Each step starts from its own
	/// due-work query (sessions due a reminder, sessions with a waitlist, deleted sessions past retention), never a sweep of departments; the
	/// LMS gate is asked once per department with due work and a closed (or failing) gate skips it (R-L9, fails closed). Only active members
	/// are reminded or promoted (inactive-member-automation-policy). The purge needs no gate: it only removes what retention allows.
	/// </summary>
	public class LmsClassSweepService : ILmsClassSweepService
	{
		public const int Batch = 500;

		private readonly ILmsClassSessionsRepository _sessions;
		private readonly ILmsClassEnrollmentsRepository _enrollments;
		private readonly ILmsClassService _classes;
		private readonly ILmsAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly ILmsClassNotifier _notifier;
		private readonly IEventAggregator _events;

		public LmsClassSweepService(ILmsClassSessionsRepository sessions, ILmsClassEnrollmentsRepository enrollments, ILmsClassService classes, ILmsAccessService access,
			IDepartmentsService departments, ILmsClassNotifier notifier, IEventAggregator events)
		{
			_sessions = sessions;
			_enrollments = enrollments;
			_classes = classes;
			_access = access;
			_departments = departments;
			_notifier = notifier;
			_events = events;
		}

		public async Task<LmsClassSweepResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsClassSweepResult();
			var gates = new Dictionary<int, bool>();

			async Task<bool> GateAsync(int departmentId)
			{
				if (gates.TryGetValue(departmentId, out var open))
					return open;
				try
				{
					// The flag and the add-on window (one billing read for this department, this run).
					open = await _access.CanAuthorAndManageAsync(departmentId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"The LMS gate could not be read for department {departmentId}; its class work waits.");
					open = false;
				}
				gates[departmentId] = open;
				if (!open)
					result.GateClosed++;
				return open;
			}

			// 1. Reminders: sessions starting within their reminder window with an enrolled member not reminded yet (claimed before sending).
			var reminders = (await _sessions.GetReminderCandidatesAsync(utcNow, utcNow.AddHours(LmsClassRules.MaxReminderHours), Batch) ?? new List<LmsClassSession>())
				.Where(s => s.ReminderHours > 0 && s.StartsOn.AddHours(-s.ReminderHours) <= utcNow && s.StartsOn > utcNow).ToList();
			foreach (var department in reminders.GroupBy(s => s.DepartmentId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!await GateAsync(department.Key))
					continue;
				try
				{
					var active = await _departments.GetActiveMemberUserIdsAsync(department.Key) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					var timeZone = (await _departments.GetDepartmentByIdAsync(department.Key, false));
					foreach (var session in department)
					{
						var local = DateTime.SpecifyKind(session.StartsOn, DateTimeKind.Utc).TimeConverter(timeZone);
						foreach (var enrollment in (await _enrollments.GetForSessionAsync(department.Key, session.LmsClassSessionId) ?? new List<LmsClassEnrollment>())
							.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && !e.ReminderSentOn.HasValue && active.Contains(e.UserId ?? string.Empty)))
						{
							if (!await _enrollments.ClaimReminderAsync(department.Key, enrollment.LmsClassEnrollmentId, utcNow, cancellationToken))
								continue;
							result.Reminders += await _notifier.SendAsync(department.Key, new[] { enrollment.UserId }, "Notice.ReminderTitle", "Notice.Reminder",
								c => new object[] { session.Title, local.ToString("g", c), session.Location ?? session.MeetingUrl ?? string.Empty },
								"CL:" + session.LmsClassSessionId.ToString(CultureInfo.InvariantCulture), cancellationToken);
						}
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Class reminders failed for department {department.Key}.");
					result.Failures++;
				}
			}

			// 2. Waitlist promotion: future sessions with someone waiting (a seat may have opened, or the capacity grew).
			var waitlists = await _sessions.GetWaitlistCandidatesAsync(utcNow, Batch) ?? new List<LmsClassSession>();
			foreach (var department in waitlists.GroupBy(s => s.DepartmentId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!await GateAsync(department.Key))
					continue;
				foreach (var session in department)
				{
					try
					{
						result.Promoted += await _classes.PromoteWaitlistAsync(department.Key, session.LmsClassSessionId, utcNow, cancellationToken);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Waitlist promotion failed for class session {session.LmsClassSessionId}.");
						result.Failures++;
					}
				}
			}

			// 3. Roster retention: a deleted session is purged only after its retention (and a grace for an unsigned one); never before.
			var purge = await _sessions.GetPurgeCandidatesAsync(utcNow, utcNow - LmsClassService.PurgeGrace, Batch) ?? new List<LmsClassSession>();
			foreach (var session in purge.Where(s => LmsClassRules.CanPurge(s, utcNow)))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (await _sessions.PurgeAsync(session.DepartmentId, session.LmsClassSessionId, utcNow, cancellationToken))
					{
						result.Purged++;
						_events?.SendMessage(new AuditEvent
						{
							DepartmentId = session.DepartmentId,
							Type = AuditLogTypes.LmsClassRosterPurged,
							Before = JsonConvert.SerializeObject(new { session.LmsClassSessionId, session.Title, session.RetainUntil }),
							Successful = true,
							ServerName = Environment.MachineName
						});
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Class session {session.LmsClassSessionId} could not be purged.");
					result.Failures++;
				}
			}

			return result;
		}
	}

	/// <summary>
	/// Messages audience "event roster status" (§13a T4, LMS): the active members of one class session who are enrolled, attended, were
	/// absent or are waitlisted. Answers only while the LMS gate is open; the compose page asks <see cref="IsAvailableAsync"/> once.
	/// </summary>
	public class LmsEventRosterAudienceService : IEventRosterAudienceService
	{
		private readonly ILmsClassSessionsRepository _sessions;
		private readonly ILmsClassEnrollmentsRepository _enrollments;
		private readonly IDepartmentsService _departments;
		private readonly ILmsAccessService _access;

		public LmsEventRosterAudienceService(ILmsClassSessionsRepository sessions, ILmsClassEnrollmentsRepository enrollments, IDepartmentsService departments, ILmsAccessService access)
		{
			_sessions = sessions;
			_enrollments = enrollments;
			_departments = departments;
			_access = access;
		}

		public async Task<bool> IsAvailableAsync(int departmentId)
		{
			try
			{
				return departmentId > 0 && await _access.IsFlagEnabledAsync(departmentId) && await _access.CanAuthorAndManageAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public async Task<List<EventRosterAudienceSession>> GetSessionsAsync(int departmentId, DateTime utcNow) =>
			(await _sessions.GetForDepartmentAsync(departmentId, utcNow.AddDays(-90), utcNow.AddDays(90)) ?? new List<LmsClassSession>())
				.Where(s => s.Status != (int)LmsClassSessionStatuses.Cancelled)
				.OrderByDescending(s => s.StartsOn).Take(200)
				.Select(s => new EventRosterAudienceSession { SessionId = s.LmsClassSessionId, Title = s.Title, StartsOn = s.StartsOn }).ToList();

		public async Task<List<string>> ResolveAsync(int departmentId, int sessionId, EventRosterAudienceStatuses status)
		{
			var session = await _sessions.GetByIdForDepartmentAsync(departmentId, sessionId);
			if (session == null || session.DepartmentId != departmentId || session.IsDeleted)
				return new List<string>();
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return Filter(await _enrollments.GetForSessionAsync(departmentId, sessionId), status)
				.Where(id => active.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>The roster rows in the asked state (pure, so it can be tested).</summary>
		public static IEnumerable<string> Filter(IEnumerable<LmsClassEnrollment> roster, EventRosterAudienceStatuses status) =>
			(roster ?? Enumerable.Empty<LmsClassEnrollment>()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.UserId) && status switch
			{
				EventRosterAudienceStatuses.Enrolled => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled,
				EventRosterAudienceStatuses.Attended => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Attended,
				EventRosterAudienceStatuses.Absent => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Absent,
				EventRosterAudienceStatuses.Waitlisted => e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted,
				_ => false
			}).Select(e => e.UserId.Trim());
	}

	/// <summary>
	/// The timecard seam (R-C10) while no timecard exists: Workforce Pro (Shifts plan SM7, time and pay) is not built, so training time is
	/// never offered. Workforce Pro registers its own <see cref="ITrainingTimecardOffers"/> when it lands.
	/// </summary>
	public class UnavailableTrainingTimecardOffers : ITrainingTimecardOffers
	{
		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(false);

		public Task<bool> OfferAsync(TrainingTimeOffer offer, CancellationToken cancellationToken = default) => Task.FromResult(false);
	}

	/// <summary>
	/// The post-class quiz (§13 T4 item 1): at sign-off the session's LMS course (its assessment) is assigned to every attendee, due in two
	/// weeks. Assignment follows the course rules (the add-on, active members only).
	/// </summary>
	public class LmsCourseAssessmentPostClassEvaluation : ILmsPostClassEvaluation
	{
		public static readonly TimeSpan Due = TimeSpan.FromDays(14);

		private readonly ILmsLearnerService _learner;

		public LmsCourseAssessmentPostClassEvaluation(ILmsLearnerService learner)
		{
			_learner = learner;
		}

		public LmsClassPostEvaluationKinds Kind => LmsClassPostEvaluationKinds.CourseAssessment;

		public async Task<int> StartAsync(LmsActor signer, LmsClassSession session, IReadOnlyCollection<string> attendeeUserIds, CancellationToken cancellationToken = default)
		{
			if (signer == null || session?.LmsCourseId == null || attendeeUserIds == null || attendeeUserIds.Count == 0)
				return 0;
			var outcome = await _learner.AssignAsync(signer, session.LmsCourseId.Value,
				attendeeUserIds.Select(u => (IContentAudienceRule)new ContentAudienceRule(ContentAudienceKinds.User, u)).ToList(), DateTime.UtcNow.Add(Due),
				cancellationToken: cancellationToken);
			return outcome?.Created ?? 0;
		}
	}
}
