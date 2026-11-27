using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	// Training T4 part 1 (round 5B): class sessions on the Calendar with enrolment rules, waitlist, roster, rotating-QR and GPS sign-in,
	// instructor sign-off into the transcript, the OSHA-grade roster with per-category retention, instructor records, lesson plans on the
	// content core (R-C6), training time and cost (R-C10), and facility / prop booking. LMS add-on (§17.2): LMS tables reference baseline
	// tables by id only (no foreign keys); baseline tables never reference them (§17.3). Persisted enums are append only.

	#region Enums

	/// <summary>What a class session teaches.</summary>
	public enum LmsClassCourseKinds
	{
		/// <summary>A stand-alone class (its own title).</summary>
		None = 0,
		/// <summary>An LMS course (T2).</summary>
		Course = 1,
		/// <summary>A baseline training.</summary>
		Training = 2
	}

	public enum LmsClassSessionStatuses
	{
		Scheduled = 0,
		Cancelled = 1,
		/// <summary>The instructor signed the roster off: learning records were written for every attendee.</summary>
		SignedOff = 2
	}

	public enum LmsClassEnrollmentStatuses
	{
		/// <summary>The session needs approval: waiting for a class manager.</summary>
		PendingApproval = 0,
		Enrolled = 1,
		/// <summary>The session was full: promoted in order when a seat opens (worker 95 and every withdrawal).</summary>
		Waitlisted = 2,
		/// <summary>The member withdrew (self-unenrol).</summary>
		Withdrawn = 3,
		/// <summary>A class manager declined the request.</summary>
		Declined = 4,
		/// <summary>A class manager removed the member from the roster.</summary>
		Removed = 5
	}

	public enum LmsClassAttendanceStates
	{
		Unmarked = 0,
		Attended = 1,
		Absent = 2,
		/// <summary>Did not attend with a reason; never earns a record.</summary>
		Excused = 3
	}

	public enum LmsClassCheckInMethods
	{
		None = 0,
		/// <summary>An instructor marked it.</summary>
		Manual = 1,
		/// <summary>The member scanned the session's rotating QR.</summary>
		Qr = 2,
		/// <summary>A meeting attendance report (Teams, Zoom, Meet) was imported.</summary>
		Import = 3,
		/// <summary>A check-in on the session's calendar item.</summary>
		Calendar = 4
	}

	public enum LmsClassInstructorRoles
	{
		Lead = 0,
		Assistant = 1,
		/// <summary>A safety officer (live-fire evolutions).</summary>
		Safety = 2
	}

	public enum LmsClassCostKinds
	{
		Instructor = 0,
		Materials = 1,
		OutsideFees = 2,
		BackfillOvertime = 3,
		Other = 4
	}

	public enum LmsTrainingResourceKinds
	{
		Facility = 0,
		Prop = 1,
		Apparatus = 2,
		Equipment = 3,
		Other = 4
	}

	/// <summary>
	/// The evaluation that follows a class (§13 T4 item 1). CourseAssessment assigns the linked LMS course (its quiz) to every attendee at
	/// sign-off. SkillSheet is the seam the skill-sheet stream (T4 part 2) fills through <c>ILmsPostClassEvaluation</c>.
	/// </summary>
	public enum LmsClassPostEvaluationKinds
	{
		None = 0,
		CourseAssessment = 1,
		SkillSheet = 2
	}

	public enum LmsClassRepeatKinds
	{
		None = 0,
		Daily = 1,
		Weekly = 2
	}

	#endregion Enums

	#region Class sessions

	/// <summary>
	/// A class session (§13 T4 item 1): a calendar item with a course link, instructors, capacity, waitlist, enrolment rules, a rotating QR
	/// and optional GPS sign-in, and on sign-off the OSHA-grade roster (dates, content summary, trainers and qualifications, attendees and
	/// job titles) kept for its category's retention.
	/// </summary>
	[Table("LmsClassSessions")]
	public class LmsClassSession : IEntity
	{
		public const int TitleMaxLength = 250;
		public const int TextMaxLength = 4000;
		public const int LocationMaxLength = 500;
		public const int UrlMaxLength = 1000;
		public const int IdsMaxLength = 1000;
		public const int TagsMaxLength = 500;
		public const int MaxCapacity = 10000;
		public const int MaxOccurrences = 52;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsClassSessionId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The calendar item the session sits on.</summary>
		public int? CalendarItemId { get; set; }

		/// <summary>Sessions created together as a series share this key.</summary>
		public string SeriesKey { get; set; }

		public int SeriesIndex { get; set; }

		public string Title { get; set; }

		/// <summary>What members read before enrolling (plain text).</summary>
		public string Description { get; set; }

		/// <summary>The roster's content summary (OSHA: what was covered), plain text.</summary>
		public string ContentSummary { get; set; }

		/// <summary><see cref="LmsClassCourseKinds"/>.</summary>
		public int CourseKind { get; set; }

		public int? LmsCourseId { get; set; }

		public int? TrainingId { get; set; }

		public int? LmsLessonPlanId { get; set; }

		/// <summary>The lesson plan version the class was taught from (frozen when the session was saved).</summary>
		public int? LmsLessonPlanVersionId { get; set; }

		public int? CategoryId { get; set; }

		public string CourseCode { get; set; }

		/// <summary>UTC.</summary>
		public DateTime StartsOn { get; set; }

		/// <summary>UTC.</summary>
		public DateTime EndsOn { get; set; }

		public string Location { get; set; }

		/// <summary>A Teams, Zoom or Meet link for a virtual or hybrid class.</summary>
		public string MeetingUrl { get; set; }

		/// <summary>Seats; 0 for no limit.</summary>
		public int Capacity { get; set; }

		public bool WaitlistEnabled { get; set; }

		/// <summary>Members may enrol themselves.</summary>
		public bool EnrollmentOpen { get; set; }

		/// <summary>A self-enrolment waits for a class manager's approval.</summary>
		public bool RequiresApproval { get; set; }

		/// <summary>Self-enrolment closes this many days before the start (0: until the start).</summary>
		public int CutoffDays { get; set; }

		public bool AllowSelfUnenroll { get; set; }

		/// <summary>Comma separated group ids whose members may enrol themselves; empty for everyone.</summary>
		public string RestrictGroupIds { get; set; }

		/// <summary>Comma separated group ids whose members see the session; empty for everyone.</summary>
		public string VisibleGroupIds { get; set; }

		/// <summary>Comma separated tags (filters and reports).</summary>
		public string Tags { get; set; }

		/// <summary>Hours before the start enrolled members are reminded (0: no reminder).</summary>
		public int ReminderHours { get; set; }

		public bool QrEnabled { get; set; }

		/// <summary>The rotating code's secret (HMAC slices, <see cref="LmsCrewCodes"/>).</summary>
		public string QrSecret { get; set; }

		/// <summary>QR sign-in needs the phone's position within the radius.</summary>
		public bool GpsRequired { get; set; }

		public decimal? GpsLatitude { get; set; }

		public decimal? GpsLongitude { get; set; }

		public int GpsRadiusMeters { get; set; }

		/// <summary><see cref="LmsClassPostEvaluationKinds"/>.</summary>
		public int PostClassEvaluationKind { get; set; }

		/// <summary>What the evaluation points at (a skill sheet id for the T4 part 2 seam).</summary>
		public string PostClassEvaluationRef { get; set; }

		/// <summary><see cref="LmsClassSessionStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime? CancelledOn { get; set; }

		public string CancelReason { get; set; }

		public DateTime? SignedOffOn { get; set; }

		public string SignedOffByUserId { get; set; }

		/// <summary>Years the roster is kept after the class (from the category, or set on the session).</summary>
		public int? RetentionYears { get; set; }

		/// <summary>The roster cannot be purged before this date (UTC).</summary>
		public DateTime? RetainUntil { get; set; }

		/// <summary>ISO 4217 of the cost lines.</summary>
		public string Currency { get; set; }

		/// <summary>The annual plan line this session was made from (round 4C seam).</summary>
		public int? LmsTrainingPlanItemId { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime? DeletedOn { get; set; }

		public string DeletedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsScheduled => Status == (int)LmsClassSessionStatuses.Scheduled && !IsDeleted;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsClassSessionId; }
			set { LmsClassSessionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsClassSessions";

		[NotMapped]
		public string IdName => "LmsClassSessionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsScheduled" };

		public List<int> RestrictGroups() => LmsClassRules.ParseIds(RestrictGroupIds);

		public List<int> VisibleGroups() => LmsClassRules.ParseIds(VisibleGroupIds);

		public List<string> TagList() => LmsClassRules.ParseTags(Tags);
	}

	/// <summary>One member on a session's roster: enrolment state, attendance, sign-in, the hours and the snapshot the roster keeps.</summary>
	[Table("LmsClassEnrollments")]
	public class LmsClassEnrollment : IEntity
	{
		public const int NameMaxLength = 200;
		public const int NoteMaxLength = 500;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsClassEnrollmentId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsClassSessionId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LmsClassEnrollmentStatuses"/>.</summary>
		public int Status { get; set; }

		public int? WaitlistPosition { get; set; }

		/// <summary><see cref="LmsClassAttendanceStates"/>.</summary>
		public int Attendance { get; set; }

		public bool IsWalkIn { get; set; }

		/// <summary><see cref="LmsClassCheckInMethods"/>.</summary>
		public int CheckInMethod { get; set; }

		public DateTime? CheckInOn { get; set; }

		public DateTime? CheckOutOn { get; set; }

		public decimal? CheckInLatitude { get; set; }

		public decimal? CheckInLongitude { get; set; }

		/// <summary>Minutes from an imported meeting attendance report.</summary>
		public int? ImportedMinutes { get; set; }

		/// <summary>The hours the sign-off credited.</summary>
		public decimal? Hours { get; set; }

		/// <summary>Hours an instructor set by hand (wins over the computed ones).</summary>
		public decimal? HoursOverride { get; set; }

		/// <summary>The roster snapshot at sign-off: the member's name.</summary>
		public string MemberName { get; set; }

		/// <summary>The roster snapshot at sign-off: the member's job title (their roles).</summary>
		public string JobTitle { get; set; }

		public int? LearningRecordId { get; set; }

		/// <summary>When the hours were offered to the timecard (R-C10).</summary>
		public DateTime? TimecardOfferedOn { get; set; }

		/// <summary>The reminder claim (worker 95 sends once).</summary>
		public DateTime? ReminderSentOn { get; set; }

		public DateTime EnrolledOn { get; set; }

		public string EnrolledByUserId { get; set; }

		public DateTime? DecidedOn { get; set; }

		public string DecidedByUserId { get; set; }

		public DateTime? PromotedOn { get; set; }

		public DateTime? WithdrawnOn { get; set; }

		public string Note { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool HoldsSeat => Status == (int)LmsClassEnrollmentStatuses.Enrolled;

		[NotMapped]
		[JsonIgnore]
		public bool IsOnRoster => Status == (int)LmsClassEnrollmentStatuses.Enrolled || Status == (int)LmsClassEnrollmentStatuses.Waitlisted ||
			Status == (int)LmsClassEnrollmentStatuses.PendingApproval;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsClassEnrollmentId; }
			set { LmsClassEnrollmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsClassEnrollments";

		[NotMapped]
		public string IdName => "LmsClassEnrollmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "HoldsSeat", "IsOnRoster" };
	}

	/// <summary>An instructor of a session: a member, or an outside instructor by name, with qualifications and the hours taught (item 4).</summary>
	[Table("LmsClassInstructors")]
	public class LmsClassInstructor : IEntity
	{
		public const int NameMaxLength = 200;
		public const int QualificationsMaxLength = 500;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsClassInstructorId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsClassSessionId { get; set; }

		/// <summary>The member; null for an outside instructor.</summary>
		public string UserId { get; set; }

		/// <summary>An outside instructor's name, or the member's name snapshot at sign-off.</summary>
		public string Name { get; set; }

		/// <summary>Certifications and qualifications as the roster shows them.</summary>
		public string Qualifications { get; set; }

		/// <summary><see cref="LmsClassInstructorRoles"/>.</summary>
		public int Role { get; set; }

		/// <summary>Hours taught (null: the session's length).</summary>
		public decimal? HoursTaught { get; set; }

		public int SortOrder { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsMember => !string.IsNullOrWhiteSpace(UserId);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsClassInstructorId; }
			set { LmsClassInstructorId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsClassInstructors";

		[NotMapped]
		public string IdName => "LmsClassInstructorId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsMember" };
	}

	/// <summary>A cost line of a session (R-C10): instructor, materials, outside fees, backfill overtime.</summary>
	[Table("LmsClassCostLines")]
	public class LmsClassCostLine : IEntity
	{
		public const int DescriptionMaxLength = 250;
		public const decimal MaxAmount = 10000000m;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsClassCostLineId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsClassSessionId { get; set; }

		/// <summary><see cref="LmsClassCostKinds"/>.</summary>
		public int Kind { get; set; }

		public string Description { get; set; }

		public decimal Amount { get; set; }

		public string Currency { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsClassCostLineId; }
			set { LmsClassCostLineId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsClassCostLines";

		[NotMapped]
		public string IdName => "LmsClassCostLineId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	#endregion Class sessions

	#region Facility and prop booking

	/// <summary>A bookable training resource: a facility (drill ground, classroom, burn building), a prop, an apparatus or equipment.</summary>
	[Table("LmsTrainingResources")]
	public class LmsTrainingResource : IEntity
	{
		public const int NameMaxLength = 150;
		public const int NotesMaxLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsTrainingResourceId { get; set; }

		public int DepartmentId { get; set; }

		public string Name { get; set; }

		/// <summary><see cref="LmsTrainingResourceKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The point of interest the facility is (Mapping), when there is one.</summary>
		public int? PoiId { get; set; }

		public int? Capacity { get; set; }

		public string Notes { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsTrainingResourceId; }
			set { LmsTrainingResourceId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsTrainingResources";

		[NotMapped]
		public string IdName => "LmsTrainingResourceId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A resource booked for a session's time; two bookings of one resource never overlap.</summary>
	[Table("LmsClassResourceBookings")]
	public class LmsClassResourceBooking : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsClassResourceBookingId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsTrainingResourceId { get; set; }

		public int LmsClassSessionId { get; set; }

		/// <summary>UTC.</summary>
		public DateTime StartsOn { get; set; }

		/// <summary>UTC.</summary>
		public DateTime EndsOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsClassResourceBookingId; }
			set { LmsClassResourceBookingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsClassResourceBookings";

		[NotMapped]
		public string IdName => "LmsClassResourceBookingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	#endregion Facility and prop booking

	#region Lesson plans (R-C6)

	/// <summary>
	/// A lesson plan (§13 T4 item 5, R-C6) on the content core: versioned, submitted, approved and published like a protocol (P1
	/// lifecycle, <see cref="ProtocolLifecycle"/>). A session is taught from its published version.
	/// </summary>
	[Table("LmsLessonPlans")]
	public class LmsLessonPlan : IEntity
	{
		public const int TitleMaxLength = 250;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLessonPlanId { get; set; }

		public int DepartmentId { get; set; }

		public string Title { get; set; }

		/// <summary>The Resgrid starter plan it was made from (<see cref="LmsLessonPlanStarterCatalog"/>).</summary>
		public string StarterKey { get; set; }

		/// <summary>The published version.</summary>
		public int? CurrentVersionId { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLessonPlanId; }
			set { LmsLessonPlanId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLessonPlans";

		[NotMapped]
		public string IdName => "LmsLessonPlanId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One version of a lesson plan; <see cref="Status"/> is a <see cref="ProtocolVersionStatuses"/> value.</summary>
	[Table("LmsLessonPlanVersions")]
	public class LmsLessonPlanVersion : IEntity
	{
		public const int SummaryMaxLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLessonPlanVersionId { get; set; }

		public int LmsLessonPlanId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="ProtocolVersionStatuses"/>.</summary>
		public int Status { get; set; }

		public int Major { get; set; }

		public int Minor { get; set; }

		public string Title { get; set; }

		/// <summary>JSON <see cref="LmsLessonPlanContent"/>.</summary>
		public string ContentJson { get; set; }

		/// <summary>SHA-256 (lower-case hex) of <see cref="ContentJson"/>, fixed at submission.</summary>
		public string ContentHash { get; set; }

		public string ChangeSummary { get; set; }

		public DateTime? SubmittedOn { get; set; }

		public string SubmittedByUserId { get; set; }

		public DateTime? DecidedOn { get; set; }

		public string DecidedByUserId { get; set; }

		public string DecisionNote { get; set; }

		public DateTime? PublishedOn { get; set; }

		public string PublishedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public string Number => Major <= 0 ? null : Major + "." + Minor;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLessonPlanVersionId; }
			set { LmsLessonPlanVersionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLessonPlanVersions";

		[NotMapped]
		public string IdName => "LmsLessonPlanVersionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Number" };

		public LmsLessonPlanContent Content() => LmsLessonPlanContent.Parse(ContentJson);
	}

	/// <summary>
	/// A lesson plan's content (plain text, rendered encoded): objectives, the competency items it covers (round-4C frameworks), materials
	/// and props, the instructor-to-student ratio, the safety plan and the pre-brief checklist.
	/// </summary>
	public class LmsLessonPlanContent
	{
		public const int MaxItems = 60;
		public const int ItemMaxLength = 500;
		public const int SafetyPlanMaxLength = 8000;

		public List<string> Objectives { get; set; } = new List<string>();
		public List<int> CompetencyItemIds { get; set; } = new List<int>();
		public List<string> Materials { get; set; } = new List<string>();
		public List<string> Props { get; set; } = new List<string>();
		/// <summary>Students per instructor (0: not set).</summary>
		public int StudentsPerInstructor { get; set; }
		public int DurationMinutes { get; set; }
		public string SafetyPlan { get; set; }
		public List<string> PreBriefChecklist { get; set; } = new List<string>();
		public List<string> Roles { get; set; } = new List<string>();
		public string Notes { get; set; }

		public static LmsLessonPlanContent Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new LmsLessonPlanContent();
			try
			{
				return (JsonConvert.DeserializeObject<LmsLessonPlanContent>(json) ?? new LmsLessonPlanContent()).Normalized();
			}
			catch (JsonException)
			{
				return new LmsLessonPlanContent();
			}
		}

		public LmsLessonPlanContent Normalized()
		{
			static List<string> Lines(IEnumerable<string> values) => (values ?? Enumerable.Empty<string>())
				.Select(v => (v ?? string.Empty).Trim()).Where(v => v.Length > 0).Select(v => v.Length > ItemMaxLength ? v.Substring(0, ItemMaxLength) : v)
				.Take(MaxItems).ToList();

			var safety = (SafetyPlan ?? string.Empty).Trim();
			var notes = (Notes ?? string.Empty).Trim();
			return new LmsLessonPlanContent
			{
				Objectives = Lines(Objectives),
				CompetencyItemIds = (CompetencyItemIds ?? new List<int>()).Where(i => i > 0).Distinct().Take(200).ToList(),
				Materials = Lines(Materials),
				Props = Lines(Props),
				StudentsPerInstructor = Math.Clamp(StudentsPerInstructor, 0, 1000),
				DurationMinutes = Math.Clamp(DurationMinutes, 0, 24 * 60 * 7),
				SafetyPlan = safety.Length == 0 ? null : safety.Length > SafetyPlanMaxLength ? safety.Substring(0, SafetyPlanMaxLength) : safety,
				PreBriefChecklist = Lines(PreBriefChecklist),
				Roles = Lines(Roles),
				Notes = notes.Length == 0 ? null : notes.Length > SafetyPlanMaxLength ? notes.Substring(0, SafetyPlanMaxLength) : notes
			};
		}

		public string ToJson() => JsonConvert.SerializeObject(Normalized());
	}

	#endregion Lesson plans (R-C6)
}
