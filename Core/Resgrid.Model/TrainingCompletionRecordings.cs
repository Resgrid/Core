using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	// Recorded completions (round 7B, product-owner decision 2026-11-25, permission 135 RecordTrainingCompletions, M0400): an instructor or
	// training officer records that members completed a training they took together (a class, a group viewing). The completion is applied to
	// each member's record through the normal path (baseline training assignment / LMS course enrolment, learning record, certification) and
	// carries metadata that says it was recorded by someone else: never presented as the member's own completion. Baseline (free): the LMS
	// course target is an id and a kind here; the LMS layer completes the course itself (ITrainingCompletionRecordingTarget). Append only.

	/// <summary>How a completion came about (LearningRecords, TrainingUsers, LmsEnrollments). Persisted; append only.</summary>
	public enum LearningCompletionMethods
	{
		/// <summary>The member completed it themselves (or the source system recorded it as theirs): every record before M0400.</summary>
		Self = 0,
		/// <summary>An instructor or training officer recorded it for the member ("recorded by instructor").</summary>
		RecordedByOther = 1
	}

	/// <summary>What a recorded completion completes.</summary>
	public enum TrainingCompletionRecordingTargets
	{
		/// <summary>A baseline training (its TrainingUsers assignment).</summary>
		Training = 0,
		/// <summary>An LMS course (its current version, completed through the LMS layer).</summary>
		Course = 1
	}

	/// <summary>Where the recording was made.</summary>
	public enum TrainingCompletionRecordingSources
	{
		/// <summary>From a course's enrolment list or a training's assignee list (a note is required).</summary>
		Manual = 0,
		/// <summary>From a class session's roster (the session is the source reference).</summary>
		ClassSession = 1
	}

	/// <summary>
	/// One member's recorded completion: who recorded it, when, from where, the note and the attestation statement version the recorder
	/// accepted, the records it produced and, when undone, who voided it and why. The row is the audit trail; it is never deleted except
	/// with the department.
	/// </summary>
	[Table("TrainingCompletionRecordings")]
	public class TrainingCompletionRecording : IEntity
	{
		public const int NoteMaxLength = 1000;
		public const int ReasonMaxLength = 500;
		public const int NameMaxLength = 250;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingCompletionRecordingId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The member (or, on a class roster, an external learner's "xl-" id).</summary>
		public string UserId { get; set; }

		/// <summary><see cref="TrainingCompletionRecordingTargets"/>.</summary>
		public int TargetKind { get; set; }

		/// <summary>The training id or the LMS course id.</summary>
		public int TargetId { get; set; }

		/// <summary>The LMS course version completed (null for a training).</summary>
		public int? TargetVersionId { get; set; }

		/// <summary>The training's or course's name when it was recorded.</summary>
		public string TargetName { get; set; }

		/// <summary><see cref="TrainingCompletionRecordingSources"/>.</summary>
		public int SourceKind { get; set; }

		/// <summary>The class session id (ClassSession), else null.</summary>
		public string SourceRefId { get; set; }

		/// <summary>Every member recorded by one action shares the key.</summary>
		public string BatchKey { get; set; }

		public string Note { get; set; }

		/// <summary>The attestation statement the recorder accepted (<see cref="TrainingCompletionAttestation"/>).</summary>
		public string AttestationVersion { get; set; }

		/// <summary>The training has a quiz the member did not take (recorded as waived).</summary>
		public bool QuizWaived { get; set; }

		/// <summary>The completion moment written to the record (UTC).</summary>
		public DateTime CompletedOn { get; set; }

		/// <summary>Baseline: the TrainingUsers row and its assignment cycle.</summary>
		public int? TrainingUserId { get; set; }

		public int? Cycle { get; set; }

		/// <summary>LMS: the enrolment completed.</summary>
		public int? EnrollmentId { get; set; }

		public int? LearningRecordId { get; set; }

		/// <summary>The certification the completion issued or renewed, when there was one.</summary>
		public int? PersonnelCertificationId { get; set; }

		public string RecordedByUserId { get; set; }

		public DateTime RecordedOn { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public string VoidReason { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsVoided => VoidedOn.HasValue;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingCompletionRecordingId; }
			set { TrainingCompletionRecordingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingCompletionRecordings";

		[NotMapped]
		public string IdName => "TrainingCompletionRecordingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsVoided" };
	}

	/// <summary>
	/// The attestation statement a recorder accepts. The text lives in the Trainings resources under <see cref="StatementKey"/>; a change of
	/// wording is a new version (the stored version says which wording was accepted). An L2 signature was considered and not used: no
	/// <c>SignatureUse</c> fits a bulk roster action, and the statement version, the recorder, the time and the audit entry are the record.
	/// </summary>
	public static class TrainingCompletionAttestation
	{
		public const string CurrentVersion = "2026-11-25";

		public static readonly IReadOnlyList<string> Versions = new[] { CurrentVersion };

		public static string StatementKey(string version) => "RecordCompletions.Attestation." + (string.IsNullOrWhiteSpace(version) ? CurrentVersion : version.Trim());

		public static bool IsKnown(string version) => !string.IsNullOrWhiteSpace(version) && Versions.Contains(version.Trim());
	}

	/// <summary>What the recorder asks for: the members, the note, the attestation and where it comes from.</summary>
	public class TrainingCompletionRecordingRequest
	{
		public string RecorderUserId { get; set; }
		public List<string> MemberUserIds { get; set; } = new List<string>();
		public string Note { get; set; }
		/// <summary>The recorder ticked the attestation statement.</summary>
		public bool Attested { get; set; }
		public string AttestationVersion { get; set; } = TrainingCompletionAttestation.CurrentVersion;
		public TrainingCompletionRecordingSources Source { get; set; } = TrainingCompletionRecordingSources.Manual;
		/// <summary>The class session id (ClassSession).</summary>
		public string SourceRefId { get; set; }
		/// <summary>The completion moment (null: now). A class uses its end.</summary>
		public DateTime? CompletedOnUtc { get; set; }
		/// <summary>Set by the caller that groups several targets (a class) under one key; else a new key.</summary>
		public string BatchKey { get; set; }
	}

	/// <summary>One member's outcome of a recording.</summary>
	public class TrainingCompletionRecordingResult
	{
		public string UserId { get; set; }
		/// <summary>Null when recorded; else a "training_record_*" refusal code the page shows as text.</summary>
		public string Refusal { get; set; }
		public TrainingCompletionRecording Recording { get; set; }
		public bool Recorded => Refusal == null && Recording != null;
	}

	public class TrainingCompletionRecordingOutcome
	{
		/// <summary>A refusal of the whole request ("training_record_*"); null when members were looked at one by one.</summary>
		public string Error { get; set; }
		public List<TrainingCompletionRecordingResult> Results { get; set; } = new List<TrainingCompletionRecordingResult>();
		public int Recorded => Results.Count(r => r.Recorded);
		public int Refused => Results.Count(r => !r.Recorded);
		public bool Succeeded => Error == null;

		public static TrainingCompletionRecordingOutcome Fail(string error) => new TrainingCompletionRecordingOutcome { Error = error };
	}

	/// <summary>One member the record page offers: whether they are assigned / enrolled, why they cannot be recorded, their live recording.</summary>
	public class TrainingCompletionCandidate
	{
		public string UserId { get; set; }
		/// <summary>On the training's assignee list or the course's enrolment list (open).</summary>
		public bool Assigned { get; set; }
		/// <summary>Why a completion cannot be recorded for them ("training_record_*"); null when it can.</summary>
		public string Refusal { get; set; }
		public TrainingCompletionRecording Recording { get; set; }
	}

	/// <summary>The "Record completion" page of a baseline training or an LMS course (from its assignee / enrolment list).</summary>
	public class TrainingCompletionRecordingView
	{
		public TrainingCompletionRecordingTargets Target { get; set; }
		public int TargetId { get; set; }
		public string TargetName { get; set; }
		/// <summary>The training has a quiz: a recorded completion notes it as waived.</summary>
		public bool HasQuiz { get; set; }
		public bool CanRecord { get; set; }
		/// <summary>Why nothing can be recorded for this target now (not found, the add-on); null when it can.</summary>
		public string Refusal { get; set; }
		public List<TrainingCompletionCandidate> Candidates { get; set; } = new List<TrainingCompletionCandidate>();
		public List<TrainingCompletionRecording> Recordings { get; set; } = new List<TrainingCompletionRecording>();
		public string AttestationVersion { get; set; } = TrainingCompletionAttestation.CurrentVersion;
	}

	/// <summary>The refusal codes and pure checks of a recording (shared by the baseline and the LMS paths).</summary>
	public static class TrainingCompletionRecordingRules
	{
		public const string NotAuthorized = "training_record_not_authorized";
		public const string AttestationRequired = "training_record_attestation_required";
		public const string NoteRequired = "training_record_note_required";
		public const string NoMembers = "training_record_no_members";
		public const string NotFound = "training_record_not_found";
		public const string NotActiveMember = "training_record_not_active";
		public const string OutsideScope = "training_record_outside_scope";
		public const string Self = "training_record_self";
		public const string AlreadyComplete = "training_record_already_complete";
		public const string NoSeat = "training_record_no_seat";
		public const string AddonRequired = "training_record_addon_required";
		public const string NotOnRoster = "training_record_not_on_roster";
		public const string ExternalNotAllowed = "training_record_external";
		public const string NoLinkedTraining = "training_record_no_linked_training";
		public const string ClassNotStarted = "training_record_class_not_started";
		public const string ClassCancelled = "training_record_class_cancelled";
		public const string ReasonRequired = "training_record_reason_required";
		public const string AlreadyVoided = "training_record_already_voided";
		public const string Failed = "training_record_failed";
		public const int MaxMembers = 500;

		/// <summary>Why the request as a whole cannot be recorded (attestation, note, members); null when it can.</summary>
		public static string RequestRefusal(TrainingCompletionRecordingRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.RecorderUserId))
				return NotAuthorized;
			if (!request.Attested || !TrainingCompletionAttestation.IsKnown(request.AttestationVersion))
				return AttestationRequired;
			if (request.Source == TrainingCompletionRecordingSources.Manual && string.IsNullOrWhiteSpace(request.Note))
				return NoteRequired;
			if (request.Source == TrainingCompletionRecordingSources.ClassSession && string.IsNullOrWhiteSpace(request.SourceRefId))
				return NotFound;
			var members = Members(request);
			if (members.Count == 0 || members.Count > MaxMembers)
				return NoMembers;
			return null;
		}

		/// <summary>The distinct, trimmed member ids of a request (case-insensitive).</summary>
		public static List<string> Members(TrainingCompletionRecordingRequest request) =>
			(request?.MemberUserIds ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

		public static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		/// <summary>The recording row a request makes for one member (ids of the produced records are filled in afterwards).</summary>
		public static TrainingCompletionRecording NewRecording(int departmentId, string userId, TrainingCompletionRecordingTargets target, int targetId, int? targetVersionId,
			string targetName, TrainingCompletionRecordingRequest request, string batchKey, DateTime completedOn, bool quizWaived, DateTime utcNow) => new TrainingCompletionRecording
			{
				DepartmentId = departmentId,
				UserId = userId,
				TargetKind = (int)target,
				TargetId = targetId,
				TargetVersionId = targetVersionId,
				TargetName = Trim(targetName, TrainingCompletionRecording.NameMaxLength),
				SourceKind = (int)request.Source,
				SourceRefId = request.Source == TrainingCompletionRecordingSources.ClassSession ? Trim(request.SourceRefId, 64) : null,
				BatchKey = batchKey,
				Note = Trim(request.Note, TrainingCompletionRecording.NoteMaxLength),
				AttestationVersion = request.AttestationVersion.Trim(),
				QuizWaived = quizWaived,
				CompletedOn = completedOn,
				RecordedByUserId = request.RecorderUserId.Trim(),
				RecordedOn = utcNow
			};

		/// <summary>A short, human source label ("class:12" / "manual").</summary>
		public static string SourceLabel(TrainingCompletionRecording recording) =>
			recording == null ? null : recording.SourceKind == (int)TrainingCompletionRecordingSources.ClassSession ? "class:" + recording.SourceRefId : "manual";
	}
}
