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
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Class sessions (§13 T4 items 1, 4, 6; §13a T4 Events; LMS add-on): each session sits on a calendar item it creates; members enrol under
	/// the session's rules (open / closed, capacity, cutoff, groups, approval, self-unenrol) with a waitlist promoted in order; members sign
	/// in by the rotating QR (optionally within a GPS radius), by an instructor, or by an imported meeting report; the instructor's sign-off
	/// snapshots the OSHA-grade roster and writes the transcript through <see cref="ILearningRecordWriter"/> (kind ClassSession), idempotently;
	/// the roster is held for its category's retention. Consumes baseline services only (§17.3); its callers check the LMS gate.
	/// </summary>
	public class LmsClassService : ILmsClassService
	{
		/// <summary>How long a deleted, unsigned session waits before worker 95 purges it.</summary>
		public static readonly TimeSpan PurgeGrace = TimeSpan.FromDays(30);

		private readonly ILmsClassSessionsRepository _sessions;
		private readonly ILmsClassEnrollmentsRepository _enrollments;
		private readonly ILmsClassInstructorsRepository _instructors;
		private readonly ILmsClassCostLinesRepository _costs;
		private readonly ILmsTrainingResourcesRepository _resources;
		private readonly ILmsClassResourceBookingsRepository _bookings;
		private readonly ILmsLessonPlansRepository _lessonPlans;
		private readonly ILmsLessonPlanVersionsRepository _lessonPlanVersions;
		private readonly ILmsTrainingPlansRepository _trainingPlans;
		private readonly ILmsTrainingPlanItemsRepository _trainingPlanItems;
		private readonly ILmsCourseService _courses;
		private readonly ITrainingService _trainings;
		private readonly ICalendarService _calendar;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly ICertificationService _certifications;
		private readonly IDepartmentSettingsService _settings;
		private readonly ILearningRecordService _learningRecords;
		private readonly ILearningRecordWriter _writer;
		private readonly IShiftsService _shifts;
		private readonly ILmsTrainingRequestService _requests;
		private readonly ILmsGateService _gate;
		private readonly ILmsClassNotifier _notifier;
		private readonly ITrainingTimecardOffers _timecards;
		private readonly IEnumerable<ILmsPostClassEvaluation> _evaluations;
		private readonly IEventAggregator _events;
		private readonly ILmsLearnerClassMailer _learnerMail;
		private readonly ILmsExternalLearnersRepository _learners;

		public LmsClassService(ILmsClassSessionsRepository sessions, ILmsClassEnrollmentsRepository enrollments, ILmsClassInstructorsRepository instructors,
			ILmsClassCostLinesRepository costs, ILmsTrainingResourcesRepository resources, ILmsClassResourceBookingsRepository bookings,
			ILmsLessonPlansRepository lessonPlans, ILmsLessonPlanVersionsRepository lessonPlanVersions, ILmsTrainingPlansRepository trainingPlans,
			ILmsTrainingPlanItemsRepository trainingPlanItems, ILmsCourseService courses, ITrainingService trainings, ICalendarService calendar,
			IDepartmentsService departments, IDepartmentGroupsService groups, IPersonnelRolesService roles, ICertificationService certifications,
			IDepartmentSettingsService settings, ILearningRecordService learningRecords, ILearningRecordWriter writer, IShiftsService shifts,
			ILmsTrainingRequestService requests, ILmsGateService gate, ILmsClassNotifier notifier, ITrainingTimecardOffers timecards,
			IEnumerable<ILmsPostClassEvaluation> evaluations, IEventAggregator events, ILmsLearnerClassMailer learnerMail, ILmsExternalLearnersRepository learners)
		{
			_sessions = sessions;
			_enrollments = enrollments;
			_instructors = instructors;
			_costs = costs;
			_resources = resources;
			_bookings = bookings;
			_lessonPlans = lessonPlans;
			_lessonPlanVersions = lessonPlanVersions;
			_trainingPlans = trainingPlans;
			_trainingPlanItems = trainingPlanItems;
			_courses = courses;
			_trainings = trainings;
			_calendar = calendar;
			_departments = departments;
			_groups = groups;
			_roles = roles;
			_certifications = certifications;
			_settings = settings;
			_learningRecords = learningRecords;
			_writer = writer;
			_shifts = shifts;
			_requests = requests;
			_gate = gate;
			_notifier = notifier;
			_timecards = timecards;
			_evaluations = evaluations ?? Enumerable.Empty<ILmsPostClassEvaluation>();
			_events = events;
			_learnerMail = learnerMail;
			_learners = learners;
		}

		#region Reads

		public async Task<List<LmsClassSession>> GetSessionsAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc) =>
			departmentId <= 0 ? new List<LmsClassSession>() : await _sessions.GetForDepartmentAsync(departmentId, fromUtc, toUtc) ?? new List<LmsClassSession>();

		public async Task<LmsClassSession> GetSessionAsync(int departmentId, int lmsClassSessionId)
		{
			var session = await _sessions.GetByIdForDepartmentAsync(departmentId, lmsClassSessionId);
			return session == null || session.DepartmentId != departmentId || session.IsDeleted ? null : session;
		}

		public async Task<LmsClassSession> GetSessionForCalendarItemAsync(int departmentId, int calendarItemId)
		{
			var session = await _sessions.GetByCalendarItemAsync(departmentId, calendarItemId);
			return session == null || session.DepartmentId != departmentId || session.IsDeleted ? null : session;
		}

		public Task<string> GetTrainingCurrencyAsync(int departmentId) => LmsProgramSettingsService.TrainingCurrencyAsync(_settings, departmentId);

		private async Task<LmsClassSession> RequireAsync(int departmentId, int lmsClassSessionId) =>
			await GetSessionAsync(departmentId, lmsClassSessionId) ?? throw new InvalidOperationException("lms_class_not_found");

		public async Task<List<LmsClassSession>> GetSeriesAsync(int departmentId, string seriesKey) =>
			(await _sessions.GetBySeriesAsync(departmentId, seriesKey) ?? new List<LmsClassSession>()).Where(s => s.DepartmentId == departmentId).ToList();

		public async Task<List<LmsClassEnrollment>> GetEnrollmentsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds) =>
			await _enrollments.GetForSessionsAsync(departmentId, lmsClassSessionIds) ?? new List<LmsClassEnrollment>();

		public async Task<List<LmsClassInstructor>> GetInstructorsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds) =>
			await _instructors.GetForSessionsAsync(departmentId, lmsClassSessionIds) ?? new List<LmsClassInstructor>();

		public async Task<bool> IsInstructorAsync(int departmentId, int lmsClassSessionId, string userId) =>
			!string.IsNullOrWhiteSpace(userId) && (await _instructors.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassInstructor>())
				.Any(i => string.Equals(i.UserId, userId, StringComparison.OrdinalIgnoreCase));

		public async Task<List<LmsClassInstructor>> GetTaughtAsync(int departmentId, string userId) =>
			string.IsNullOrWhiteSpace(userId) ? new List<LmsClassInstructor>() : await _instructors.GetForUserAsync(departmentId, userId) ?? new List<LmsClassInstructor>();

		public async Task<LmsClassSessionDetail> GetDetailAsync(int departmentId, int lmsClassSessionId, DateTime utcNow)
		{
			var session = await GetSessionAsync(departmentId, lmsClassSessionId);
			if (session == null)
				return null;

			var detail = new LmsClassSessionDetail { Session = session };
			var roster = await _enrollments.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassEnrollment>();
			detail.Instructors = await _instructors.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassInstructor>();
			detail.Costs = await _costs.GetForSessionsAsync(departmentId, new[] { lmsClassSessionId }) ?? new List<LmsClassCostLine>();
			detail.Bookings = await _bookings.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassResourceBooking>();
			detail.SeatsTaken = LmsClassRules.SeatsTaken(roster);
			if (session.LmsLessonPlanVersionId.HasValue)
				detail.LessonPlanVersion = await _lessonPlanVersions.GetByIdForDepartmentAsync(departmentId, session.LmsLessonPlanVersionId.Value);
			if (session.CalendarItemId.HasValue)
			{
				var item = await _calendar.GetCalendarItemByIdAsync(session.CalendarItemId.Value);
				detail.CalendarItem = item != null && item.DepartmentId == departmentId ? item : null;
			}

			var names = await NamesAsync(departmentId);
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var checkIns = await CalendarCheckInsAsync(session);
			var settings = await _settings.GetTrainingRecordSettingsAsync(departmentId) ?? new TrainingRecordSettings();
			HashSet<string> onShift;
			try
			{
				onShift = session.IsScheduled
					? await GetShiftConflictsAsync(departmentId, session, roster.Where(r => r.IsOnRoster).Select(r => r.UserId))
					: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Class shift conflicts could not be read.");
				onShift = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			detail.Roster = roster.Select(e =>
			{
				checkIns.TryGetValue(e.UserId ?? string.Empty, out var calendarCheckIn);
				return new LmsClassRosterRow
				{
					Enrollment = e,
					Name = names.TryGetValue(e.UserId ?? string.Empty, out var name) ? name : e.MemberName ?? e.UserId,
					OnShift = onShift.Contains(e.UserId ?? string.Empty),
					Inactive = !active.Contains(e.UserId ?? string.Empty) && !LmsExternalLearnerIds.IsExternal(e.UserId),
					IsExternal = LmsExternalLearnerIds.IsExternal(e.UserId),
					CalendarCheckIn = calendarCheckIn,
					ProjectedHours = LmsClassRules.HoursFor(session, e, calendarCheckIn, settings)
				};
			}).OrderBy(r => r.Enrollment.Status == (int)LmsClassEnrollmentStatuses.Enrolled ? 0 : r.Enrollment.Status == (int)LmsClassEnrollmentStatuses.Waitlisted ? 2 : 1)
				.ThenBy(r => r.Enrollment.WaitlistPosition ?? 0).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			return detail;
		}

		public async Task<List<LmsMyClass>> GetMyClassesAsync(int departmentId, string userId, DateTime utcNow)
		{
			var mine = new List<LmsMyClass>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return mine;

			var enrollments = (await _enrollments.GetForUserAsync(departmentId, userId) ?? new List<LmsClassEnrollment>()).Where(e => e.IsOnRoster || e.Attendance == (int)LmsClassAttendanceStates.Attended).ToList();
			var taught = await GetTaughtAsync(departmentId, userId);
			var group = await _groups.GetGroupForUserAsync(userId, departmentId);
			var groupId = group != null && group.DepartmentId == departmentId ? group.DepartmentGroupId : (int?)null;

			var upcoming = await _sessions.GetForDepartmentAsync(departmentId, utcNow.AddDays(-1), utcNow.AddDays(120)) ?? new List<LmsClassSession>();
			var ids = new HashSet<int>(enrollments.Select(e => e.LmsClassSessionId).Concat(taught.Select(t => t.LmsClassSessionId)));
			var sessions = upcoming.ToDictionary(s => s.LmsClassSessionId);
			foreach (var id in ids.Where(i => !sessions.ContainsKey(i)))
			{
				var session = await GetSessionAsync(departmentId, id);
				if (session != null)
					sessions[id] = session;
			}

			var seats = (await _enrollments.GetForSessionsAsync(departmentId, sessions.Keys) ?? new List<LmsClassEnrollment>())
				.GroupBy(e => e.LmsClassSessionId).ToDictionary(g => g.Key, g => LmsClassRules.SeatsTaken(g));
			foreach (var session in sessions.Values.Where(s => !s.IsDeleted))
			{
				var enrollment = enrollments.FirstOrDefault(e => e.LmsClassSessionId == session.LmsClassSessionId);
				var teaching = taught.Any(t => t.LmsClassSessionId == session.LmsClassSessionId);
				var refusal = LmsClassRules.SelfEnrollRefusal(session, utcNow, groupId);
				if (enrollment == null && !teaching && (refusal != null || !LmsClassRules.IsVisibleTo(session, groupId, false)))
					continue;
				mine.Add(new LmsMyClass
				{
					Session = session,
					Enrollment = enrollment,
					IsInstructor = teaching,
					EnrollRefusal = refusal,
					SeatsTaken = seats.TryGetValue(session.LmsClassSessionId, out var taken) ? taken : 0
				});
			}

			return mine.OrderBy(m => m.Session.StartsOn < utcNow.AddHours(-12) ? 1 : 0).ThenBy(m => m.Session.StartsOn).ToList();
		}

		#endregion Reads

		#region Authoring

		public async Task<List<LmsClassSession>> CreateAsync(int departmentId, LmsClassSessionInput input, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || input == null)
				throw new InvalidOperationException("lms_class_not_found");
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false) ?? throw new InvalidOperationException("lms_class_not_found");
			var template = await ValidateAsync(departmentId, input, null);

			var created = new List<LmsClassSession>();
			var seriesKey = input.Repeat != LmsClassRepeatKinds.None && input.Occurrences > 1 ? Guid.NewGuid().ToString("N") : null;
			var occurrences = LmsClassRules.Occurrences(input.LocalStart, input.LocalEnd, seriesKey == null ? LmsClassRepeatKinds.None : input.Repeat, input.Occurrences);
			var instructorIds = new List<string>();
			foreach (var requested in (input.InstructorUserIds ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
				instructorIds.Add(await RequireCurrentMemberAsync(departmentId, requested));
			var names = instructorIds.Count > 0 ? await NamesAsync(departmentId) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;

			for (var index = 0; index < occurrences.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var (localStart, localEnd) = occurrences[index];
				var session = Copy(template);
				session.DepartmentId = departmentId;
				session.SeriesKey = seriesKey;
				session.SeriesIndex = index;
				session.StartsOn = DateTimeHelpers.ConvertToUtc(localStart, department.TimeZone);
				session.EndsOn = DateTimeHelpers.ConvertToUtc(localEnd, department.TimeZone);
				session.QrSecret = session.QrEnabled ? LmsCrewCodes.NewSecret() : null;
				session.Status = (int)LmsClassSessionStatuses.Scheduled;
				session.CreatedOn = now;
				session.CreatedByUserId = userId;
				session.CalendarItemId = await CreateCalendarItemAsync(department, session, localStart, localEnd, userId, cancellationToken);
				var stored = await _sessions.InsertAsync(session, cancellationToken, true);

				var order = 0;
				foreach (var instructorId in instructorIds)
					await _instructors.InsertAsync(new LmsClassInstructor
					{
						DepartmentId = departmentId,
						LmsClassSessionId = stored.LmsClassSessionId,
						UserId = instructorId,
						Name = names.TryGetValue(instructorId, out var name) ? name : null,
						Role = order == 0 ? (int)LmsClassInstructorRoles.Lead : (int)LmsClassInstructorRoles.Assistant,
						SortOrder = order++,
						CreatedOn = now,
						CreatedByUserId = userId
					}, cancellationToken, true);

				created.Add(stored);
			}

			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionChanged, null, JsonConvert.SerializeObject(created.Select(Summary)));
			foreach (var session in created)
				Announce(TrainingClassWorkflowChanges.Scheduled, session, null, userId);
			return created;
		}

		public async Task<LmsClassSession> UpdateAsync(int departmentId, int lmsClassSessionId, LmsClassSessionInput input, string userId, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (session.Status == (int)LmsClassSessionStatuses.Cancelled)
				throw new InvalidOperationException("lms_class_cancelled");
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false) ?? throw new InvalidOperationException("lms_class_not_found");
			var template = await ValidateAsync(departmentId, input, session);
			var before = JsonConvert.SerializeObject(Summary(session));

			var startsOn = DateTimeHelpers.ConvertToUtc(input.LocalStart, department.TimeZone);
			var endsOn = DateTimeHelpers.ConvertToUtc(input.LocalEnd, department.TimeZone);
			var moved = startsOn != session.StartsOn || endsOn != session.EndsOn;
			var bookings = await _bookings.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassResourceBooking>();
			if (moved)
				foreach (var booking in bookings)
					if (await _bookings.HasOverlapAsync(departmentId, booking.LmsTrainingResourceId, startsOn, endsOn, lmsClassSessionId))
						throw new InvalidOperationException("lms_class_resource_conflict");

			var capacityGrew = template.Capacity == 0 || (session.Capacity > 0 && template.Capacity > session.Capacity);
			var keepLocation = session.Location;
			var keepMeeting = session.MeetingUrl;
			var keep = new { session.LmsClassSessionId, session.CalendarItemId, session.SeriesKey, session.SeriesIndex, session.QrSecret, session.Status, session.SignedOffOn,
				session.SignedOffByUserId, session.RetainUntil, session.CreatedOn, session.CreatedByUserId, session.LmsTrainingPlanItemId, session.CancelledOn, session.CancelReason };
			session = Copy(template);
			session.LmsClassSessionId = keep.LmsClassSessionId;
			session.DepartmentId = departmentId;
			session.CalendarItemId = keep.CalendarItemId;
			session.SeriesKey = keep.SeriesKey;
			session.SeriesIndex = keep.SeriesIndex;
			session.Status = keep.Status;
			session.SignedOffOn = keep.SignedOffOn;
			session.SignedOffByUserId = keep.SignedOffByUserId;
			session.CreatedOn = keep.CreatedOn;
			session.CreatedByUserId = keep.CreatedByUserId;
			session.LmsTrainingPlanItemId = keep.LmsTrainingPlanItemId;
			session.StartsOn = startsOn;
			session.EndsOn = endsOn;
			session.QrSecret = session.QrEnabled ? keep.QrSecret ?? LmsCrewCodes.NewSecret() : keep.QrSecret;
			session.RetainUntil = keep.SignedOffOn.HasValue ? LmsClassRules.RetainUntil(endsOn, session.RetentionYears) : null;
			session.UpdatedOn = DateTime.UtcNow;
			session.UpdatedByUserId = userId;
			var stored = await _sessions.SaveOrUpdateAsync(session, cancellationToken, true);

			if (moved)
				foreach (var booking in bookings)
				{
					booking.StartsOn = startsOn;
					booking.EndsOn = endsOn;
					await _bookings.SaveOrUpdateAsync(booking, cancellationToken, true);
				}

			await SyncCalendarItemAsync(department, stored, input.LocalStart, input.LocalEnd, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionChanged, before, JsonConvert.SerializeObject(Summary(stored)));
			var changedPlace = !string.Equals(keepLocation ?? string.Empty, stored.Location ?? string.Empty, StringComparison.Ordinal) ||
				!string.Equals(keepMeeting ?? string.Empty, stored.MeetingUrl ?? string.Empty, StringComparison.Ordinal);
			if (moved || changedPlace)
			{
				// Round 7B: a reminder sent for the old time no longer holds; worker 95 reminds everyone again for the new one.
				if (moved)
					await _enrollments.ResetRemindersAsync(departmentId, stored.LmsClassSessionId, cancellationToken);
				if (stored.IsScheduled)
					await MailLearnersAsync(stored, (await _enrollments.GetForSessionAsync(departmentId, stored.LmsClassSessionId) ?? new List<LmsClassEnrollment>())
						.Where(e => e.IsOnRoster).Select(e => e.UserId), LmsLearnerNoticeKinds.Changed, cancellationToken);
			}
			if (capacityGrew && stored.IsScheduled)
				await PromoteWaitlistAsync(departmentId, stored.LmsClassSessionId, DateTime.UtcNow, cancellationToken);
			return stored;
		}

		public async Task<LmsClassSession> CreateFromPlanItemAsync(int departmentId, int lmsTrainingPlanItemId, string userId, CancellationToken cancellationToken = default)
		{
			var item = await _trainingPlanItems.GetByIdForDepartmentAsync(departmentId, lmsTrainingPlanItemId);
			if (item == null || item.DepartmentId != departmentId)
				throw new InvalidOperationException("lms_class_not_found");
			var plan = await _trainingPlans.GetByIdForDepartmentAsync(departmentId, item.LmsTrainingPlanId);
			if (plan == null || plan.IsDeleted)
				throw new InvalidOperationException("lms_class_not_found");
			if (item.ClassSessionId.HasValue)
			{
				var existing = await GetSessionAsync(departmentId, item.ClassSessionId.Value);
				if (existing != null)
					return existing;
			}

			// The planned date, else the first weekday of the month; 09:00 department-local, for the planned hours of one session.
			var day = item.PlannedOn?.Date ?? FirstWeekday(plan.Year, Math.Clamp(item.Month, 1, 12));
			var start = day.AddHours(9);
			var minutes = (double)Math.Clamp(item.PlannedHours, 0.25m, 24m) * 60d;
			var created = await CreateAsync(departmentId, new LmsClassSessionInput
			{
				Title = item.Title,
				Description = item.Notes,
				CategoryId = item.CategoryId,
				LocalStart = start,
				LocalEnd = start.AddMinutes(minutes),
				EnrollmentOpen = true,
				AllowSelfUnenroll = true,
				QrEnabled = true,
				ReminderHours = 24
			}, userId, cancellationToken);

			var session = created[0];
			session.LmsTrainingPlanItemId = item.LmsTrainingPlanItemId;
			session = await _sessions.SaveOrUpdateAsync(session, cancellationToken, true);
			item.ClassSessionId = session.LmsClassSessionId;
			item.CalendarItemId ??= session.CalendarItemId;
			item.UpdatedOn = DateTime.UtcNow;
			await _trainingPlanItems.SaveOrUpdateAsync(item, cancellationToken, true);
			return session;
		}

		public async Task<LmsClassSession> CancelAsync(int departmentId, int lmsClassSessionId, string reason, string userId, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (session.Status == (int)LmsClassSessionStatuses.SignedOff)
				throw new InvalidOperationException("lms_class_signed_off");
			if (session.Status == (int)LmsClassSessionStatuses.Cancelled)
				return session;

			session.Status = (int)LmsClassSessionStatuses.Cancelled;
			session.CancelledOn = DateTime.UtcNow;
			session.CancelReason = Trim(reason, 500);
			session.UpdatedOn = DateTime.UtcNow;
			session.UpdatedByUserId = userId;
			var stored = await _sessions.SaveOrUpdateAsync(session, cancellationToken, true);
			await _bookings.DeleteForSessionAsync(departmentId, lmsClassSessionId, cancellationToken);
			await RemoveCalendarItemAsync(stored, cancellationToken);

			var roster = (await _enrollments.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassEnrollment>()).Where(e => e.IsOnRoster).Select(e => e.UserId).ToList();
			await NotifyAsync(departmentId, roster, "Notice.CancelledTitle", "Notice.Cancelled", stored, cancellationToken);
			await MailLearnersAsync(stored, roster, LmsLearnerNoticeKinds.Cancelled, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionCancelled, null, JsonConvert.SerializeObject(new { stored.LmsClassSessionId, stored.Title, stored.CancelReason }));
			Announce(TrainingClassWorkflowChanges.Cancelled, stored, null, userId);
			return stored;
		}

		public async Task<bool> DeleteAsync(int departmentId, int lmsClassSessionId, string userId, CancellationToken cancellationToken = default)
		{
			var session = await GetSessionAsync(departmentId, lmsClassSessionId);
			if (session == null)
				return false;

			session.IsDeleted = true;
			session.DeletedOn = DateTime.UtcNow;
			session.DeletedByUserId = userId;
			await _sessions.SaveOrUpdateAsync(session, cancellationToken, true);
			if (session.Status != (int)LmsClassSessionStatuses.SignedOff)
			{
				await _bookings.DeleteForSessionAsync(departmentId, lmsClassSessionId, cancellationToken);
				await RemoveCalendarItemAsync(session, cancellationToken);
			}

			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionDeleted, JsonConvert.SerializeObject(Summary(session)), null);
			return true;
		}

		public async Task<bool> PurgeAsync(int departmentId, int lmsClassSessionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var session = await _sessions.GetByIdForDepartmentAsync(departmentId, lmsClassSessionId);
			if (session == null || session.DepartmentId != departmentId)
				return false;
			if (!LmsClassRules.CanPurge(session, utcNow))
				throw new InvalidOperationException("lms_class_retention_hold");
			var purged = await _sessions.PurgeAsync(departmentId, lmsClassSessionId, utcNow, cancellationToken);
			if (purged)
				Audit(departmentId, null, AuditLogTypes.LmsClassRosterPurged, JsonConvert.SerializeObject(new { session.LmsClassSessionId, session.Title, session.RetainUntil }), null);
			return purged;
		}

		public async Task<LmsClassSession> RotateQrAsync(int departmentId, int lmsClassSessionId, string userId, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			session.QrEnabled = true;
			session.QrSecret = LmsCrewCodes.NewSecret();
			session.UpdatedOn = DateTime.UtcNow;
			session.UpdatedByUserId = userId;
			return await _sessions.SaveOrUpdateAsync(session, cancellationToken, true);
		}

		/// <summary>Validates the input and builds the session's fields (not its identity, times or state).</summary>
		private async Task<LmsClassSession> ValidateAsync(int departmentId, LmsClassSessionInput input, LmsClassSession existing)
		{
			if (input == null)
				throw new InvalidOperationException("lms_class_not_found");
			if (input.LocalEnd <= input.LocalStart)
				throw new InvalidOperationException("lms_class_times_invalid");
			if ((input.LocalEnd - input.LocalStart).TotalDays > 14)
				throw new InvalidOperationException("lms_class_times_invalid");
			if (input.Capacity < 0 || input.Capacity > LmsClassSession.MaxCapacity)
				throw new InvalidOperationException("lms_class_capacity_invalid");
			if (input.CutoffDays < 0 || input.CutoffDays > LmsClassRules.MaxCutoffDays || input.ReminderHours < 0 || input.ReminderHours > LmsClassRules.MaxReminderHours)
				throw new InvalidOperationException("lms_class_rules_invalid");
			if (input.Repeat != LmsClassRepeatKinds.None && (input.Occurrences < 1 || input.Occurrences > LmsClassSession.MaxOccurrences))
				throw new InvalidOperationException("lms_class_rules_invalid");
			var url = Trim(input.MeetingUrl, LmsClassSession.UrlMaxLength);
			if (url != null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
				throw new InvalidOperationException("lms_class_meeting_url_invalid");
			if (input.GpsRequired && (!input.GpsLatitude.HasValue || !input.GpsLongitude.HasValue || !LmsClassRules.IsValidPosition(input.GpsLatitude.Value, input.GpsLongitude.Value)))
				throw new InvalidOperationException("lms_class_gps_location_required");

			var title = Trim(input.Title, LmsClassSession.TitleMaxLength);
			var kind = Enum.IsDefined(typeof(LmsClassCourseKinds), input.CourseKind) ? input.CourseKind : LmsClassCourseKinds.None;
			int? courseId = null, trainingId = null;
			if (kind == LmsClassCourseKinds.Course)
			{
				var course = input.LmsCourseId.HasValue ? await _courses.GetCourseAsync(departmentId, input.LmsCourseId.Value) : null;
				if (course == null || course.IsDeleted || course.IsSystem || course.DepartmentId != departmentId)
					throw new InvalidOperationException("lms_class_course_invalid");
				courseId = course.LmsCourseId;
				title ??= Trim(course.Title, LmsClassSession.TitleMaxLength);
			}
			else if (kind == LmsClassCourseKinds.Training)
			{
				var training = input.TrainingId.HasValue ? await _trainings.GetTrainingByIdAsync(input.TrainingId.Value) : null;
				if (training == null || training.DepartmentId != departmentId)
					throw new InvalidOperationException("lms_class_course_invalid");
				trainingId = training.TrainingId;
				title ??= Trim(training.Name, LmsClassSession.TitleMaxLength);
			}
			if (string.IsNullOrWhiteSpace(title))
				throw new InvalidOperationException("lms_class_title_required");

			TrainingCategory category = null;
			if (input.CategoryId.HasValue && input.CategoryId.Value > 0)
			{
				category = (await _learningRecords.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>()).FirstOrDefault(c => c.TrainingCategoryId == input.CategoryId.Value && !c.IsDeleted);
				if (category == null)
					throw new InvalidOperationException("lms_class_category_invalid");
			}

			int? lessonPlanId = null, lessonPlanVersionId = null;
			if (input.LmsLessonPlanId.HasValue && input.LmsLessonPlanId.Value > 0)
			{
				var plan = await _lessonPlans.GetByIdForDepartmentAsync(departmentId, input.LmsLessonPlanId.Value);
				if (plan == null || plan.IsDeleted || plan.DepartmentId != departmentId || !plan.CurrentVersionId.HasValue)
					throw new InvalidOperationException("lms_class_lesson_plan_invalid");
				lessonPlanId = plan.LmsLessonPlanId;
				// A session keeps the version it was taught from: an edit that does not change the plan keeps its frozen version.
				lessonPlanVersionId = existing != null && existing.LmsLessonPlanId == plan.LmsLessonPlanId && existing.LmsLessonPlanVersionId.HasValue
					? existing.LmsLessonPlanVersionId : plan.CurrentVersionId;
			}

			var groupIds = (await _groups.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>()).Select(g => g.DepartmentGroupId).ToHashSet();
			int? retention = input.RetentionYears.HasValue && input.RetentionYears.Value > 0 ? Math.Min(input.RetentionYears.Value, 75) : category?.RetentionYears;
			if (!retention.HasValue || retention.Value <= 0)
			{
				var defaults = await _settings.GetTrainingRecordSettingsAsync(departmentId);
				retention = defaults != null && defaults.DefaultRetentionYears > 0 ? defaults.DefaultRetentionYears : (int?)null;
			}
			// The department's training currency unless the class names its own (region neutrality: never a fixed USD).
			var currency = LmsTrainingCurrency.Normalize(input.Currency) ?? LmsTrainingCurrency.Normalize(existing?.Currency)
				?? await LmsProgramSettingsService.TrainingCurrencyAsync(_settings, departmentId);

			return new LmsClassSession
			{
				Title = title,
				Description = Trim(input.Description, LmsClassSession.TextMaxLength),
				ContentSummary = Trim(input.ContentSummary, LmsClassSession.TextMaxLength),
				CourseKind = (int)kind,
				LmsCourseId = courseId,
				TrainingId = trainingId,
				LmsLessonPlanId = lessonPlanId,
				LmsLessonPlanVersionId = lessonPlanVersionId,
				CategoryId = category?.TrainingCategoryId,
				CourseCode = Trim(input.CourseCode, 100),
				Location = Trim(input.Location, LmsClassSession.LocationMaxLength),
				MeetingUrl = url,
				Capacity = input.Capacity,
				WaitlistEnabled = input.WaitlistEnabled,
				EnrollmentOpen = input.EnrollmentOpen,
				RequiresApproval = input.RequiresApproval,
				CutoffDays = input.CutoffDays,
				AllowSelfUnenroll = input.AllowSelfUnenroll,
				RestrictGroupIds = LmsClassRules.JoinIds((input.RestrictGroupIds ?? new List<int>()).Where(groupIds.Contains)),
				VisibleGroupIds = LmsClassRules.JoinIds((input.VisibleGroupIds ?? new List<int>()).Where(groupIds.Contains)),
				Tags = LmsClassRules.NormalizeTags(input.Tags),
				ReminderHours = input.ReminderHours,
				QrEnabled = input.QrEnabled,
				GpsRequired = input.GpsRequired,
				GpsLatitude = input.GpsLatitude.HasValue ? Math.Round(input.GpsLatitude.Value, 6) : null,
				GpsLongitude = input.GpsLongitude.HasValue ? Math.Round(input.GpsLongitude.Value, 6) : null,
				GpsRadiusMeters = input.GpsRadiusMeters <= 0 ? LmsClassRules.DefaultGpsRadiusMeters : Math.Min(input.GpsRadiusMeters, LmsClassRules.MaxGpsRadiusMeters),
				PostClassEvaluationKind = Enum.IsDefined(typeof(LmsClassPostEvaluationKinds), input.PostClassEvaluationKind) ? (int)input.PostClassEvaluationKind : 0,
				PostClassEvaluationRef = Trim(input.PostClassEvaluationRef, 128),
				RetentionYears = retention,
				Currency = currency
			};
		}

		private static LmsClassSession Copy(LmsClassSession s) => JsonConvert.DeserializeObject<LmsClassSession>(JsonConvert.SerializeObject(s));

		private async Task<int?> CreateCalendarItemAsync(Department department, LmsClassSession session, DateTime localStart, DateTime localEnd, string userId,
			CancellationToken cancellationToken)
		{
			try
			{
				var types = await _calendar.GetAllCalendarItemTypesForDepartmentAsync(department.DepartmentId) ?? new List<CalendarItemType>();
				var type = types.FirstOrDefault(t => t.CountsAsTraining && session.CategoryId.HasValue && t.DefaultCategoryId == session.CategoryId)
					?? types.FirstOrDefault(t => t.CountsAsTraining);
				var saved = await _calendar.AddNewCalendarItemAsync(new CalendarItem
				{
					DepartmentId = department.DepartmentId,
					Title = session.Title,
					Start = localStart,
					End = localEnd,
					StartTimezone = department.TimeZone,
					EndTimezone = department.TimeZone,
					Description = CalendarDescription(session),
					Location = session.Location,
					ItemType = type?.CalendarItemTypeId ?? 0,
					// Attendance runs on the class roster (QR, instructor, import): the calendar's own check-in stays off so one class is one
					// way of signing in; a check-in an admin still makes there is read at sign-off.
					CheckInType = (int)CalendarItemCheckInTypes.Disabled,
					SignupType = (int)CalendarItemSignupTypes.None,
					CreatorUserId = userId,
					Public = string.IsNullOrWhiteSpace(session.VisibleGroupIds)
				}, department.TimeZone, cancellationToken);
				return saved != null && saved.CalendarItemId > 0 ? saved.CalendarItemId : null;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The class session's calendar item could not be created.");
				throw new InvalidOperationException("lms_class_calendar_failed");
			}
		}

		private async Task SyncCalendarItemAsync(Department department, LmsClassSession session, DateTime localStart, DateTime localEnd, CancellationToken cancellationToken)
		{
			if (!session.CalendarItemId.HasValue)
			{
				session.CalendarItemId = await CreateCalendarItemAsync(department, session, localStart, localEnd, session.UpdatedByUserId, cancellationToken);
				await _sessions.SaveOrUpdateAsync(session, cancellationToken, true);
				return;
			}

			try
			{
				var item = await _calendar.GetCalendarItemByIdAsync(session.CalendarItemId.Value);
				if (item == null || item.DepartmentId != department.DepartmentId)
					return;
				item.Title = session.Title;
				item.Start = localStart;
				item.End = localEnd;
				item.Location = session.Location;
				item.Description = CalendarDescription(session);
				item.Public = string.IsNullOrWhiteSpace(session.VisibleGroupIds);
				await _calendar.UpdateCalendarItemAsync(item, department.TimeZone, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The calendar item of class session {session.LmsClassSessionId} could not be updated.");
			}
		}

		private async Task RemoveCalendarItemAsync(LmsClassSession session, CancellationToken cancellationToken)
		{
			if (!session.CalendarItemId.HasValue)
				return;
			try
			{
				var item = await _calendar.GetCalendarItemByIdAsync(session.CalendarItemId.Value);
				if (item != null && item.DepartmentId == session.DepartmentId)
					await _calendar.DeleteCalendarItemByIdAsync(item.CalendarItemId, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The calendar item of class session {session.LmsClassSessionId} could not be removed.");
			}
		}

		private static string CalendarDescription(LmsClassSession session)
		{
			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(session.Description))
				parts.Add(session.Description);
			if (!string.IsNullOrWhiteSpace(session.MeetingUrl))
				parts.Add(session.MeetingUrl);
			return parts.Count == 0 ? null : string.Join("\n\n", parts);
		}

		private static DateTime FirstWeekday(int year, int month)
		{
			var day = new DateTime(year, month, 1);
			while (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday)
				day = day.AddDays(1);
			return day;
		}

		public async Task<LmsClassInstructor> AddInstructorAsync(int departmentId, int lmsClassSessionId, LmsClassInstructor instructor, string userId,
			CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (instructor == null)
				throw new InvalidOperationException("lms_class_instructor_invalid");
			var memberId = Trim(instructor.UserId, 128);
			var name = Trim(instructor.Name, LmsClassInstructor.NameMaxLength);
			if (memberId != null)
			{
				memberId = await RequireCurrentMemberAsync(departmentId, memberId);
				var existing = await _instructors.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassInstructor>();
				var same = existing.FirstOrDefault(i => string.Equals(i.UserId, memberId, StringComparison.OrdinalIgnoreCase));
				if (same != null)
					return same;
				name ??= (await NamesAsync(departmentId)).TryGetValue(memberId, out var n) ? n : null;
			}
			else if (name == null)
				throw new InvalidOperationException("lms_class_instructor_invalid");

			var all = await _instructors.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassInstructor>();
			var stored = await _instructors.InsertAsync(new LmsClassInstructor
			{
				DepartmentId = departmentId,
				LmsClassSessionId = session.LmsClassSessionId,
				UserId = memberId,
				Name = name,
				Qualifications = Trim(instructor.Qualifications, LmsClassInstructor.QualificationsMaxLength),
				Role = Enum.IsDefined(typeof(LmsClassInstructorRoles), instructor.Role) ? instructor.Role : 0,
				HoursTaught = instructor.HoursTaught.HasValue ? Math.Round(Math.Clamp(instructor.HoursTaught.Value, 0m, 9999m), 2) : null,
				SortOrder = all.Count == 0 ? 0 : all.Max(i => i.SortOrder) + 1,
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = userId
			}, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionChanged, null, JsonConvert.SerializeObject(new { session.LmsClassSessionId, instructor = stored.Name }));
			return stored;
		}

		public async Task<bool> RemoveInstructorAsync(int departmentId, int lmsClassInstructorId, string userId, CancellationToken cancellationToken = default)
		{
			var instructor = await _instructors.GetByIdForDepartmentAsync(departmentId, lmsClassInstructorId);
			if (instructor == null || instructor.DepartmentId != departmentId)
				return false;
			var deleted = await _instructors.DeleteAsync(instructor, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionChanged, JsonConvert.SerializeObject(new { instructor.LmsClassSessionId, instructor = instructor.Name }), null);
			return deleted;
		}

		public async Task<LmsClassCostLine> AddCostAsync(int departmentId, int lmsClassSessionId, LmsClassCostLine line, string userId, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (line == null || line.Amount < 0 || line.Amount > LmsClassCostLine.MaxAmount || !Enum.IsDefined(typeof(LmsClassCostKinds), line.Kind))
				throw new InvalidOperationException("lms_class_cost_invalid");
			var stored = await _costs.InsertAsync(new LmsClassCostLine
			{
				DepartmentId = departmentId,
				LmsClassSessionId = session.LmsClassSessionId,
				Kind = line.Kind,
				Description = Trim(line.Description, LmsClassCostLine.DescriptionMaxLength),
				Amount = Math.Round(line.Amount, 2),
				Currency = LmsTrainingCurrency.Normalize(session.Currency) ?? await LmsProgramSettingsService.TrainingCurrencyAsync(_settings, departmentId),
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = userId
			}, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionChanged, null, JsonConvert.SerializeObject(new { session.LmsClassSessionId, cost = stored.Amount, stored.Kind }));
			return stored;
		}

		public async Task<bool> RemoveCostAsync(int departmentId, int lmsClassCostLineId, string userId, CancellationToken cancellationToken = default)
		{
			var line = await _costs.GetByIdForDepartmentAsync(departmentId, lmsClassCostLineId);
			if (line == null || line.DepartmentId != departmentId)
				return false;
			var deleted = await _costs.DeleteAsync(line, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.LmsClassSessionChanged, JsonConvert.SerializeObject(new { line.LmsClassSessionId, cost = line.Amount, line.Kind }), null);
			return deleted;
		}

		#endregion Authoring

		#region Training resources

		public async Task<List<LmsTrainingResource>> GetResourcesAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsTrainingResource>() : await _resources.GetForDepartmentAsync(departmentId) ?? new List<LmsTrainingResource>();

		public async Task<LmsTrainingResource> SaveResourceAsync(LmsTrainingResource resource, string userId, CancellationToken cancellationToken = default)
		{
			if (resource == null || resource.DepartmentId <= 0)
				throw new InvalidOperationException("lms_class_not_found");
			var name = Trim(resource.Name, LmsTrainingResource.NameMaxLength) ?? throw new InvalidOperationException("lms_class_name_required");
			LmsTrainingResource row;
			if (resource.LmsTrainingResourceId > 0)
			{
				row = await _resources.GetByIdForDepartmentAsync(resource.DepartmentId, resource.LmsTrainingResourceId);
				if (row == null || row.IsDeleted || row.DepartmentId != resource.DepartmentId)
					throw new InvalidOperationException("lms_class_not_found");
				row.UpdatedOn = DateTime.UtcNow;
			}
			else
				row = new LmsTrainingResource { DepartmentId = resource.DepartmentId, CreatedOn = DateTime.UtcNow, CreatedByUserId = userId };

			row.Name = name;
			row.Kind = Enum.IsDefined(typeof(LmsTrainingResourceKinds), resource.Kind) ? resource.Kind : (int)LmsTrainingResourceKinds.Other;
			row.PoiId = resource.PoiId.HasValue && resource.PoiId.Value > 0 ? resource.PoiId : null;
			row.Capacity = resource.Capacity.HasValue && resource.Capacity.Value > 0 ? Math.Min(resource.Capacity.Value, LmsClassSession.MaxCapacity) : null;
			row.Notes = Trim(resource.Notes, LmsTrainingResource.NotesMaxLength);
			var stored = await _resources.SaveOrUpdateAsync(row, cancellationToken, true);
			Audit(resource.DepartmentId, userId, AuditLogTypes.LmsTrainingResourceChanged, null, JsonConvert.SerializeObject(new { stored.LmsTrainingResourceId, stored.Name, stored.Kind }));
			return stored;
		}

		public async Task<bool> DeleteResourceAsync(int departmentId, int lmsTrainingResourceId, string userId, CancellationToken cancellationToken = default)
		{
			var row = await _resources.GetByIdForDepartmentAsync(departmentId, lmsTrainingResourceId);
			if (row == null || row.IsDeleted || row.DepartmentId != departmentId)
				return false;
			row.IsDeleted = true;
			row.UpdatedOn = DateTime.UtcNow;
			await _resources.SaveOrUpdateAsync(row, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsTrainingResourceChanged, JsonConvert.SerializeObject(new { row.LmsTrainingResourceId, row.Name }), null);
			return true;
		}

		public async Task<LmsClassResourceBooking> BookResourceAsync(int departmentId, int lmsClassSessionId, int lmsTrainingResourceId, string userId,
			CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (!session.IsScheduled)
				throw new InvalidOperationException("lms_class_not_open");
			var resource = await _resources.GetByIdForDepartmentAsync(departmentId, lmsTrainingResourceId);
			if (resource == null || resource.IsDeleted || resource.DepartmentId != departmentId)
				throw new InvalidOperationException("lms_class_not_found");
			var mine = (await _bookings.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassResourceBooking>())
				.FirstOrDefault(b => b.LmsTrainingResourceId == lmsTrainingResourceId);
			if (mine != null)
				return mine;
			if (await _bookings.HasOverlapAsync(departmentId, lmsTrainingResourceId, session.StartsOn, session.EndsOn, lmsClassSessionId))
				throw new InvalidOperationException("lms_class_resource_conflict");

			var stored = await _bookings.InsertAsync(new LmsClassResourceBooking
			{
				DepartmentId = departmentId,
				LmsTrainingResourceId = lmsTrainingResourceId,
				LmsClassSessionId = lmsClassSessionId,
				StartsOn = session.StartsOn,
				EndsOn = session.EndsOn,
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = userId
			}, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsTrainingResourceChanged, null, JsonConvert.SerializeObject(new { session.LmsClassSessionId, resource.Name, booked = true }));
			return stored;
		}

		public async Task<bool> CancelBookingAsync(int departmentId, int lmsClassResourceBookingId, string userId, CancellationToken cancellationToken = default)
		{
			var booking = await _bookings.GetByIdForDepartmentAsync(departmentId, lmsClassResourceBookingId);
			if (booking == null || booking.DepartmentId != departmentId)
				return false;
			var deleted = await _bookings.DeleteAsync(booking, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.LmsTrainingResourceChanged, JsonConvert.SerializeObject(new { booking.LmsClassSessionId, booking.LmsTrainingResourceId, booked = false }), null);
			return deleted;
		}

		public async Task<List<LmsClassResourceBooking>> GetBookingsAsync(int departmentId, int lmsTrainingResourceId, DateTime fromUtc, DateTime toUtc) =>
			await _bookings.GetForResourceAsync(departmentId, lmsTrainingResourceId, fromUtc, toUtc) ?? new List<LmsClassResourceBooking>();

		#endregion Training resources

		#region Enrolment and the waitlist

		public async Task<LmsClassEnrollment> EnrollSelfAsync(int departmentId, int lmsClassSessionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			userId = await RequireCurrentMemberAsync(departmentId, userId);
			var group = await _groups.GetGroupForUserAsync(userId, departmentId);
			var refusal = LmsClassRules.SelfEnrollRefusal(session, utcNow, group != null && group.DepartmentId == departmentId ? group.DepartmentGroupId : (int?)null);
			if (refusal != null)
				throw new InvalidOperationException(refusal);
			await RequireLearnerSeatAsync(departmentId, userId);
			return await PlaceAsync(session, userId, userId, false, utcNow, cancellationToken);
		}

		public async Task<LmsClassEnrollment> EnrollMemberAsync(int departmentId, int lmsClassSessionId, string memberUserId, string managerUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (!session.IsScheduled)
				throw new InvalidOperationException("lms_class_not_open");
			var member = await _departments.GetDepartmentMemberAsync(memberUserId, departmentId, false);
			if (!DepartmentMemberStateHelper.IsActiveMember(member, departmentId))
				throw new InvalidOperationException("lms_class_not_member");
			var enrollment = await PlaceAsync(session, member.UserId, managerUserId, true, utcNow, cancellationToken);
			await NotifyAsync(departmentId, new[] { enrollment.UserId }, "Notice.EnrolledTitle", enrollment.Status == (int)LmsClassEnrollmentStatuses.Waitlisted ? "Notice.Waitlisted" : "Notice.Enrolled",
				session, cancellationToken);
			return enrollment;
		}

		/// <summary>
		/// T5 (round 6E): an external learner's place on the roster under the same rules (capacity and waitlist; approval unless a class manager
		/// places them), with their name and organization kept as the roster snapshot (the OSHA-grade roster's name and job title).
		/// </summary>
		public async Task<LmsClassEnrollment> PlaceExternalAsync(int departmentId, int lmsClassSessionId, string learnerUserId, string name, string organization,
			string actingUserId, bool byManager, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (!LmsExternalLearnerIds.IsExternal(learnerUserId))
				throw new InvalidOperationException("lms_class_not_member");
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (!session.IsScheduled)
				throw new InvalidOperationException("lms_class_not_open");
			return await PlaceAsync(session, learnerUserId, string.IsNullOrWhiteSpace(actingUserId) ? learnerUserId : actingUserId, byManager, utcNow, cancellationToken,
				Trim(name, LmsClassEnrollment.NameMaxLength), Trim(organization, LmsClassEnrollment.NameMaxLength));
		}

		/// <summary>Puts a member on the roster where the rules place them; an existing roster row is returned (or re-used when they had left).</summary>
		private async Task<LmsClassEnrollment> PlaceAsync(LmsClassSession session, string userId, string actingUserId, bool byManager, DateTime utcNow, CancellationToken cancellationToken,
			string snapshotName = null, string snapshotTitle = null)
		{
			var departmentId = session.DepartmentId;
			var roster = await _enrollments.GetForSessionAsync(departmentId, session.LmsClassSessionId) ?? new List<LmsClassEnrollment>();
			var existing = roster.FirstOrDefault(e => string.Equals(e.UserId, userId, StringComparison.OrdinalIgnoreCase));
			if (existing != null && existing.IsOnRoster)
			{
				// A manager confirming a pending or waitlisted member enrols them where a seat allows.
				if (!byManager || existing.Status == (int)LmsClassEnrollmentStatuses.Enrolled)
					return existing;
			}

			var placement = LmsClassRules.Placement(session, LmsClassRules.SeatsTaken(roster.Where(e => e != existing)), byManager)
				?? throw new InvalidOperationException("lms_class_full");
			var row = existing ?? new LmsClassEnrollment { DepartmentId = departmentId, LmsClassSessionId = session.LmsClassSessionId, UserId = userId.Trim() };
			if (existing != null && existing.Status == (int)placement)
				return existing;
			row.Status = (int)placement;
			row.WaitlistPosition = placement == LmsClassEnrollmentStatuses.Waitlisted ? LmsClassRules.NextWaitlistPosition(roster) : (int?)null;
			if (!string.IsNullOrEmpty(snapshotName))
				row.MemberName = snapshotName;
			if (!string.IsNullOrEmpty(snapshotTitle))
				row.JobTitle = snapshotTitle;
			row.EnrolledOn = existing?.IsOnRoster == true ? existing.EnrolledOn : utcNow;
			row.EnrolledByUserId = existing?.IsOnRoster == true ? existing.EnrolledByUserId : actingUserId;
			row.WithdrawnOn = null;
			row.UpdatedOn = utcNow;
			if (byManager && existing != null)
			{
				row.DecidedOn = utcNow;
				row.DecidedByUserId = actingUserId;
			}

			LmsClassEnrollment stored;
			try
			{
				stored = row.LmsClassEnrollmentId > 0 ? await _enrollments.SaveOrUpdateAsync(row, cancellationToken, true) : await _enrollments.InsertAsync(row, cancellationToken, true);
			}
			catch (Exception ex) when (IsDuplicate(ex))
			{
				// Two requests raced to enrol the same member: the other one's row stands.
				return await _enrollments.GetForMemberAsync(departmentId, session.LmsClassSessionId, userId);
			}

			Audit(departmentId, actingUserId, AuditLogTypes.LmsClassRosterChanged, null,
				JsonConvert.SerializeObject(new { session.LmsClassSessionId, stored.UserId, status = ((LmsClassEnrollmentStatuses)stored.Status).ToString() }));
			if (LmsLearnerNoticeRules.PlacementNotice(stored) is LmsLearnerNoticeKinds placementNotice)
				await MailLearnersAsync(session, new[] { stored.UserId }, placementNotice, cancellationToken);
			return stored;
		}

		private static bool IsDuplicate(Exception ex)
		{
			for (var e = ex; e != null; e = e.InnerException)
			{
				if (e is Npgsql.PostgresException postgres && postgres.SqlState == "23505")
					return true;
				if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
					return true;
			}
			return false;
		}

		public async Task<LmsClassEnrollment> UnenrollSelfAsync(int departmentId, int lmsClassSessionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			var enrollment = await _enrollments.GetForMemberAsync(departmentId, lmsClassSessionId, userId);
			var refusal = LmsClassRules.SelfUnenrollRefusal(session, enrollment, utcNow);
			if (refusal != null)
				throw new InvalidOperationException(refusal);
			return await LeaveAsync(session, enrollment, LmsClassEnrollmentStatuses.Withdrawn, userId, utcNow, cancellationToken);
		}

		public async Task<LmsClassEnrollment> RemoveMemberAsync(int departmentId, int lmsClassEnrollmentId, string managerUserId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var enrollment = await _enrollments.GetByIdForDepartmentAsync(departmentId, lmsClassEnrollmentId);
			if (enrollment == null || enrollment.DepartmentId != departmentId)
				throw new InvalidOperationException("lms_class_not_enrolled");
			var session = await RequireAsync(departmentId, enrollment.LmsClassSessionId);
			if (session.Status == (int)LmsClassSessionStatuses.SignedOff)
				throw new InvalidOperationException("lms_class_signed_off");
			var stored = await LeaveAsync(session, enrollment, LmsClassEnrollmentStatuses.Removed, managerUserId, utcNow, cancellationToken);
			await NotifyAsync(departmentId, new[] { stored.UserId }, "Notice.EnrolledTitle", "Notice.Removed", session, cancellationToken);
			return stored;
		}

		private async Task<LmsClassEnrollment> LeaveAsync(LmsClassSession session, LmsClassEnrollment enrollment, LmsClassEnrollmentStatuses status, string actingUserId, DateTime utcNow,
			CancellationToken cancellationToken)
		{
			var heldSeat = enrollment.HoldsSeat;
			enrollment.Status = (int)status;
			enrollment.WaitlistPosition = null;
			enrollment.WithdrawnOn = utcNow;
			enrollment.UpdatedOn = utcNow;
			if (status == LmsClassEnrollmentStatuses.Removed)
			{
				enrollment.DecidedOn = utcNow;
				enrollment.DecidedByUserId = actingUserId;
			}
			var stored = await _enrollments.SaveOrUpdateAsync(enrollment, cancellationToken, true);
			Audit(session.DepartmentId, actingUserId, AuditLogTypes.LmsClassRosterChanged, null,
				JsonConvert.SerializeObject(new { session.LmsClassSessionId, stored.UserId, status = status.ToString() }));
			if (heldSeat)
				await PromoteWaitlistAsync(session.DepartmentId, session.LmsClassSessionId, utcNow, cancellationToken);
			return stored;
		}

		public async Task<LmsClassEnrollment> DecideAsync(int departmentId, int lmsClassEnrollmentId, bool approve, string managerUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var enrollment = await _enrollments.GetByIdForDepartmentAsync(departmentId, lmsClassEnrollmentId);
			if (enrollment == null || enrollment.DepartmentId != departmentId || enrollment.Status != (int)LmsClassEnrollmentStatuses.PendingApproval)
				throw new InvalidOperationException("lms_class_not_pending");
			var session = await RequireAsync(departmentId, enrollment.LmsClassSessionId);

			LmsClassEnrollment stored;
			if (approve)
				stored = await PlaceAsync(session, enrollment.UserId, managerUserId, true, utcNow, cancellationToken);
			else
			{
				enrollment.Status = (int)LmsClassEnrollmentStatuses.Declined;
				enrollment.DecidedOn = utcNow;
				enrollment.DecidedByUserId = managerUserId;
				enrollment.UpdatedOn = utcNow;
				stored = await _enrollments.SaveOrUpdateAsync(enrollment, cancellationToken, true);
				Audit(departmentId, managerUserId, AuditLogTypes.LmsClassRosterChanged, null,
					JsonConvert.SerializeObject(new { session.LmsClassSessionId, stored.UserId, status = LmsClassEnrollmentStatuses.Declined.ToString() }));
			}

			var key = !approve ? "Notice.Declined" : stored.Status == (int)LmsClassEnrollmentStatuses.Waitlisted ? "Notice.Waitlisted" : "Notice.Enrolled";
			await NotifyAsync(departmentId, new[] { stored.UserId }, "Notice.EnrolledTitle", key, session, cancellationToken);
			return stored;
		}

		public async Task<int> PromoteWaitlistAsync(int departmentId, int lmsClassSessionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var session = await GetSessionAsync(departmentId, lmsClassSessionId);
			if (session == null || !session.IsScheduled || !session.WaitlistEnabled || session.StartsOn <= utcNow)
				return 0;
			var roster = await _enrollments.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassEnrollment>();
			if (!roster.Any(e => e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted))
				return 0;

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var promoted = 0;
			// External learners (T5) are never department members: they are promoted in the same order while their account is active (round 7B:
			// a suspended learner is passed over, like an inactive member).
			var waitingLearners = roster.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted && LmsExternalLearnerIds.IsExternal(e.UserId)).Select(e => e.UserId).ToList();
			var activeLearners = waitingLearners.Count == 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				: new HashSet<string>((await _learners.GetByLearnerUserIdsAsync(departmentId, waitingLearners) ?? new List<LmsExternalLearner>())
					.Where(l => l.DepartmentId == departmentId && l.Status == (int)LmsExternalLearnerStatuses.Active).Select(l => l.LearnerUserId), StringComparer.OrdinalIgnoreCase);
			foreach (var next in LmsClassRules.PromotionOrder(session, roster, id => active.Contains(id) || activeLearners.Contains(id), utcNow))
			{
				if (!await _enrollments.PromoteAsync(departmentId, next.LmsClassEnrollmentId, utcNow, cancellationToken))
					continue;
				promoted++;
				Audit(departmentId, null, AuditLogTypes.LmsClassRosterChanged, null,
					JsonConvert.SerializeObject(new { session.LmsClassSessionId, next.UserId, status = "Promoted" }));
				await NotifyAsync(departmentId, new[] { next.UserId }, "Notice.EnrolledTitle", "Notice.Promoted", session,
					cancellationToken);
				next.Status = (int)LmsClassEnrollmentStatuses.Enrolled;
				next.PromotedOn = utcNow;
				await MailLearnersAsync(session, new[] { next.UserId }, LmsLearnerNoticeKinds.Promoted, cancellationToken);
				Announce(TrainingClassWorkflowChanges.WaitlistPromoted, session, roster, null, next.UserId);
			}

			return promoted;
		}

		#endregion Enrolment and the waitlist

		#region Attendance and sign-off

		public string CurrentCode(LmsClassSession session, DateTime utcNow) => LmsClassRules.CurrentCode(session, utcNow);

		public async Task<LmsClassEnrollment> SignInAsync(int departmentId, int lmsClassSessionId, string userId, string code, decimal? latitude, decimal? longitude, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			var refusal = LmsClassRules.SignInRefusal(session, code, utcNow, latitude, longitude);
			if (refusal != null)
				throw new InvalidOperationException(refusal);
			userId = await RequireCurrentMemberAsync(departmentId, userId);
			await RequireLearnerSeatAsync(departmentId, userId);

			var hasPosition = latitude.HasValue && longitude.HasValue && LmsClassRules.IsValidPosition(latitude.Value, longitude.Value);
			return await MarkPresentAsync(session, userId, LmsClassCheckInMethods.Qr, userId, utcNow, enrollment =>
			{
				enrollment.CheckInOn ??= utcNow;
				if (hasPosition)
				{
					enrollment.CheckInLatitude = Math.Round(latitude.Value, 6);
					enrollment.CheckInLongitude = Math.Round(longitude.Value, 6);
				}
			}, cancellationToken);
		}

		public async Task<LmsClassEnrollment> SignOutAsync(int departmentId, int lmsClassSessionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			var enrollment = await _enrollments.GetForMemberAsync(departmentId, lmsClassSessionId, userId);
			if (enrollment == null || !enrollment.CheckInOn.HasValue)
				throw new InvalidOperationException("lms_class_not_signed_in");
			if (session.Status != (int)LmsClassSessionStatuses.Scheduled)
				throw new InvalidOperationException("lms_class_not_open");
			if (enrollment.CheckOutOn.HasValue)
				return enrollment;
			enrollment.CheckOutOn = utcNow > enrollment.CheckInOn.Value ? utcNow : enrollment.CheckInOn.Value;
			enrollment.UpdatedOn = utcNow;
			return await _enrollments.SaveOrUpdateAsync(enrollment, cancellationToken, true);
		}

		public async Task<LmsClassEnrollment> MarkAttendanceAsync(int departmentId, int lmsClassEnrollmentId, LmsClassAttendanceStates state, decimal? hoursOverride, string actingUserId,
			DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var enrollment = await _enrollments.GetByIdForDepartmentAsync(departmentId, lmsClassEnrollmentId);
			if (enrollment == null || enrollment.DepartmentId != departmentId)
				throw new InvalidOperationException("lms_class_not_enrolled");
			var session = await RequireAsync(departmentId, enrollment.LmsClassSessionId);
			if (session.Status == (int)LmsClassSessionStatuses.Cancelled)
				throw new InvalidOperationException("lms_class_cancelled");
			if (!Enum.IsDefined(typeof(LmsClassAttendanceStates), state))
				throw new InvalidOperationException("lms_class_rules_invalid");
			if (hoursOverride.HasValue && (hoursOverride.Value < 0 || hoursOverride.Value > 9999))
				throw new InvalidOperationException("lms_class_hours_invalid");

			var before = ((LmsClassAttendanceStates)enrollment.Attendance).ToString();
			enrollment.Attendance = (int)state;
			enrollment.HoursOverride = hoursOverride.HasValue ? Math.Round(hoursOverride.Value, 2) : null;
			// Marked attended from the waitlist or a pending request: they were there, so they hold a place on the roster.
			if (state == LmsClassAttendanceStates.Attended && enrollment.Status != (int)LmsClassEnrollmentStatuses.Enrolled)
			{
				enrollment.Status = (int)LmsClassEnrollmentStatuses.Enrolled;
				enrollment.WaitlistPosition = null;
			}
			if (state == LmsClassAttendanceStates.Attended && enrollment.CheckInMethod == (int)LmsClassCheckInMethods.None)
				enrollment.CheckInMethod = (int)LmsClassCheckInMethods.Manual;
			enrollment.UpdatedOn = utcNow;
			var stored = await _enrollments.SaveOrUpdateAsync(enrollment, cancellationToken, true);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsClassAttendanceChanged, before,
				JsonConvert.SerializeObject(new { session.LmsClassSessionId, stored.UserId, attendance = state.ToString(), stored.HoursOverride }));
			return stored;
		}

		public async Task<LmsClassEnrollment> AddWalkInAsync(int departmentId, int lmsClassSessionId, string memberUserId, string actingUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (session.Status == (int)LmsClassSessionStatuses.Cancelled)
				throw new InvalidOperationException("lms_class_cancelled");
			var memberId = await RequireCurrentMemberAsync(departmentId, memberUserId);
			return await MarkPresentAsync(session, memberId, LmsClassCheckInMethods.Manual, actingUserId, utcNow, null, cancellationToken);
		}

		/// <summary>Marks a member attended (a walk-in when they were not on the roster); <paramref name="apply"/> adds the sign-in details.</summary>
		private async Task<LmsClassEnrollment> MarkPresentAsync(LmsClassSession session, string userId, LmsClassCheckInMethods method, string actingUserId, DateTime utcNow,
			Action<LmsClassEnrollment> apply, CancellationToken cancellationToken)
		{
			var enrollment = await _enrollments.GetForMemberAsync(session.DepartmentId, session.LmsClassSessionId, userId);
			var isNew = enrollment == null;
			enrollment ??= new LmsClassEnrollment
			{
				DepartmentId = session.DepartmentId,
				LmsClassSessionId = session.LmsClassSessionId,
				UserId = userId.Trim(),
				EnrolledOn = utcNow,
				EnrolledByUserId = actingUserId
			};
			if (isNew || !enrollment.IsOnRoster)
				enrollment.IsWalkIn = enrollment.Status != (int)LmsClassEnrollmentStatuses.Enrolled;
			enrollment.Status = (int)LmsClassEnrollmentStatuses.Enrolled;
			enrollment.WaitlistPosition = null;
			enrollment.WithdrawnOn = null;
			enrollment.Attendance = (int)LmsClassAttendanceStates.Attended;
			if (enrollment.CheckInMethod == (int)LmsClassCheckInMethods.None || method == LmsClassCheckInMethods.Qr)
				enrollment.CheckInMethod = (int)method;
			apply?.Invoke(enrollment);
			enrollment.UpdatedOn = utcNow;

			LmsClassEnrollment stored;
			try
			{
				stored = isNew ? await _enrollments.InsertAsync(enrollment, cancellationToken, true) : await _enrollments.SaveOrUpdateAsync(enrollment, cancellationToken, true);
			}
			catch (Exception ex) when (isNew && IsDuplicate(ex))
			{
				return await MarkPresentAsync(session, userId, method, actingUserId, utcNow, apply, cancellationToken);
			}

			Audit(session.DepartmentId, actingUserId, AuditLogTypes.LmsClassAttendanceChanged, null,
				JsonConvert.SerializeObject(new { session.LmsClassSessionId, stored.UserId, attendance = "Attended", method = method.ToString(), stored.IsWalkIn }));
			return stored;
		}

		public async Task<LmsAttendanceImportResult> ImportAttendanceAsync(int departmentId, int lmsClassSessionId, string reportText, string actingUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (session.Status == (int)LmsClassSessionStatuses.Cancelled)
				throw new InvalidOperationException("lms_class_cancelled");
			var rows = LmsAttendanceImport.Parse(reportText);
			if (rows.Count == 0)
				throw new InvalidOperationException("lms_class_import_empty");

			var result = new LmsAttendanceImportResult { Rows = rows.Count };
			var users = await _departments.GetAllUsersForDepartmentAsync(departmentId, true, false) ?? new List<Model.Identity.IdentityUser>();
			var byEmail = users.Where(u => !string.IsNullOrWhiteSpace(u?.Email)).GroupBy(u => u.Email.Trim(), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
			// Person names carry the id upper-cased; the roster keeps the account's own id.
			var canonical = users.Where(u => !string.IsNullOrWhiteSpace(u?.Id)).GroupBy(u => u.Id, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
			var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
			{
				if (person?.UserId == null)
					continue;
				var personId = canonical.TryGetValue(person.UserId, out var id) ? id : person.UserId;
				foreach (var key in new[] { $"{person.FirstName} {person.LastName}", $"{person.LastName}, {person.FirstName}", $"{person.LastName} {person.FirstName}" })
				{
					var normalized = NormalizeName(key);
					if (normalized.Length > 0 && !byName.ContainsKey(normalized))
						byName[normalized] = personId;
				}
			}
			var roster = await _enrollments.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassEnrollment>();

			foreach (var row in rows)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string userId = null;
				if (!string.IsNullOrWhiteSpace(row.Email) && byEmail.TryGetValue(row.Email.Trim(), out var byMail))
					userId = byMail;
				else if (!string.IsNullOrWhiteSpace(row.Name) && byName.TryGetValue(NormalizeName(row.Name), out var byPerson))
					userId = byPerson;
				var member = userId == null ? null : await _departments.GetDepartmentMemberAsync(userId, departmentId, false);
				if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
				{
					result.Unmatched.Add(row);
					continue;
				}

				var wasOnRoster = roster.Any(e => e.IsOnRoster && string.Equals(e.UserId, member.UserId, StringComparison.OrdinalIgnoreCase));
				await MarkPresentAsync(session, member.UserId, LmsClassCheckInMethods.Import, actingUserId, utcNow, enrollment =>
				{
					if (row.Minutes > 0)
						enrollment.ImportedMinutes = Math.Min(row.Minutes, 24 * 60);
					if (enrollment.CheckInMethod != (int)LmsClassCheckInMethods.Qr)
						enrollment.CheckInMethod = (int)LmsClassCheckInMethods.Import;
				}, cancellationToken);
				result.Matched++;
				if (!wasOnRoster)
					result.WalkIns++;
			}

			return result;
		}

		private static string NormalizeName(string name) =>
			string.Join(" ", (name ?? string.Empty).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

		public async Task<LmsClassSignOffResult> SignOffAsync(int departmentId, int lmsClassSessionId, string signerUserId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var session = await RequireAsync(departmentId, lmsClassSessionId);
			if (session.Status == (int)LmsClassSessionStatuses.Cancelled)
				throw new InvalidOperationException("lms_class_cancelled");
			if (session.StartsOn > utcNow)
				throw new InvalidOperationException("lms_class_not_started");

			var firstSignOff = !session.SignedOffOn.HasValue;
			var roster = await _enrollments.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassEnrollment>();
			var instructors = await _instructors.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassInstructor>();
			var checkIns = await CalendarCheckInsAsync(session);
			var settings = await _settings.GetTrainingRecordSettingsAsync(departmentId) ?? new TrainingRecordSettings();
			var names = await NamesAsync(departmentId);
			var roles = await _roles.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();
			var roleLookup = new Dictionary<string, List<PersonnelRole>>(roles, StringComparer.OrdinalIgnoreCase);

			// Explicit absent marking: an enrolled member nobody marked is attended when they signed in somewhere, absent otherwise.
			var hours = new Dictionary<int, decimal>();
			foreach (var enrollment in roster.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled))
			{
				checkIns.TryGetValue(enrollment.UserId ?? string.Empty, out var calendarCheckIn);
				if (enrollment.Attendance == (int)LmsClassAttendanceStates.Unmarked)
				{
					var signedIn = enrollment.CheckInOn.HasValue || enrollment.ImportedMinutes > 0 || calendarCheckIn != null;
					enrollment.Attendance = (int)(signedIn ? LmsClassAttendanceStates.Attended : LmsClassAttendanceStates.Absent);
					if (signedIn && calendarCheckIn != null && enrollment.CheckInMethod == (int)LmsClassCheckInMethods.None)
						enrollment.CheckInMethod = (int)LmsClassCheckInMethods.Calendar;
				}
				if (enrollment.Attendance == (int)LmsClassAttendanceStates.Attended)
					hours[enrollment.LmsClassEnrollmentId] = LmsClassRules.HoursFor(session, enrollment, calendarCheckIn, settings);

				// The OSHA-grade roster: the attendee's name and job title as they were when the class was signed off.
				enrollment.MemberName = Trim(names.TryGetValue(enrollment.UserId ?? string.Empty, out var name) ? name : enrollment.MemberName, LmsClassEnrollment.NameMaxLength);
				enrollment.JobTitle = Trim(roleLookup.TryGetValue(enrollment.UserId ?? string.Empty, out var memberRoles)
					? string.Join(", ", memberRoles.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name)).Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
					: enrollment.JobTitle, LmsClassEnrollment.NameMaxLength);
				enrollment.Hours = hours.TryGetValue(enrollment.LmsClassEnrollmentId, out var h) ? h : 0m;
			}

			// Trainer names and qualifications: a member instructor's live certifications when none were entered.
			foreach (var instructor in instructors.Where(i => i.IsMember))
			{
				var changed = false;
				if (string.IsNullOrWhiteSpace(instructor.Name) && names.TryGetValue(instructor.UserId, out var instructorName))
				{
					instructor.Name = Trim(instructorName, LmsClassInstructor.NameMaxLength);
					changed = true;
				}
				if (string.IsNullOrWhiteSpace(instructor.Qualifications))
				{
					var held = (await _certifications.GetCertificationsByUserIdAsync(instructor.UserId) ?? new List<PersonnelCertification>())
						.Where(c => c != null && !c.IsDeleted && c.DepartmentId == departmentId && (!c.ExpiresOn.HasValue || c.ExpiresOn.Value >= session.StartsOn) && !string.IsNullOrWhiteSpace(c.Name))
						.Select(c => string.IsNullOrWhiteSpace(c.Number) ? c.Name.Trim() : c.Name.Trim() + " (" + c.Number.Trim() + ")").Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
					if (held.Count > 0)
					{
						instructor.Qualifications = Trim(string.Join("; ", held), LmsClassInstructor.QualificationsMaxLength);
						changed = true;
					}
				}
				if (changed)
					await _instructors.SaveOrUpdateAsync(instructor, cancellationToken, true);
			}

			var objectives = new List<string>();
			if (session.LmsLessonPlanVersionId.HasValue)
			{
				var version = await _lessonPlanVersions.GetByIdForDepartmentAsync(departmentId, session.LmsLessonPlanVersionId.Value);
				if (version != null)
					objectives = version.Content().Objectives;
			}

			var drafts = LmsClassRules.SignOffDrafts(session, roster, hours, instructors, objectives, signerUserId);
			var write = await _writer.WriteSourceAsync(departmentId, LearningRecordSourceKinds.ClassSession, session.LmsClassSessionId.ToString(CultureInfo.InvariantCulture),
				drafts, signerUserId, cancellationToken) ?? new LearningRecordWriteResult();

			var result = new LmsClassSignOffResult
			{
				Attended = roster.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Attended),
				Absent = roster.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Absent),
				Written = write.Written,
				Voided = write.Voided,
				Unchanged = write.Unchanged,
				InstructorRecords = drafts.Count(d => d.ComponentKey == LmsClassRules.InstructorComponent)
			};

			foreach (var record in write.Records.Where(r => r != null && string.IsNullOrEmpty(r.ComponentKey)))
			{
				var enrollment = roster.FirstOrDefault(e => string.Equals(e.UserId, record.UserId, StringComparison.OrdinalIgnoreCase));
				if (enrollment != null)
					enrollment.LearningRecordId = record.LearningRecordId;
			}

			// Training time to the timecard (R-C10), where one exists; each attendee's hours are offered once.
			bool timecard;
			try
			{
				timecard = await _timecards.IsAvailableAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The timecard could not be asked.");
				timecard = false;
			}
			result.TimecardAvailable = timecard;

			foreach (var enrollment in roster.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled))
			{
				// External learners (T5) have no timecard: only members' hours are offered.
				if (timecard && enrollment.Attendance == (int)LmsClassAttendanceStates.Attended && !enrollment.TimecardOfferedOn.HasValue && (enrollment.Hours ?? 0m) > 0 &&
					!LmsExternalLearnerIds.IsExternal(enrollment.UserId))
				{
					try
					{
						if (await _timecards.OfferAsync(new TrainingTimeOffer
						{
							DepartmentId = departmentId,
							UserId = enrollment.UserId,
							StartUtc = enrollment.CheckInOn ?? session.StartsOn,
							EndUtc = enrollment.CheckOutOn ?? session.EndsOn,
							Hours = enrollment.Hours ?? 0m,
							Title = session.Title,
							SourceKind = (int)LearningRecordSourceKinds.ClassSession,
							SourceRefId = session.LmsClassSessionId.ToString(CultureInfo.InvariantCulture)
						}, cancellationToken))
						{
							enrollment.TimecardOfferedOn = utcNow;
							result.TimecardOffers++;
						}
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Training time for {enrollment.UserId} was not offered to the timecard.");
					}
				}

				enrollment.UpdatedOn = utcNow;
				await _enrollments.SaveOrUpdateAsync(enrollment, cancellationToken, true);
			}

			session.Status = (int)LmsClassSessionStatuses.SignedOff;
			session.SignedOffOn = utcNow;
			session.SignedOffByUserId = signerUserId;
			session.RetainUntil = LmsClassRules.RetainUntil(session.EndsOn, session.RetentionYears);
			session.UpdatedOn = utcNow;
			session.UpdatedByUserId = signerUserId;
			await _sessions.SaveOrUpdateAsync(session, cancellationToken, true);

			if (firstSignOff && session.PostClassEvaluationKind != (int)LmsClassPostEvaluationKinds.None)
				result.EvaluationsStarted = await StartEvaluationAsync(session, signerUserId,
					roster.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Attended &&
						!LmsExternalLearnerIds.IsExternal(e.UserId)).Select(e => e.UserId).ToList(), cancellationToken);

			Audit(departmentId, signerUserId, AuditLogTypes.LmsClassSignedOff, null,
				JsonConvert.SerializeObject(new { session.LmsClassSessionId, session.Title, result.Attended, result.Absent, result.Written, result.Voided, session.RetainUntil }));
			if (firstSignOff)
			{
				await MailLearnersAsync(session, roster.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Attended)
					.Select(e => e.UserId), LmsLearnerNoticeKinds.SignedOff, cancellationToken);
				Announce(TrainingClassWorkflowChanges.SignedOff, session, roster, signerUserId);
			}
			return result;
		}

		private async Task<int> StartEvaluationAsync(LmsClassSession session, string signerUserId, IReadOnlyCollection<string> attendees, CancellationToken cancellationToken)
		{
			var evaluation = _evaluations.FirstOrDefault(e => (int)e.Kind == session.PostClassEvaluationKind);
			if (evaluation == null || attendees.Count == 0)
				return 0;
			try
			{
				var actor = await _gate.GetActorAsync(session.DepartmentId, signerUserId);
				return await evaluation.StartAsync(actor, session, attendees, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// The records are written; a failing evaluation never undoes the sign-off.
				Logging.LogException(ex, $"The post-class evaluation of class session {session.LmsClassSessionId} could not be started.");
				return 0;
			}
		}

		#endregion Attendance and sign-off

		#region Shift calendar and costs

		public async Task<HashSet<string>> GetShiftConflictsAsync(int departmentId, LmsClassSession session, IEnumerable<string> userIds)
		{
			var ids = (userIds ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
			if (session == null || ids.Count == 0)
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var localStart = DateTime.SpecifyKind(session.StartsOn, DateTimeKind.Utc).TimeConverter(department);
			var localEnd = DateTime.SpecifyKind(session.EndsOn, DateTimeKind.Utc).TimeConverter(department);
			var schedules = await _shifts.GetShiftDaySchedulesForDateRangeAsync(departmentId, localStart.Date.AddDays(-1), localEnd.Date) ?? new List<ShiftDaySchedule>();
			return LmsClassRules.ShiftConflicts(localStart, localEnd, schedules, ids);
		}

		public async Task<List<LmsClassShiftConflict>> GetUpcomingConflictsAsync(int departmentId, DateTime utcNow, int days)
		{
			var result = new List<LmsClassShiftConflict>();
			var sessions = (await _sessions.GetForDepartmentAsync(departmentId, utcNow, utcNow.AddDays(Math.Clamp(days, 1, 60))) ?? new List<LmsClassSession>())
				.Where(s => s.IsScheduled && s.StartsOn > utcNow).ToList();
			if (sessions.Count == 0)
				return result;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var roster = (await _enrollments.GetForSessionsAsync(departmentId, sessions.Select(s => s.LmsClassSessionId)) ?? new List<LmsClassEnrollment>())
				.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled).GroupBy(e => e.LmsClassSessionId).ToDictionary(g => g.Key, g => g.Select(e => e.UserId).ToList());
			var first = DateTime.SpecifyKind(sessions.Min(s => s.StartsOn), DateTimeKind.Utc).TimeConverter(department);
			var last = DateTime.SpecifyKind(sessions.Max(s => s.EndsOn), DateTimeKind.Utc).TimeConverter(department);
			var schedules = await _shifts.GetShiftDaySchedulesForDateRangeAsync(departmentId, first.Date.AddDays(-1), last.Date) ?? new List<ShiftDaySchedule>();

			foreach (var session in sessions)
			{
				var enrolled = roster.TryGetValue(session.LmsClassSessionId, out var ids) ? ids : new List<string>();
				var localStart = DateTime.SpecifyKind(session.StartsOn, DateTimeKind.Utc).TimeConverter(department);
				var localEnd = DateTime.SpecifyKind(session.EndsOn, DateTimeKind.Utc).TimeConverter(department);
				result.Add(new LmsClassShiftConflict
				{
					Session = session,
					Enrolled = enrolled.Count,
					OnShiftUserIds = LmsClassRules.ShiftConflicts(localStart, localEnd, schedules, enrolled).OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList()
				});
			}

			return result;
		}

		public async Task<LmsClassCostRollup> GetCostRollupAsync(int departmentId, DateTime fromUtc, DateTime toUtc, LmsClassCostGroupings groupBy)
		{
			var sessions = (await _sessions.GetForDepartmentAsync(departmentId, fromUtc, toUtc) ?? new List<LmsClassSession>()).Where(s => s.StartsOn >= fromUtc && s.StartsOn < toUtc).ToList();
			var ids = sessions.Select(s => s.LmsClassSessionId).ToList();
			var lines = ids.Count == 0 ? new List<LmsClassCostLine>() : await _costs.GetForSessionsAsync(departmentId, ids) ?? new List<LmsClassCostLine>();
			var attendees = ids.Count == 0 ? new List<LmsClassEnrollment>() : await _enrollments.GetForSessionsAsync(departmentId, ids) ?? new List<LmsClassEnrollment>();

			var requestGrouping = groupBy switch
			{
				LmsClassCostGroupings.Member => LmsTrainingRequestCostGroupings.Member,
				LmsClassCostGroupings.Year => LmsTrainingRequestCostGroupings.Year,
				_ => LmsTrainingRequestCostGroupings.Course
			};
			LmsTrainingRequestCostSummary requests = null;
			try
			{
				requests = await _requests.GetCostSummaryAsync(departmentId, fromUtc, toUtc, requestGrouping);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training request costs could not be read for the class cost roll-up.");
			}

			return new LmsClassCostRollup
			{
				Currency = LmsTrainingCurrency.Normalize(sessions.Select(s => s.Currency).FirstOrDefault(c => LmsTrainingCurrency.Normalize(c) != null))
					?? LmsTrainingCurrency.Normalize(requests?.Currency) ?? await LmsProgramSettingsService.TrainingCurrencyAsync(_settings, departmentId),
				Rows = LmsClassCostMath.Rollup(sessions, lines, attendees, groupBy, requests?.Rows)
			};
		}

		#endregion Shift calendar and costs

		#region Helpers

		/// <summary>The member's own id (pickers post it upper-cased), or lms_class_not_member when they are not a current member.</summary>
		/// <summary>
		/// Round 10 A7 (owner decision 2026-11-26): enrolling oneself in a class and signing oneself in by its QR code are learner actions that
		/// need the member's LMS seat. A class manager or instructor placing, adding as a walk-in or marking a member needs their permission only.
		/// </summary>
		private async Task RequireLearnerSeatAsync(int departmentId, string userId)
		{
			if (!LmsLearnerSeats.Needed(false, userId))
				return;
			if (!await _gate.HoldsLearnerSeatAsync(departmentId, userId))
				throw new InvalidOperationException("lms_class_no_seat");
		}

		private async Task<string> RequireCurrentMemberAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				throw new InvalidOperationException("lms_class_not_member");
			var member = await _departments.GetDepartmentMemberAsync(userId.Trim(), departmentId, false);
			if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
				throw new InvalidOperationException("lms_class_not_member");
			return string.IsNullOrWhiteSpace(member.UserId) ? userId.Trim() : member.UserId;
		}

		private async Task<Dictionary<string, string>> NamesAsync(int departmentId) =>
			(await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private async Task<Dictionary<string, CalendarItemCheckIn>> CalendarCheckInsAsync(LmsClassSession session)
		{
			var map = new Dictionary<string, CalendarItemCheckIn>(StringComparer.OrdinalIgnoreCase);
			if (session?.CalendarItemId == null)
				return map;
			try
			{
				foreach (var checkIn in await _calendar.GetCheckInsByCalendarItemAsync(session.CalendarItemId.Value) ?? new List<CalendarItemCheckIn>())
					if (checkIn?.UserId != null && checkIn.DepartmentId == session.DepartmentId && !map.ContainsKey(checkIn.UserId))
						map[checkIn.UserId] = checkIn;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Calendar check-ins of a class session could not be read.");
			}
			return map;
		}

		private async Task NotifyAsync(int departmentId, IEnumerable<string> userIds, string titleKey, string messageKey, LmsClassSession session,
			CancellationToken cancellationToken)
		{
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				var local = DateTime.SpecifyKind(session.StartsOn, DateTimeKind.Utc).TimeConverter(department);
				await _notifier.SendAsync(departmentId, userIds, titleKey, messageKey, c => new object[] { session.Title, local.ToString("g", c) }, "CL:" + session.LmsClassSessionId.ToString(CultureInfo.InvariantCulture), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A class notification could not be sent.");
			}
		}

		/// <summary>Round 7B: the class e-mails of the external learners among <paramref name="userIds"/> (members are left to their notices). Non-fatal.</summary>
		private async Task MailLearnersAsync(LmsClassSession session, IEnumerable<string> userIds, LmsLearnerNoticeKinds kind, CancellationToken cancellationToken)
		{
			var learners = (userIds ?? Enumerable.Empty<string>()).Where(LmsExternalLearnerIds.IsExternal).ToList();
			if (learners.Count == 0 || _learnerMail == null)
				return;
			try
			{
				await _learnerMail.SendAsync(session.DepartmentId, session, learners, kind, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Learner class e-mails ({kind}) could not be sent.");
			}
		}

		/// <summary>Round 7B workflow triggers 240-243: ids, title, times and counts only (never a name or contact detail).</summary>
		private void Announce(TrainingClassWorkflowChanges change, LmsClassSession session, IReadOnlyCollection<LmsClassEnrollment> roster, string actingUserId,
			string promotedUserId = null)
		{
			if (session == null)
				return;
			var rows = roster ?? (IReadOnlyCollection<LmsClassEnrollment>)Array.Empty<LmsClassEnrollment>();
			_events?.SendMessage(new TrainingClassWorkflowEvent
			{
				DepartmentId = session.DepartmentId,
				Change = change,
				SessionId = session.LmsClassSessionId,
				Title = session.Title,
				CourseKind = ((LmsClassCourseKinds)session.CourseKind).ToString(),
				CourseId = session.LmsCourseId,
				TrainingId = session.TrainingId,
				StartsOn = session.StartsOn,
				EndsOn = session.EndsOn,
				Capacity = session.Capacity,
				Enrolled = rows.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled),
				Waitlisted = rows.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted),
				Attended = change == TrainingClassWorkflowChanges.SignedOff ? rows.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Attended) : 0,
				Absent = change == TrainingClassWorkflowChanges.SignedOff ? rows.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Absent) : 0,
				SeriesKey = session.SeriesKey,
				UserId = promotedUserId,
				IsExternalLearner = LmsExternalLearnerIds.IsExternal(promotedUserId),
				ActingUserId = actingUserId,
				OccurredOn = DateTime.UtcNow
			});
		}

		private static object Summary(LmsClassSession s) => new { s.LmsClassSessionId, s.Title, s.StartsOn, s.EndsOn, s.Capacity, s.EnrollmentOpen, s.RequiresApproval, s.Status };

		internal static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent
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
}
