using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// A member's enrollment in a course, pinned to the version they take (R-L12: a learner finishes the version they started;
	/// an enrollment that has not started moves to a newly published version). One open enrollment per member and course
	/// (filtered unique index on <see cref="IsOpen"/>). Completion writes one learning record (T1 writer) and may issue or renew a
	/// certification.
	/// </summary>
	[Table("LmsEnrollments")]
	public class LmsEnrollment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsEnrollmentId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LmsEnrollmentSources"/>.</summary>
		public int Source { get; set; }

		/// <summary>The Academy role path that assigned it (A1), e.g. "dispatcher".</summary>
		public string RolePathKey { get; set; }

		public string AssignedByUserId { get; set; }

		public DateTime AssignedOn { get; set; }

		public DateTime? DueOn { get; set; }

		/// <summary><see cref="LmsEnrollmentStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>True while NotStarted or InProgress (the unique open enrollment).</summary>
		public bool IsOpen { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>Completed through a passed pre-test (test-out).</summary>
		public bool TestedOut { get; set; }

		/// <summary>The lesson the learner was on (resume).</summary>
		public int? CurrentLessonId { get; set; }

		public int ProgressPercent { get; set; }

		public int SeatSeconds { get; set; }

		public DateTime? LastActivityOn { get; set; }

		/// <summary>The learning record the completion wrote (LearningRecords, baseline).</summary>
		public int? LearningRecordId { get; set; }

		/// <summary>The certification record the completion issued or renewed (PersonnelCertifications, baseline).</summary>
		public int? PersonnelCertificationId { get; set; }

		/// <summary>When worker 88 last reminded the learner of this in-progress course (claimed before sending).</summary>
		public DateTime? ReminderSentOn { get; set; }

		public DateTime? WithdrawnOn { get; set; }

		public string WithdrawnByUserId { get; set; }

		public long RowVersion { get; set; }

		/// <summary>
		/// Round 7B (M0401): <see cref="LearningCompletionMethods"/>. RecordedByOther: an instructor recorded the completion (permission 135);
		/// the learning record carries the same metadata. A later enrolment the member completes themselves is its own record (both are kept).
		/// </summary>
		public int CompletionMethod { get; set; }

		/// <summary>The TrainingCompletionRecordings row of a recorded completion.</summary>
		public int? RecordedCompletionId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsEnrollmentId; }
			set { LmsEnrollmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsEnrollments";

		[NotMapped]
		public string IdName => "LmsEnrollmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Progress on one lesson of an enrollment (R-C8): resume, video position, watched percent, seat time.</summary>
	[Table("LmsLessonProgress")]
	public class LmsLessonProgress : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLessonProgressId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsEnrollmentId { get; set; }

		public int LmsLessonId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LmsLessonProgressStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>Seconds on the lesson, accrued from heartbeats and capped by wall-clock time (never more than really elapsed).</summary>
		public int SeatSeconds { get; set; }

		public int VideoPositionSeconds { get; set; }

		/// <summary>The furthest point watched, in percent of the video (0–100).</summary>
		public int WatchedPercent { get; set; }

		public DateTime? LastSeenOn { get; set; }

		/// <summary>The learner's free-response answer.</summary>
		public string ResponseText { get; set; }

		/// <summary>The learner's uploaded file (LmsLessonFiles).</summary>
		public int? LmsLessonFileId { get; set; }

		/// <summary>The crew viewing the member was marked present at (R-C4), when that completed the lesson.</summary>
		public int? LmsCrewViewingId { get; set; }

		/// <summary>Round 7B (M0401): the recorded completion that marked this lesson complete (undone when the recording is voided).</summary>
		public int? RecordedCompletionId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLessonProgressId; }
			set { LmsLessonProgressId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLessonProgress";

		[NotMapped]
		public string IdName => "LmsLessonProgressId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A file a learner uploaded for a file-request lesson. Bytes live here only; never read in a list query.</summary>
	[Table("LmsLessonFiles")]
	public class LmsLessonFile : IEntity
	{
		public const long MaxBytes = 10 * 1024 * 1024;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLessonFileId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsLessonId { get; set; }

		public string UserId { get; set; }

		public string FileName { get; set; }

		public string ContentType { get; set; }

		public long SizeBytes { get; set; }

		public byte[] Data { get; set; }

		public DateTime UploadedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLessonFileId; }
			set { LmsLessonFileId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLessonFiles";

		[NotMapped]
		public string IdName => "LmsLessonFileId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One anonymous course evaluation response: no user, the day only (aggregate results, T2 item 5).</summary>
	[Table("LmsSurveyResponses")]
	public class LmsSurveyResponse : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSurveyResponseId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public int LmsLessonId { get; set; }

		/// <summary>Answers by question key (JSON object of key to string).</summary>
		public string AnswersJson { get; set; }

		/// <summary>The UTC day only, so a response cannot be matched to a member's completion time.</summary>
		public DateTime SubmittedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsSurveyResponseId; }
			set { LmsSurveyResponseId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsSurveyResponses";

		[NotMapped]
		public string IdName => "LmsSurveyResponseId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A crew viewing (R-C4): one lesson on a shared screen with a rotating QR each member scans to be marked present.</summary>
	[Table("LmsCrewViewings")]
	public class LmsCrewViewing : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCrewViewingId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public int LmsLessonId { get; set; }

		public string StartedByUserId { get; set; }

		public DateTime StartedOn { get; set; }

		/// <summary>Scanning stops at this time even if nobody ends the viewing.</summary>
		public DateTime ExpiresOn { get; set; }

		public DateTime? EndedOn { get; set; }

		/// <summary>The rotating code's key (random, never shown).</summary>
		public string Secret { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCrewViewingId; }
			set { LmsCrewViewingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCrewViewings";

		[NotMapped]
		public string IdName => "LmsCrewViewingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A member marked present at a crew viewing by scanning its code on their own phone.</summary>
	[Table("LmsCrewViewingAttendees")]
	public class LmsCrewViewingAttendee : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCrewViewingAttendeeId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCrewViewingId { get; set; }

		public string UserId { get; set; }

		public DateTime MarkedOn { get; set; }

		public string IpAddress { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCrewViewingAttendeeId; }
			set { LmsCrewViewingAttendeeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCrewViewingAttendees";

		[NotMapped]
		public string IdName => "LmsCrewViewingAttendeeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A learning path assigned to a member: an Academy role path (A1, R-S2; <see cref="PathKey"/> is the role path key) or, since T3, a
	/// department path (<see cref="PathKey"/> "dept-{id}"). Each step's course is tracked as an enrollment.
	/// </summary>
	[Table("LmsPathAssignments")]
	public class LmsPathAssignment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsPathAssignmentId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="AcademyRolePath.Key"/>.</summary>
		public string PathKey { get; set; }

		public string UserId { get; set; }

		public string AssignedByUserId { get; set; }

		public DateTime AssignedOn { get; set; }

		public DateTime? DueOn { get; set; }

		public DateTime? RemovedOn { get; set; }

		/// <summary>T3 (M0365): the learning path (an Academy system path or a department path); null on rows not linked yet.</summary>
		public int? LmsPathId { get; set; }

		/// <summary>T3: when the learner finished the path.</summary>
		public DateTime? CompletedOn { get; set; }

		/// <summary>T3: the certification the completed path issued or renewed.</summary>
		public int? PersonnelCertificationId { get; set; }

		/// <summary>T3: the learning requirement (onboarding) that assigned it, if one did.</summary>
		public int? LmsRequirementId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsPathAssignmentId; }
			set { LmsPathAssignmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsPathAssignments";

		[NotMapped]
		public string IdName => "LmsPathAssignmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// The department's LMS state the lapse rules need (§17.3): when the add-on was last seen active (stamped by the gate,
	/// at most hourly). The lapse windows (rules 4, 6, 11) run from <see cref="LastEntitledOn"/>.
	/// </summary>
	[Table("LmsDepartmentStates")]
	public class LmsDepartmentState : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsDepartmentStateId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The last time the gate saw an active LMS add-on; null when it never has.</summary>
		public DateTime? LastEntitledOn { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsDepartmentStateId; }
			set { LmsDepartmentStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsDepartmentStates";

		[NotMapped]
		public string IdName => "LmsDepartmentStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Hosted media (decision 9): the uploaded source and its HLS renditions in the LMS bucket on RustFS, transcoded by worker 87.
	/// Stored bytes count toward the department's included 100 GB (report only). Academy media is never hosted here.
	/// </summary>
	[Table("LmsMediaAssets")]
	public class LmsMediaAsset : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsMediaAssetId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>
		/// Set when this row shares another department's stored objects (a course set copy, round 10 A3, M0418): the department whose keys
		/// SourceKey/HlsPrefix/Mp4Key name. Such a row is never archived for its own department's lapse (it follows the set or sponsorship
		/// gate), and a store purge must not delete objects while any row names them. Null for the department's own uploads.
		/// </summary>
		public int? SharedFromDepartmentId { get; set; }

		/// <summary><see cref="LmsMediaKinds"/>.</summary>
		public int Kind { get; set; }

		public string FileName { get; set; }

		public string ContentType { get; set; }

		/// <summary>The uploaded source's object key.</summary>
		public string SourceKey { get; set; }

		public long SourceBytes { get; set; }

		/// <summary><see cref="LmsMediaStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The key prefix the HLS playlist and segments live under ("…/hls/"); master playlist "index.m3u8".</summary>
		public string HlsPrefix { get; set; }

		/// <summary>The progressive MP4 rendition (the one Omni may cache offline, R-C9).</summary>
		public string Mp4Key { get; set; }

		public long OutputBytes { get; set; }

		public int DurationSeconds { get; set; }

		public int Attempts { get; set; }

		public string LastError { get; set; }

		public DateTime? ClaimedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? ReadyOn { get; set; }

		public DateTime? ArchivedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public long StoredBytes => SourceBytes + OutputBytes;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsMediaAssetId; }
			set { LmsMediaAssetId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsMediaAssets";

		[NotMapped]
		public string IdName => "LmsMediaAssetId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "StoredBytes" };
	}
}
