using System;
using System.Collections.Generic;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Training T4 part 2 (LMS add-on): evaluator mode for Omni (Omni itself is out of scope for this build). All times are UTC; enum
	// fields are names unless noted. Steps and answers travel as the server's own shapes so a device grades with the same engine rules.

	/// <summary>A skill sheet version as a device caches it for offline evaluations.</summary>
	public class LmsSkillSheetData
	{
		public int SkillSheetId { get; set; }
		public int SkillSheetVersionId { get; set; }
		public string Title { get; set; }
		public string Code { get; set; }
		/// <summary>LmsSkillSheetKinds name: SkillSheet, ObservationReport.</summary>
		public string Kind { get; set; }
		public string Version { get; set; }
		/// <summary>SHA-256 of the version (steps and grading): a device's cache key.</summary>
		public string ContentHash { get; set; }
		public decimal PassThreshold { get; set; }
		public int TimeLimitSeconds { get; set; }
		public bool AllowCrewEvaluation { get; set; }
		public bool AllowSelfEvaluation { get; set; }
		/// <summary>The evaluator must pick a next signer from <see cref="NextSignerUserIds"/>.</summary>
		public bool NextSignerRequired { get; set; }
		public List<string> NextSignerUserIds { get; set; } = new List<string>();
		/// <summary>The steps (Type is the LmsSkillStepTypes value).</summary>
		public List<LmsSkillStep> Steps { get; set; } = new List<LmsSkillStep>();
	}

	public class LmsSkillSheetsResult : StandardApiResponseV4Base
	{
		public List<LmsSkillSheetData> Data { get; set; } = new List<LmsSkillSheetData>();
	}

	public class LmsSkillSheetResult : StandardApiResponseV4Base
	{
		public LmsSkillSheetData Data { get; set; }
	}

	/// <summary>An evaluation as the evaluator's device and the member's list show it.</summary>
	public class LmsSkillEvaluationData
	{
		public int EvaluationId { get; set; }
		public int? SkillSheetId { get; set; }
		public int? SkillSheetVersionId { get; set; }
		public string Title { get; set; }
		public string Code { get; set; }
		/// <summary>LmsSkillEvaluationModes name: Individual, Crew.</summary>
		public string Mode { get; set; }
		/// <summary>LmsSkillEvaluationStatuses name: InProgress, AwaitingSignature, Completed, Voided.</summary>
		public string Status { get; set; }
		/// <summary>LmsSkillEvaluationResults name: Pending, Passed, Failed.</summary>
		public string Result { get; set; }
		public decimal? Score { get; set; }
		public bool CriticalFailed { get; set; }
		public bool TimeExceeded { get; set; }
		public string EvaluatorUserId { get; set; }
		public List<string> MemberUserIds { get; set; } = new List<string>();
		public DateTime StartedOn { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public int DurationSeconds { get; set; }
		public int? TaskBookId { get; set; }
		public string TaskKey { get; set; }
		public int? CallId { get; set; }
		public int? DeploymentId { get; set; }
		public long RowVersion { get; set; }
		/// <summary>The S3 evaluator request the evaluator signs (Signatures/SignStep or SignEvaluation).</summary>
		public int? SignatureRequestId { get; set; }
		public string ContentHash { get; set; }
		public string IdempotencyKey { get; set; }
		/// <summary>The answers (only on a single evaluation read).</summary>
		public LmsSkillAnswers Answers { get; set; }
	}

	public class LmsSkillEvaluationsResult : StandardApiResponseV4Base
	{
		public List<LmsSkillEvaluationData> Data { get; set; } = new List<LmsSkillEvaluationData>();
	}

	public class LmsSkillEvaluationResult : StandardApiResponseV4Base
	{
		public LmsSkillEvaluationData Data { get; set; }
		/// <summary>The refusal code ("lms_skill_*") when the action was refused.</summary>
		public string Error { get; set; }
	}

	/// <summary>The evaluator's pending work: open evaluations and evaluator signatures waiting for them.</summary>
	public class LmsSkillPendingData
	{
		public List<LmsSkillEvaluationData> Evaluations { get; set; } = new List<LmsSkillEvaluationData>();
		public List<LmsSkillPendingSignatureData> Signatures { get; set; } = new List<LmsSkillPendingSignatureData>();
	}

	public class LmsSkillPendingSignatureData
	{
		public int SignatureRequestId { get; set; }
		public int SignatureRequestSignerId { get; set; }
		public string SubjectId { get; set; }
		public string Title { get; set; }
		public string StepLabel { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string ContentCode { get; set; }
	}

	public class LmsSkillPendingResult : StandardApiResponseV4Base
	{
		public LmsSkillPendingData Data { get; set; }
	}

	public class LmsSkillStartInput
	{
		public int? SkillSheetId { get; set; }
		public List<string> MemberUserIds { get; set; } = new List<string>();
		public int? TaskBookId { get; set; }
		public string TaskKey { get; set; }
		public int? CallId { get; set; }
		public int? DeploymentId { get; set; }
		public string Location { get; set; }
		/// <summary>The outbox key: a replay of the same start returns the same evaluation.</summary>
		public string IdempotencyKey { get; set; }
		public DateTime? StartedOn { get; set; }
	}

	public class LmsSkillSaveInput
	{
		public int EvaluationId { get; set; }
		public long RowVersion { get; set; }
		public LmsSkillAnswers Answers { get; set; }
		public string Notes { get; set; }
		public bool PassAll { get; set; }
	}

	public class LmsSkillSubmitInput
	{
		public int EvaluationId { get; set; }
		public long RowVersion { get; set; }
		public string NextSignerUserId { get; set; }
		public DateTime? EndedOn { get; set; }
	}

	/// <summary>A whole evaluation captured offline (the Omni outbox).</summary>
	public class LmsSkillOfflineInput
	{
		public string IdempotencyKey { get; set; }
		public int SkillSheetId { get; set; }
		public int SkillSheetVersionId { get; set; }
		public List<string> MemberUserIds { get; set; } = new List<string>();
		public LmsSkillAnswers Answers { get; set; }
		public string Notes { get; set; }
		public DateTime StartedOn { get; set; }
		public DateTime EndedOn { get; set; }
		public int? TaskBookId { get; set; }
		public string TaskKey { get; set; }
		public int? CallId { get; set; }
		public int? DeploymentId { get; set; }
		public string Location { get; set; }
		public string NextSignerUserId { get; set; }
	}

	public class LmsSkillEvidenceInput
	{
		public int EvaluationId { get; set; }
		public string StepKey { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		/// <summary>The file, base64.</summary>
		public string Data { get; set; }
	}

	public class LmsSkillEvidenceResult : StandardApiResponseV4Base
	{
		public int? FileId { get; set; }
		public string Sha256 { get; set; }
		public string Error { get; set; }
	}

	public class LmsSkillSignInput
	{
		public int EvaluationId { get; set; }
	}

	public class LmsSkillSignResult : StandardApiResponseV4Base
	{
		public bool Succeeded { get; set; }
		/// <summary>SignatureRequestRefusals name when refused (StepUpRequired: do the step-up, then try again).</summary>
		public string Refusal { get; set; }
		public string EvaluationStatus { get; set; }
	}

	public class LmsTaskBookData
	{
		public int TaskBookId { get; set; }
		public string UserId { get; set; }
		public string PositionCode { get; set; }
		public string Name { get; set; }
		/// <summary>LmsTaskBookStatuses name.</summary>
		public string Status { get; set; }
		public DateTime InitiatedOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public List<LmsTaskBookTaskData> Tasks { get; set; } = new List<LmsTaskBookTaskData>();
	}

	public class LmsTaskBookTaskData
	{
		public string Key { get; set; }
		public string Number { get; set; }
		public string Title { get; set; }
		public int RequiredEvaluations { get; set; }
		public int Counted { get; set; }
		public bool Complete { get; set; }
		public bool RequiresIncident { get; set; }
		public int? SkillSheetId { get; set; }
	}

	public class LmsTaskBooksResult : StandardApiResponseV4Base
	{
		public List<LmsTaskBookData> Data { get; set; } = new List<LmsTaskBookData>();
	}
}
