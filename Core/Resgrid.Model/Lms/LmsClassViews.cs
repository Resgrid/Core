using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>What a class manager enters for a new session (or a series) and when editing one. Times are department-local.</summary>
	public sealed class LmsClassSessionInput
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public string ContentSummary { get; set; }
		public LmsClassCourseKinds CourseKind { get; set; }
		public int? LmsCourseId { get; set; }
		public int? TrainingId { get; set; }
		public int? LmsLessonPlanId { get; set; }
		public int? CategoryId { get; set; }
		public string CourseCode { get; set; }
		public DateTime LocalStart { get; set; }
		public DateTime LocalEnd { get; set; }
		public string Location { get; set; }
		public string MeetingUrl { get; set; }
		public int Capacity { get; set; }
		public bool WaitlistEnabled { get; set; }
		public bool EnrollmentOpen { get; set; }
		public bool RequiresApproval { get; set; }
		public int CutoffDays { get; set; }
		public bool AllowSelfUnenroll { get; set; }
		public List<int> RestrictGroupIds { get; set; } = new List<int>();
		public List<int> VisibleGroupIds { get; set; } = new List<int>();
		public string Tags { get; set; }
		public int ReminderHours { get; set; }
		public bool QrEnabled { get; set; }
		public bool GpsRequired { get; set; }
		public decimal? GpsLatitude { get; set; }
		public decimal? GpsLongitude { get; set; }
		public int GpsRadiusMeters { get; set; }
		public LmsClassPostEvaluationKinds PostClassEvaluationKind { get; set; }
		public string PostClassEvaluationRef { get; set; }
		/// <summary>Null: the category's retention years.</summary>
		public int? RetentionYears { get; set; }
		public string Currency { get; set; }
		/// <summary>A new session only: repeat it as a series.</summary>
		public LmsClassRepeatKinds Repeat { get; set; }
		public int Occurrences { get; set; } = 1;
		/// <summary>A new session only: the members who teach it.</summary>
		public List<string> InstructorUserIds { get; set; } = new List<string>();
	}

	/// <summary>One roster row as the session page shows it.</summary>
	public sealed class LmsClassRosterRow
	{
		public LmsClassEnrollment Enrollment { get; set; }
		public string Name { get; set; }
		/// <summary>On a shift that overlaps the class (the shift-calendar conflict flag).</summary>
		public bool OnShift { get; set; }
		/// <summary>The member is no longer active (left, disabled or hidden).</summary>
		public bool Inactive { get; set; }
		/// <summary>An external learner (T5), not a department member.</summary>
		public bool IsExternal { get; set; }
		/// <summary>A calendar check-in on the session's calendar item.</summary>
		public CalendarItemCheckIn CalendarCheckIn { get; set; }
		/// <summary>The hours a sign-off now would credit.</summary>
		public decimal ProjectedHours { get; set; }
	}

	/// <summary>A session with everything its page shows.</summary>
	public sealed class LmsClassSessionDetail
	{
		public LmsClassSession Session { get; set; }
		public List<LmsClassRosterRow> Roster { get; set; } = new List<LmsClassRosterRow>();
		public List<LmsClassInstructor> Instructors { get; set; } = new List<LmsClassInstructor>();
		public List<LmsClassCostLine> Costs { get; set; } = new List<LmsClassCostLine>();
		public List<LmsClassResourceBooking> Bookings { get; set; } = new List<LmsClassResourceBooking>();
		public LmsLessonPlanVersion LessonPlanVersion { get; set; }
		public CalendarItem CalendarItem { get; set; }
		public int SeatsTaken { get; set; }
		public decimal TotalCost => Costs.Sum(c => c.Amount);

		public int Count(LmsClassEnrollmentStatuses status) => Roster.Count(r => r.Enrollment.Status == (int)status);

		public int Count(LmsClassAttendanceStates state) =>
			Roster.Count(r => r.Enrollment.Status == (int)LmsClassEnrollmentStatuses.Enrolled && r.Enrollment.Attendance == (int)state);
	}

	/// <summary>A class as a member sees it on My classes.</summary>
	public sealed class LmsMyClass
	{
		public LmsClassSession Session { get; set; }
		/// <summary>The member's roster row, or null when they are not on it.</summary>
		public LmsClassEnrollment Enrollment { get; set; }
		public bool IsInstructor { get; set; }
		/// <summary>Why the member cannot enrol themselves (null: they can).</summary>
		public string EnrollRefusal { get; set; }
		public int SeatsTaken { get; set; }
	}

	public sealed class LmsAttendanceImportResult
	{
		public int Rows { get; set; }
		public int Matched { get; set; }
		public int WalkIns { get; set; }
		public List<LmsAttendanceImportRow> Unmatched { get; set; } = new List<LmsAttendanceImportRow>();
	}

	public sealed class LmsClassSignOffResult
	{
		public int Attended { get; set; }
		public int Absent { get; set; }
		public int Written { get; set; }
		public int Voided { get; set; }
		public int Unchanged { get; set; }
		public int InstructorRecords { get; set; }
		public int TimecardOffers { get; set; }
		public bool TimecardAvailable { get; set; }
		public int EvaluationsStarted { get; set; }
	}

	/// <summary>What one worker 95 run did.</summary>
	public sealed class LmsClassSweepResult
	{
		public int Reminders { get; set; }
		public int Promoted { get; set; }
		public int Purged { get; set; }
		/// <summary>Departments with due work whose LMS gate was closed (nothing sent or promoted).</summary>
		public int GateClosed { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>An upcoming class with enrolled members who are on shift at that time.</summary>
	public sealed class LmsClassShiftConflict
	{
		public LmsClassSession Session { get; set; }
		public int Enrolled { get; set; }
		public List<string> OnShiftUserIds { get; set; } = new List<string>();
	}

	public enum LmsClassCostGroupings
	{
		Course = 0,
		Member = 1,
		Year = 2
	}

	/// <summary>Class costs rolled up (R-C10), beside the training-request costs of the same grouping (round 4C).</summary>
	public sealed class LmsClassCostRollup
	{
		public string Currency { get; set; }
		public List<LmsClassCostRollupRow> Rows { get; set; } = new List<LmsClassCostRollupRow>();
		public decimal ClassTotal => Rows.Sum(r => r.ClassCost);
		public decimal RequestTotal => Rows.Sum(r => r.RequestCost);
	}

	public sealed class LmsClassCostRollupRow
	{
		/// <summary>The course title, the member id or the year.</summary>
		public string Key { get; set; }
		public int Sessions { get; set; }
		public int Attendees { get; set; }
		public decimal Hours { get; set; }
		public decimal ClassCost { get; set; }
		/// <summary>Training-request costs of the same course / member / year.</summary>
		public decimal RequestCost { get; set; }
	}

	/// <summary>The class cost arithmetic (R-C10), pure so it can be tested.</summary>
	public static class LmsClassCostMath
	{
		/// <summary>
		/// Rolls the cost lines of the sessions up per course (course code, else title), per member (each session's cost shared equally by
		/// its attendees) or per year (the class's year), and puts the training-request costs of the same key beside them.
		/// </summary>
		public static List<LmsClassCostRollupRow> Rollup(IEnumerable<LmsClassSession> sessions, IEnumerable<LmsClassCostLine> lines,
			IEnumerable<LmsClassEnrollment> attendees, LmsClassCostGroupings groupBy, IEnumerable<LmsTrainingRequestCostRow> requestRows)
		{
			var list = (sessions ?? Enumerable.Empty<LmsClassSession>()).Where(s => s != null && !s.IsDeleted && s.Status != (int)LmsClassSessionStatuses.Cancelled).ToList();
			var costs = (lines ?? Enumerable.Empty<LmsClassCostLine>()).Where(l => l != null).GroupBy(l => l.LmsClassSessionId).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
			var present = (attendees ?? Enumerable.Empty<LmsClassEnrollment>())
				.Where(e => e != null && e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Attended && !string.IsNullOrWhiteSpace(e.UserId))
				.GroupBy(e => e.LmsClassSessionId).ToDictionary(g => g.Key, g => g.ToList());
			var rows = new Dictionary<string, LmsClassCostRollupRow>(StringComparer.OrdinalIgnoreCase);

			LmsClassCostRollupRow Row(string key)
			{
				if (!rows.TryGetValue(key, out var row))
					rows[key] = row = new LmsClassCostRollupRow { Key = key };
				return row;
			}

			foreach (var session in list)
			{
				var cost = costs.TryGetValue(session.LmsClassSessionId, out var c) ? c : 0m;
				var here = present.TryGetValue(session.LmsClassSessionId, out var p) ? p : new List<LmsClassEnrollment>();
				if (groupBy == LmsClassCostGroupings.Member)
				{
					foreach (var member in here)
					{
						var row = Row(member.UserId.Trim().ToUpperInvariant());
						row.Sessions++;
						row.Attendees = 1;
						row.Hours += member.Hours ?? 0m;
						row.ClassCost += here.Count == 0 ? 0m : Math.Round(cost / here.Count, 2);
					}
					continue;
				}

				var key = groupBy == LmsClassCostGroupings.Year
					? session.StartsOn.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)
					: (string.IsNullOrWhiteSpace(session.CourseCode) ? (session.Title ?? string.Empty).Trim() : session.CourseCode.Trim()).ToUpperInvariant();
				var grouped = Row(key);
				grouped.Sessions++;
				grouped.Attendees += here.Count;
				grouped.Hours += here.Sum(e => e.Hours ?? 0m);
				grouped.ClassCost += cost;
			}

			foreach (var request in requestRows ?? Enumerable.Empty<LmsTrainingRequestCostRow>())
				if (request != null && !string.IsNullOrWhiteSpace(request.Key))
					Row(request.Key.Trim().ToUpperInvariant()).RequestCost += request.Committed;

			return rows.Values.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();
		}
	}
}
