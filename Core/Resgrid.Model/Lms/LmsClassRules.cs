using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// The class-session rules (§13 T4 item 1, §13a T4 Events), pure so they can be tested: enrolment (open / closed, capacity, cutoff,
	/// groups, approval, self-unenrol), the waitlist order, visibility, rotating-QR and GPS sign-in, hours, retention, the sign-off drafts
	/// and the shift-calendar conflict flag. Refusals are "lms_class_*" codes the pages show as text.
	/// </summary>
	public static class LmsClassRules
	{
		public const int MaxGpsRadiusMeters = 50000;
		public const int DefaultGpsRadiusMeters = 300;
		public const int MaxReminderHours = 24 * 14;
		public const int MaxCutoffDays = 365;
		public const string InstructorComponent = "instructor";

		#region Lists

		public static List<int> ParseIds(string csv) =>
			(csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();

		public static string JoinIds(IEnumerable<int> ids)
		{
			var list = (ids ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
			return list.Count == 0 ? null : string.Join(",", list.Select(i => i.ToString(CultureInfo.InvariantCulture)));
		}

		public static List<string> ParseTags(string csv) =>
			(csv ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0)
				.Select(t => t.Length > 50 ? t.Substring(0, 50) : t).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();

		public static string NormalizeTags(string csv)
		{
			var tags = ParseTags(csv);
			var joined = string.Join(", ", tags);
			return joined.Length == 0 ? null : joined.Length > LmsClassSession.TagsMaxLength ? joined.Substring(0, LmsClassSession.TagsMaxLength) : joined;
		}

		#endregion Lists

		#region Enrolment and the waitlist

		/// <summary>Seats taken: enrolled members (walk-ins included).</summary>
		public static int SeatsTaken(IEnumerable<LmsClassEnrollment> enrollments) =>
			(enrollments ?? Enumerable.Empty<LmsClassEnrollment>()).Count(e => e != null && e.Status == (int)LmsClassEnrollmentStatuses.Enrolled);

		public static bool IsFull(LmsClassSession session, int seatsTaken) => session != null && session.Capacity > 0 && seatsTaken >= session.Capacity;

		/// <summary>Whether a member may see the session (managers always do): no visibility groups, or the member's group is one of them.</summary>
		public static bool IsVisibleTo(LmsClassSession session, int? memberGroupId, bool manager)
		{
			if (session == null || session.IsDeleted)
				return false;
			if (manager)
				return true;
			var groups = session.VisibleGroups();
			return groups.Count == 0 || (memberGroupId.HasValue && groups.Contains(memberGroupId.Value));
		}

		/// <summary>Why a member cannot enrol themselves now; null when they can.</summary>
		public static string SelfEnrollRefusal(LmsClassSession session, DateTime utcNow, int? memberGroupId)
		{
			if (session == null || session.IsDeleted)
				return "lms_class_not_found";
			if (session.Status != (int)LmsClassSessionStatuses.Scheduled || session.StartsOn <= utcNow)
				return "lms_class_not_open";
			if (!session.EnrollmentOpen)
				return "lms_class_enrollment_closed";
			if (session.CutoffDays > 0 && utcNow > session.StartsOn.AddDays(-session.CutoffDays))
				return "lms_class_enrollment_cutoff";
			if (!IsVisibleTo(session, memberGroupId, false))
				return "lms_class_not_eligible";
			var restrict = session.RestrictGroups();
			if (restrict.Count > 0 && (!memberGroupId.HasValue || !restrict.Contains(memberGroupId.Value)))
				return "lms_class_not_eligible";
			return null;
		}

		/// <summary>
		/// Where a new enrolment lands: pending approval (self-enrolment on an approval session), enrolled when a seat is free, waitlisted
		/// when full and the waitlist is on; null when full without a waitlist (refused). A class manager's enrolment skips approval and,
		/// with no waitlist, may go over capacity (their call).
		/// </summary>
		public static LmsClassEnrollmentStatuses? Placement(LmsClassSession session, int seatsTaken, bool byManager)
		{
			if (session == null)
				return null;
			if (!byManager && session.RequiresApproval)
				return LmsClassEnrollmentStatuses.PendingApproval;
			if (!IsFull(session, seatsTaken))
				return LmsClassEnrollmentStatuses.Enrolled;
			if (session.WaitlistEnabled)
				return LmsClassEnrollmentStatuses.Waitlisted;
			return byManager ? LmsClassEnrollmentStatuses.Enrolled : (LmsClassEnrollmentStatuses?)null;
		}

		/// <summary>Why a member cannot withdraw themselves; null when they can.</summary>
		public static string SelfUnenrollRefusal(LmsClassSession session, LmsClassEnrollment enrollment, DateTime utcNow)
		{
			if (session == null || enrollment == null || !enrollment.IsOnRoster)
				return "lms_class_not_enrolled";
			if (session.Status != (int)LmsClassSessionStatuses.Scheduled || session.StartsOn <= utcNow)
				return "lms_class_not_open";
			// A waitlisted or pending member may always leave the queue; an enrolled one only when the session allows it.
			if (enrollment.Status == (int)LmsClassEnrollmentStatuses.Enrolled && !session.AllowSelfUnenroll)
				return "lms_class_unenroll_closed";
			return null;
		}

		/// <summary>The next waitlist position.</summary>
		public static int NextWaitlistPosition(IEnumerable<LmsClassEnrollment> enrollments) =>
			(enrollments ?? Enumerable.Empty<LmsClassEnrollment>()).Where(e => e != null && e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted)
				.Select(e => e.WaitlistPosition ?? 0).DefaultIfEmpty(0).Max() + 1;

		/// <summary>
		/// The waitlisted members to promote now, in waitlist order: as many as there are free seats, only before the start, and only
		/// active members (a member who left, was disabled or hidden is passed over, not promoted; inactive-member-automation-policy).
		/// </summary>
		public static List<LmsClassEnrollment> PromotionOrder(LmsClassSession session, IEnumerable<LmsClassEnrollment> enrollments, Func<string, bool> isActive, DateTime utcNow)
		{
			var all = (enrollments ?? Enumerable.Empty<LmsClassEnrollment>()).Where(e => e != null).ToList();
			if (session == null || !session.IsScheduled || session.StartsOn <= utcNow || !session.WaitlistEnabled)
				return new List<LmsClassEnrollment>();

			var waiting = all.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted && (isActive == null || isActive(e.UserId)))
				.OrderBy(e => e.WaitlistPosition ?? int.MaxValue).ThenBy(e => e.EnrolledOn).ThenBy(e => e.LmsClassEnrollmentId).ToList();
			if (session.Capacity <= 0)
				return waiting;
			var free = session.Capacity - SeatsTaken(all);
			return free <= 0 ? new List<LmsClassEnrollment>() : waiting.Take(free).ToList();
		}

		#endregion Enrolment and the waitlist

		#region QR and GPS sign-in

		/// <summary>The current rotating code (null when the session does not use QR sign-in or is not open for sign-in).</summary>
		public static string CurrentCode(LmsClassSession session, DateTime utcNow) =>
			IsSignInOpen(session, utcNow) ? LmsCrewCodes.CurrentCode(session.QrSecret, utcNow) : null;

		/// <summary>Sign-in opens an hour before the start and closes two hours after the end.</summary>
		public static bool IsSignInOpen(LmsClassSession session, DateTime utcNow) =>
			session != null && session.IsScheduled && session.QrEnabled && !string.IsNullOrEmpty(session.QrSecret) &&
			utcNow >= session.StartsOn.AddHours(-1) && utcNow <= session.EndsOn.AddHours(2);

		/// <summary>
		/// Why a QR sign-in is refused: closed, a code that is not the current (or the immediately previous) slice's — a photo of the QR
		/// taken earlier is refused — or, when the session requires it, no position or one outside the radius. GPS is optional otherwise.
		/// </summary>
		public static string SignInRefusal(LmsClassSession session, string code, DateTime utcNow, decimal? latitude, decimal? longitude)
		{
			if (session == null || session.IsDeleted)
				return "lms_class_not_found";
			if (!IsSignInOpen(session, utcNow))
				return "lms_class_sign_in_closed";
			if (!LmsCrewCodes.IsValid(session.QrSecret, code, utcNow))
				return "lms_class_code_expired";
			if (session.GpsRequired)
			{
				if (!latitude.HasValue || !longitude.HasValue || !IsValidPosition(latitude.Value, longitude.Value))
					return "lms_class_gps_required";
				if (session.GpsLatitude.HasValue && session.GpsLongitude.HasValue &&
					DistanceMeters(latitude.Value, longitude.Value, session.GpsLatitude.Value, session.GpsLongitude.Value) > Math.Max(10, session.GpsRadiusMeters))
					return "lms_class_gps_too_far";
			}
			return null;
		}

		public static bool IsValidPosition(decimal latitude, decimal longitude) =>
			latitude >= -90m && latitude <= 90m && longitude >= -180m && longitude <= 180m && !(latitude == 0m && longitude == 0m);

		/// <summary>Great-circle distance in metres (haversine).</summary>
		public static double DistanceMeters(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
		{
			const double R = 6371000d;
			double Rad(decimal d) => (double)d * Math.PI / 180d;
			var dLat = Rad(lat2 - lat1);
			var dLon = Rad(lon2 - lon1);
			var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
			return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
		}

		#endregion QR and GPS sign-in

		#region Hours, retention and sign-off

		/// <summary>The session key the transcript counts once (R-T2): the calendar item's, so a calendar check-in of the same class is not counted twice.</summary>
		public static string SessionKey(LmsClassSession session) =>
			session?.CalendarItemId != null ? "calendar:" + session.CalendarItemId.Value.ToString(CultureInfo.InvariantCulture)
				: "class:" + (session?.LmsClassSessionId ?? 0).ToString(CultureInfo.InvariantCulture);

		/// <summary>
		/// An attendee's hours: an instructor's override; else check-in to check-out (a calendar check-in when there is no QR one); else the
		/// minutes of an imported meeting report; else from the check-in to the end; else the session's length. Rounded per the department's
		/// training-record setting (R-T9).
		/// </summary>
		public static decimal HoursFor(LmsClassSession session, LmsClassEnrollment enrollment, CalendarItemCheckIn calendarCheckIn, TrainingRecordSettings settings)
		{
			settings ??= new TrainingRecordSettings();
			if (enrollment?.HoursOverride != null)
				return Math.Round(Math.Clamp(enrollment.HoursOverride.Value, 0m, 9999m), 2);

			var checkIn = enrollment?.CheckInOn ?? calendarCheckIn?.CheckInTime;
			var checkOut = enrollment?.CheckInOn != null ? enrollment.CheckOutOn : calendarCheckIn?.CheckOutTime;
			if (checkIn.HasValue && checkOut.HasValue && checkOut.Value > checkIn.Value)
				return settings.RoundHours(checkOut.Value - checkIn.Value);
			if (enrollment?.ImportedMinutes is int minutes && minutes > 0)
				return settings.RoundHours(TimeSpan.FromMinutes(minutes));
			if (session == null)
				return 0m;
			if (checkIn.HasValue && session.EndsOn > checkIn.Value)
				return settings.RoundHours(session.EndsOn - (checkIn.Value > session.StartsOn ? checkIn.Value : session.StartsOn));
			return session.EndsOn > session.StartsOn ? settings.RoundHours(session.EndsOn - session.StartsOn) : 0m;
		}

		/// <summary>The date before which the roster may not be purged: the class end plus the retention years (none: no hold).</summary>
		public static DateTime? RetainUntil(DateTime endsOnUtc, int? retentionYears) =>
			retentionYears.HasValue && retentionYears.Value > 0 ? endsOnUtc.AddYears(Math.Min(retentionYears.Value, 75)) : (DateTime?)null;

		/// <summary>The roster may be purged (worker 95) only once deleted and past its retention (OSHA 1910.1030: 3 years, for example).</summary>
		public static bool CanPurge(LmsClassSession session, DateTime utcNow) =>
			session != null && session.IsDeleted && (!session.RetainUntil.HasValue || session.RetainUntil.Value <= utcNow);

		/// <summary>
		/// The learning records a sign-off writes (kind ClassSession, source ref = the session id): one per attendee who attended (never for
		/// the absent, the excused or the unmarked) and one per member instructor (component "instructor", the hours taught).
		/// </summary>
		public static List<LearningRecordDraft> SignOffDrafts(LmsClassSession session, IEnumerable<LmsClassEnrollment> roster, IReadOnlyDictionary<int, decimal> hours,
			IEnumerable<LmsClassInstructor> instructors, IEnumerable<string> objectives, string signerUserId)
		{
			var drafts = new List<LearningRecordDraft>();
			if (session == null)
				return drafts;

			var teachers = (instructors ?? Enumerable.Empty<LmsClassInstructor>()).Where(i => i != null).OrderBy(i => i.SortOrder).ThenBy(i => i.LmsClassInstructorId).ToList();
			var instructorIds = string.Join(",", teachers.Where(i => i.IsMember).Select(i => i.UserId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
			var instructorNames = string.Join(", ", teachers.Select(i => i.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
			var objectiveText = string.Join("; ", (objectives ?? Enumerable.Empty<string>()).Where(o => !string.IsNullOrWhiteSpace(o)));
			var key = SessionKey(session);
			var method = string.IsNullOrWhiteSpace(session.MeetingUrl) ? (int)MethodsOfTraining.Classroom
				: string.IsNullOrWhiteSpace(session.Location) ? (int)MethodsOfTraining.Virtual : (int)MethodsOfTraining.Classroom;

			foreach (var enrollment in (roster ?? Enumerable.Empty<LmsClassEnrollment>()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.UserId) &&
				e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Attended))
			{
				var start = enrollment.CheckInOn ?? session.StartsOn;
				var end = enrollment.CheckOutOn ?? session.EndsOn;
				drafts.Add(new LearningRecordDraft
				{
					UserId = enrollment.UserId.Trim(),
					ComponentKey = string.Empty,
					SessionKey = key,
					Title = session.Title,
					CourseCode = session.CourseCode,
					CompletedOn = end,
					StartedOn = start,
					EndedOn = end > start ? end : session.EndsOn,
					Hours = hours != null && hours.TryGetValue(enrollment.LmsClassEnrollmentId, out var h) ? h : 0m,
					CategoryId = session.CategoryId,
					MethodOfTraining = method,
					Objectives = string.IsNullOrWhiteSpace(objectiveText) ? null : objectiveText,
					Location = session.Location,
					InstructorUserIds = string.IsNullOrWhiteSpace(instructorIds) ? null : instructorIds,
					InstructorNames = string.IsNullOrWhiteSpace(instructorNames) ? null : instructorNames,
					Tags = session.Tags,
					Status = LearningRecordStatuses.Verified,
					VerifiedByUserId = signerUserId
				});
			}

			var length = session.EndsOn > session.StartsOn ? Math.Round((decimal)(session.EndsOn - session.StartsOn).TotalHours, 2) : 0m;
			foreach (var teacher in teachers.Where(i => i.IsMember).GroupBy(i => i.UserId.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
			{
				drafts.Add(new LearningRecordDraft
				{
					UserId = teacher.UserId.Trim(),
					ComponentKey = InstructorComponent,
					SessionKey = key,
					Title = session.Title,
					CourseCode = session.CourseCode,
					CompletedOn = session.EndsOn,
					StartedOn = session.StartsOn,
					EndedOn = session.EndsOn,
					Hours = Math.Round(Math.Clamp(teacher.HoursTaught ?? length, 0m, 9999m), 2),
					CategoryId = session.CategoryId,
					MethodOfTraining = method,
					Location = session.Location,
					InstructorUserIds = string.IsNullOrWhiteSpace(instructorIds) ? null : instructorIds,
					InstructorNames = string.IsNullOrWhiteSpace(instructorNames) ? null : instructorNames,
					Tags = string.IsNullOrWhiteSpace(session.Tags) ? InstructorComponent : InstructorComponent + ", " + session.Tags,
					Notes = "instructor",
					Status = LearningRecordStatuses.Verified,
					VerifiedByUserId = signerUserId
				});
			}

			return drafts;
		}

		#endregion Hours, retention and sign-off

		#region Shift calendar and series

		/// <summary>
		/// The members who are on a shift (on the day's roster, not waiting for approval) whose window overlaps the class's department-local
		/// window: the shift-calendar conflict flag.
		/// </summary>
		public static HashSet<string> ShiftConflicts(DateTime localStart, DateTime localEnd, IEnumerable<ShiftDaySchedule> schedules, IEnumerable<string> userIds)
		{
			var wanted = new HashSet<string>((userIds ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)), StringComparer.OrdinalIgnoreCase);
			var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (wanted.Count == 0 || localEnd <= localStart)
				return conflicts;

			foreach (var schedule in (schedules ?? Enumerable.Empty<ShiftDaySchedule>()).Where(s => s?.Day != null))
			{
				if (schedule.Day.Shift == null && schedule.Shift != null)
					schedule.Day.Shift = schedule.Shift;
				DateTime start, end;
				try
				{
					start = schedule.Day.Start;
					end = schedule.Day.End;
				}
				catch (Exception)
				{
					continue;
				}
				if (!(start < localEnd && end > localStart))
					continue;
				foreach (var entry in (schedule.Roster ?? new List<ShiftDayRosterEntry>()).Where(r => r != null && !r.ApprovalPending && wanted.Contains(r.UserId ?? string.Empty)))
					conflicts.Add(entry.UserId);
			}

			return conflicts;
		}

		/// <summary>The department-local start and end of each occurrence of a new series (1 to 52).</summary>
		public static List<(DateTime Start, DateTime End)> Occurrences(DateTime localStart, DateTime localEnd, LmsClassRepeatKinds repeat, int count)
		{
			var length = localEnd - localStart;
			var n = repeat == LmsClassRepeatKinds.None ? 1 : Math.Clamp(count, 1, LmsClassSession.MaxOccurrences);
			var step = repeat == LmsClassRepeatKinds.Weekly ? 7 : 1;
			return Enumerable.Range(0, n).Select(i => (localStart.AddDays(i * step), localStart.AddDays(i * step).Add(length))).ToList();
		}

		#endregion Shift calendar and series

		/// <summary>SHA-256, lower-case hex.</summary>
		public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).ToLowerInvariant();
	}

	/// <summary>One person's row of an imported meeting attendance report.</summary>
	public sealed class LmsAttendanceImportRow
	{
		public string Name { get; set; }
		public string Email { get; set; }
		public int Minutes { get; set; }
		public int Line { get; set; }
	}

	/// <summary>
	/// Reads the attendance reports Teams, Zoom and Google Meet export (CSV or tab separated; Teams puts a summary block above the
	/// participant table): finds the participant header, then name, e-mail and duration (or join and leave times) per row. A person who
	/// joined several times is one row with the minutes added up.
	/// </summary>
	public static class LmsAttendanceImport
	{
		public const int MaxRows = 5000;
		private static readonly Regex DurationPart = new Regex(@"(\d+(?:[.,]\d+)?)\s*(h|hr|hrs|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)\b",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		public static List<LmsAttendanceImportRow> Parse(string text)
		{
			var rows = new List<LmsAttendanceImportRow>();
			if (string.IsNullOrWhiteSpace(text))
				return rows;

			var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			int headerIndex = -1;
			char delimiter = ',';
			string[] header = null;
			for (var i = 0; i < lines.Length && i < 200; i++)
			{
				var candidate = lines[i];
				if (string.IsNullOrWhiteSpace(candidate))
					continue;
				var d = Delimiter(candidate);
				var cells = Split(candidate, d).Select(c => c.Trim().ToLowerInvariant()).ToArray();
				var hasName = cells.Any(IsNameHeader) || (cells.Any(c => c.Contains("first name")) && cells.Any(c => c.Contains("last name")));
				var hasTime = cells.Any(c => c.Contains("duration") || c.Contains("join") || c.Contains("time in") || c.Contains("attendance"));
				if (hasName && hasTime)
				{
					headerIndex = i;
					delimiter = d;
					header = cells;
					break;
				}
			}
			if (header == null)
				return rows;

			int Find(Func<string, bool> match) => Array.FindIndex(header, c => match(c));
			var name = Find(IsNameHeader);
			var first = Find(c => c.Contains("first name"));
			var last = Find(c => c.Contains("last name"));
			var email = Find(c => c.Contains("email") || c.Contains("e-mail") || c.Contains("upn"));
			var duration = Find(c => c.Contains("duration") || c.Contains("time in meeting") || c.Contains("attendance duration"));
			var minutesHeader = duration >= 0 && (header[duration].Contains("min"));
			var join = Find(c => c.Contains("join") || c.Contains("time joined"));
			var leave = Find(c => c.Contains("leave") || c.Contains("left") || c.Contains("exit"));

			var byPerson = new Dictionary<string, LmsAttendanceImportRow>(StringComparer.OrdinalIgnoreCase);
			for (var i = headerIndex + 1; i < lines.Length && byPerson.Count < MaxRows; i++)
			{
				var line = lines[i];
				if (string.IsNullOrWhiteSpace(line))
				{
					// Teams: the participant table ends at the next section.
					if (byPerson.Count > 0)
						break;
					continue;
				}
				var cells = Split(line, delimiter);
				string Cell(int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : null;

				var person = Cell(name);
				if (string.IsNullOrWhiteSpace(person) && (first >= 0 || last >= 0))
					person = ((Cell(first) ?? string.Empty) + " " + (Cell(last) ?? string.Empty)).Trim();
				var mail = Cell(email);
				if (string.IsNullOrWhiteSpace(mail) || !mail.Contains('@'))
					mail = null;
				if (string.IsNullOrWhiteSpace(person) && string.IsNullOrWhiteSpace(mail))
					continue;
				// A Zoom display name carries the original name in brackets: "Jane (Jane Doe)".
				if (person != null)
				{
					var bracket = person.IndexOf(" (", StringComparison.Ordinal);
					if (bracket > 0 && person.EndsWith(")", StringComparison.Ordinal))
						person = person.Substring(bracket + 2, person.Length - bracket - 3).Trim();
				}

				var minutes = ParseMinutes(Cell(duration), minutesHeader);
				if (minutes <= 0 && TryTime(Cell(join), out var joined) && TryTime(Cell(leave), out var left) && left > joined)
					minutes = (int)Math.Round((left - joined).TotalMinutes);

				var key = !string.IsNullOrWhiteSpace(mail) ? mail.Trim() : person.Trim();
				if (byPerson.TryGetValue(key, out var existing))
					existing.Minutes += Math.Max(0, minutes);
				else
					byPerson[key] = new LmsAttendanceImportRow { Name = person, Email = mail, Minutes = Math.Max(0, minutes), Line = i + 1 };
			}

			rows.AddRange(byPerson.Values.OrderBy(r => r.Line));
			return rows;
		}

		private static bool IsNameHeader(string c) =>
			c == "name" || c.StartsWith("name ", StringComparison.Ordinal) || c.StartsWith("name(", StringComparison.Ordinal) || c.Contains("full name") ||
			c == "participant" || c == "participant name" || c == "attendee" || c == "display name" || c == "user name";

		/// <summary>"65", "1h 5m 3s", "1 hr 5 min", "45 minutes", "01:05:00" (h:mm:ss) or "1:05" (h:mm).</summary>
		public static int ParseMinutes(string value, bool numberIsMinutes = true)
		{
			var text = (value ?? string.Empty).Trim();
			if (text.Length == 0)
				return 0;
			if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
				return number <= 0 ? 0 : (int)Math.Round(numberIsMinutes ? number : number);

			if (text.Contains(':'))
			{
				var parts = text.Split(':');
				if (parts.All(p => int.TryParse(p.Trim(), out _)))
				{
					var values = parts.Select(p => int.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
					if (values.Length == 3)
						return (int)Math.Round(values[0] * 60 + values[1] + values[2] / 60d);
					if (values.Length == 2)
						return values[0] * 60 + values[1];
				}
			}

			double total = 0;
			foreach (Match match in DurationPart.Matches(text))
			{
				if (!double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
					continue;
				var unit = match.Groups[2].Value.ToLowerInvariant();
				total += unit.StartsWith("h", StringComparison.Ordinal) ? amount * 60 : unit.StartsWith("s", StringComparison.Ordinal) ? amount / 60 : amount;
			}
			return (int)Math.Round(total);
		}

		private static bool TryTime(string value, out DateTime time) =>
			DateTime.TryParse(value ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time) ||
			DateTime.TryParse(value ?? string.Empty, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out time);

		private static char Delimiter(string line)
		{
			var tabs = line.Count(c => c == '\t');
			var commas = line.Count(c => c == ',');
			var semis = line.Count(c => c == ';');
			return tabs >= commas && tabs >= semis && tabs > 0 ? '\t' : semis > commas ? ';' : ',';
		}

		/// <summary>Splits one line, honouring double-quoted cells.</summary>
		public static string[] Split(string line, char delimiter)
		{
			var cells = new List<string>();
			var current = new StringBuilder();
			var quoted = false;
			for (var i = 0; i < line.Length; i++)
			{
				var ch = line[i];
				if (ch == '"')
				{
					if (quoted && i + 1 < line.Length && line[i + 1] == '"')
					{
						current.Append('"');
						i++;
					}
					else
						quoted = !quoted;
				}
				else if (ch == delimiter && !quoted)
				{
					cells.Add(current.ToString());
					current.Clear();
				}
				else
					current.Append(ch);
			}
			cells.Add(current.ToString());
			return cells.Select(c => c.Trim().Trim((char)0xFEFF)).ToArray();
		}
	}
}
