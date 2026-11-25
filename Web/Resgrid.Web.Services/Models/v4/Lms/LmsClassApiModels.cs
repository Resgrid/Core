using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Training T4 part 1 (LMS add-on): what Omni uses for class sessions: my classes, enrolment, QR sign-in, and the roster for instructors.
	// Omni itself is out of scope for this build. All times are UTC; enum fields are names.

	/// <summary>A class session as a member sees it.</summary>
	public class LmsClassData
	{
		public int ClassSessionId { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public DateTime StartsOn { get; set; }
		public DateTime EndsOn { get; set; }
		public string Location { get; set; }
		public string MeetingUrl { get; set; }
		public int? CalendarItemId { get; set; }
		/// <summary>0 for no limit.</summary>
		public int Capacity { get; set; }
		public int SeatsTaken { get; set; }
		public bool WaitlistEnabled { get; set; }
		public bool QrEnabled { get; set; }
		public bool GpsRequired { get; set; }
		/// <summary>LmsClassSessionStatuses name: Scheduled, Cancelled, SignedOff.</summary>
		public string Status { get; set; }
		public bool IsInstructor { get; set; }
		/// <summary>The member's LmsClassEnrollmentStatuses name (PendingApproval, Enrolled, Waitlisted, Withdrawn, Declined, Removed); null when not on the roster.</summary>
		public string EnrollmentStatus { get; set; }
		public int? WaitlistPosition { get; set; }
		/// <summary>LmsClassAttendanceStates name: Unmarked, Attended, Absent, Excused.</summary>
		public string Attendance { get; set; }
		public DateTime? CheckInOn { get; set; }
		public DateTime? CheckOutOn { get; set; }
		public decimal? Hours { get; set; }
		/// <summary>Why the member cannot enrol themselves (lms_class_*), null when they can.</summary>
		public string EnrollRefusal { get; set; }
		public bool CanUnenroll { get; set; }
		public string Tags { get; set; }
	}

	public class LmsClassesResult : StandardApiResponseV4Base
	{
		public List<LmsClassData> Data { get; set; } = new List<LmsClassData>();
	}

	public class LmsClassResult : StandardApiResponseV4Base
	{
		public LmsClassData Data { get; set; }
		/// <summary>A refusal code (lms_class_*), when the action was refused.</summary>
		public string Error { get; set; }
	}

	/// <summary>One roster row for an instructor.</summary>
	public class LmsClassRosterRowData
	{
		public int EnrollmentId { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public string Status { get; set; }
		public string Attendance { get; set; }
		public bool IsWalkIn { get; set; }
		/// <summary>LmsClassCheckInMethods name: None, Manual, Qr, Import, Calendar.</summary>
		public string CheckInMethod { get; set; }
		public DateTime? CheckInOn { get; set; }
		public DateTime? CheckOutOn { get; set; }
		public decimal ProjectedHours { get; set; }
		public bool OnShift { get; set; }
		public int? WaitlistPosition { get; set; }
	}

	public class LmsClassRosterResult : StandardApiResponseV4Base
	{
		public LmsClassData Session { get; set; }
		public List<LmsClassRosterRowData> Data { get; set; } = new List<LmsClassRosterRowData>();
		public string Error { get; set; }
	}

	/// <summary>The rotating sign-in code an instructor's device shows as a QR (it changes every 30 seconds).</summary>
	public class LmsClassCodeResult : StandardApiResponseV4Base
	{
		public bool Open { get; set; }
		public string Code { get; set; }
		/// <summary>The web sign-in URL the QR encodes.</summary>
		public string SignInUrl { get; set; }
		public int SecondsPerCode { get; set; }
	}

	public class LmsClassSessionRef
	{
		public int ClassSessionId { get; set; }
	}

	public class LmsClassSignInInput
	{
		public int ClassSessionId { get; set; }
		public string Code { get; set; }
		/// <summary>Optional; required only when the session asks for GPS.</summary>
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
	}

	public class LmsClassAttendanceInput
	{
		public int ClassSessionId { get; set; }
		public int EnrollmentId { get; set; }
		/// <summary>LmsClassAttendanceStates name or value: Attended, Absent, Excused, Unmarked.</summary>
		public string Attendance { get; set; }
		public decimal? Hours { get; set; }
	}

	public class LmsClassSignOffApiResult : StandardApiResponseV4Base
	{
		public int Attended { get; set; }
		public int Absent { get; set; }
		public int RecordsWritten { get; set; }
		public int RecordsVoided { get; set; }
		public string Error { get; set; }
	}
}
