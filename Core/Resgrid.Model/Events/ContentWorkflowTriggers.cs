using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Resgrid.Model.Events
{
	/// <summary>One template variable of a content workflow family: the payload property it reads, its type, what it means and a sample.</summary>
	public sealed class ContentWorkflowVariable
	{
		public ContentWorkflowVariable(string property, string dataType, string description, object sample)
		{
			Property = property;
			DataType = dataType;
			Description = description;
			Sample = sample;
			Variable = ContentWorkflowTriggers.ToSnakeCase(property);
		}

		/// <summary>The payload's property name ("ProtocolVersionId").</summary>
		public string Property { get; }

		/// <summary>The variable under the family's prefix ("protocol_version_id").</summary>
		public string Variable { get; }

		/// <summary>int, string, datetime, bool or decimal (the catalog's types).</summary>
		public string DataType { get; }

		public string Description { get; }

		public object Sample { get; }
	}

	/// <summary>
	/// One event family: the payload type, the template prefix it maps to, its triggers with the change each one means, and its variables.
	/// </summary>
	public sealed class ContentWorkflowFamily
	{
		public ContentWorkflowFamily(string prefix, Type payloadType, string userIdProperty, string changeDescription,
			IReadOnlyDictionary<WorkflowTriggerEventType, string> changes, IReadOnlyList<ContentWorkflowVariable> variables)
		{
			Prefix = prefix;
			PayloadType = payloadType;
			UserIdProperty = userIdProperty;
			ChangeDescription = changeDescription;
			Changes = changes;
			Variables = variables;
		}

		/// <summary>The Scriban object the variables sit under ("protocol_event").</summary>
		public string Prefix { get; }

		public Type PayloadType { get; }

		/// <summary>The payload property whose member fills the common user.* block (null: none).</summary>
		public string UserIdProperty { get; }

		public string ChangeDescription { get; }

		/// <summary>Trigger to the change it means ("Published").</summary>
		public IReadOnlyDictionary<WorkflowTriggerEventType, string> Changes { get; }

		public IReadOnlyList<ContentWorkflowVariable> Variables { get; }
	}

	/// <summary>
	/// The round-7C workflow triggers of the Protocols, Training/LMS, signing and knowledge build (218-219, 230-235, 248-274), one table
	/// that drives the variable catalog, the template context, the sample data and the trigger names in the workflow picker. Each
	/// family's payload is its event class in Events/ContentWorkflowEvents.cs; <c>change</c> comes from the run's trigger.
	/// </summary>
	public static class ContentWorkflowTriggers
	{
		private const string Member = "4b8e1c2d-7a90-4f3e-b1d2-6c5a4e3f2b1a";
		private const string Officer = "6c5a4e3f-2b1a-4b8e-9c2d-7a904f3eb1d2";

		private static readonly DateTime SampleDay = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);

		private static ContentWorkflowVariable V(string property, string dataType, string description, object sample) =>
			new ContentWorkflowVariable(property, dataType, description, sample);

		private static IReadOnlyDictionary<WorkflowTriggerEventType, string> Map(params (WorkflowTriggerEventType Trigger, string Change)[] pairs) =>
			pairs.ToDictionary(p => p.Trigger, p => p.Change);

		public static readonly IReadOnlyList<ContentWorkflowFamily> Families = new List<ContentWorkflowFamily>
		{
			new ContentWorkflowFamily("protocol_event", typeof(ProtocolWorkflowEvent), "UserId",
				"Published, Retired, Expired, ReviewDue, ReviewOverdue, BulletinIssued, BulletinWithdrawn, TranslationApproved or PublicLinkChanged",
				Map((WorkflowTriggerEventType.ProtocolVersionPublished, "Published"), (WorkflowTriggerEventType.ProtocolRetired, "Retired"),
					(WorkflowTriggerEventType.ProtocolVersionExpired, "Expired"), (WorkflowTriggerEventType.ProtocolReviewDue, "ReviewDue"),
					(WorkflowTriggerEventType.ProtocolReviewOverdue, "ReviewOverdue"), (WorkflowTriggerEventType.ProtocolBulletinIssued, "BulletinIssued"),
					(WorkflowTriggerEventType.ProtocolBulletinWithdrawn, "BulletinWithdrawn"), (WorkflowTriggerEventType.ProtocolTranslationApproved, "TranslationApproved"),
					(WorkflowTriggerEventType.ProtocolPublicLinkChanged, "PublicLinkChanged")),
				new[]
				{
					V("ProtocolId", "int", "Protocol ID (0 for a category public link)", 42),
					V("ProtocolVersionId", "int", "Protocol version ID (0 when none)", 118),
					V("ProtocolCode", "string", "Protocol code", "SOG-12"),
					V("ProtocolNumber", "string", "Protocol number", "4.12"),
					V("ProtocolName", "string", "Protocol name (a category public link: the category's name)", "Mayday procedures"),
					V("ProtocolKind", "string", "Dispatch, Sog, ClinicalGuideline, Policy, Procedure, Reference or Bulletin", "Sog"),
					V("CategoryId", "int", "Protocol category ID (0 when none)", 3),
					V("VersionLabel", "string", "Version number (\"2.0\")", "2.0"),
					V("IsMajorVersion", "bool", "True for a major version (x.0)", true),
					V("RequiresAcknowledgment", "bool", "True when members must read and sign the version", true),
					V("EffectiveOn", "datetime", "When the version took effect (UTC)", SampleDay),
					V("ExpiresOn", "datetime", "When the version expires (UTC), when it does", SampleDay.AddDays(365)),
					V("NextReviewOn", "datetime", "When the protocol's next review is due (UTC)", SampleDay.AddDays(365)),
					V("ReviewStage", "string", "Review reminders: ThirtyDays, SevenDays or Overdue", "ThirtyDays"),
					V("Locale", "string", "Translation approved: the translation's language (\"es\")", "es"),
					V("TranslationId", "int", "Translation ID (0 when none)", 0),
					V("PublicLinkId", "int", "Public link ID (0 when none)", 0),
					V("PublicLinkKind", "string", "Public link: Protocol or Category", null),
					V("PublicLinkAction", "string", "Public link: Created or Revoked", null),
					V("EmbedEnabled", "bool", "Public link: whether it may be embedded on the department's site", false),
					V("OwnerUserId", "string", "The protocol owner's user ID", Officer),
					V("UserId", "string", "The member who acted (empty for the scheduled worker)", Officer),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("acknowledgment", typeof(ProtocolAcknowledgmentWorkflowEvent), "UserId",
				"Assigned, Completed or Overdue",
				Map((WorkflowTriggerEventType.ProtocolAcknowledgmentAssigned, "Assigned"), (WorkflowTriggerEventType.ProtocolAcknowledgmentCompleted, "Completed"),
					(WorkflowTriggerEventType.ProtocolAcknowledgmentOverdue, "Overdue")),
				new[]
				{
					V("AcknowledgmentId", "int", "Acknowledgment ID", 901),
					V("ProtocolId", "int", "Protocol ID", 42),
					V("ProtocolVersionId", "int", "Protocol version ID", 118),
					V("ProtocolCode", "string", "Protocol code", "SOG-12"),
					V("ProtocolName", "string", "Protocol name", "Mayday procedures"),
					V("VersionLabel", "string", "Version number", "2.0"),
					V("IsBulletin", "bool", "True for a priority bulletin", false),
					V("UserId", "string", "The member who owes (or signed) it", Member),
					V("AssignmentSource", "string", "Publish, AudienceEntry or Bulletin", "Publish"),
					V("AssignedOn", "datetime", "When it was assigned (UTC)", SampleDay.AddDays(-14)),
					V("DueOn", "datetime", "When it is due (UTC)", SampleDay.AddDays(-7)),
					V("CompletedOn", "datetime", "When it was signed and complete (UTC)", null),
					V("ReminderStage", "string", "Overdue: Overdue, or Escalated when the supervisor was told", "Overdue"),
					V("DaysOverdue", "int", "Days past the due date (overdue only)", 7),
					V("QuizRequired", "bool", "True when a quiz must be passed to complete it", false),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("dispatch_case", typeof(DispatchCaseWorkflowEvent), "UserId",
				"DeterminantChanged, ReviewSent or ReviewAcknowledged",
				Map((WorkflowTriggerEventType.CallDeterminantChanged, "DeterminantChanged"), (WorkflowTriggerEventType.DispatchCaseReviewSent, "ReviewSent"),
					(WorkflowTriggerEventType.DispatchCaseReviewAcknowledged, "ReviewAcknowledged")),
				new[]
				{
					V("CallId", "int", "Call ID", 1001),
					V("CallNumber", "string", "Call number", "26-1001"),
					V("CallProtocolId", "int", "The protocol's row on the call", 77),
					V("ProtocolId", "int", "Protocol ID", 42),
					V("ProtocolVersionId", "int", "Protocol version the call cites (0 when none)", 118),
					V("ProtocolCode", "string", "Protocol code", "EMD-26"),
					V("ProtocolName", "string", "Protocol name", "Sick person"),
					V("VersionLabel", "string", "Version number", "2.0"),
					V("Determinant", "string", "The determinant on the call now", "26-C-1"),
					V("PreviousDeterminant", "string", "Determinant changed: the determinant before", "26-A-1"),
					V("ComputedDeterminant", "string", "What the case flow reached on its own", "26-A-1"),
					V("Overridden", "bool", "True when a dispatcher's override stands", true),
					V("ReviewId", "int", "Dispatch case review ID (0 when none)", 0),
					V("ReviewScore", "int", "The review's overall score, 0-100 (0 when none)", 0),
					V("QuestionCount", "int", "Questions scored", 0),
					V("QuestionPoints", "int", "Question compliance points", 0),
					V("InstructionCount", "int", "Instructions scored", 0),
					V("InstructionPoints", "int", "Instruction compliance points", 0),
					V("UserId", "string", "The dispatcher (who changed the determinant, or whose case was reviewed)", Member),
					V("ReviewerUserId", "string", "The reviewing officer's user ID", null),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("exchange_listing", typeof(ExchangeListingWorkflowEvent), "UserId",
				"Reviewed",
				Map((WorkflowTriggerEventType.ExchangeListingReviewed, "Reviewed")),
				new[]
				{
					V("ListingId", "int", "Community exchange listing ID", 15),
					V("ItemKind", "string", "Protocol, Training, SkillSheet or LessonPlan", "Protocol"),
					V("ListingTitle", "string", "The listing's title", "Mayday procedures"),
					V("VersionLabel", "string", "The submitted version", "2.0"),
					V("Decision", "string", "Listed, Rejected or Removed", "Listed"),
					V("UserId", "string", "The member who submitted it", Officer),
					V("SubmittedOn", "datetime", "When it was submitted (UTC)", SampleDay.AddDays(-3)),
					V("ReviewedOn", "datetime", "When it was reviewed (UTC)", SampleDay),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("knowledge_gap", typeof(KnowledgeGapWorkflowEvent), null,
				"Recorded",
				Map((WorkflowTriggerEventType.KnowledgeGapRecorded, "Recorded")),
				new[]
				{
					V("KnowledgeGapId", "int", "Knowledge gap ID (the question itself is read in Knowledge, never sent)", 33),
					V("GapReason", "string", "NotCovered or ThumbsDown", "NotCovered"),
					V("Surface", "string", "Where it was asked: Library, Protocol, Training, Assistant, Call, Api, Help or Course", "Library"),
					V("NearestSourceCount", "int", "Sources that came close", 2),
					V("AskedOn", "datetime", "When it was asked (UTC)", SampleDay),
					V("OccurredOn", "datetime", "When it was recorded (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("sealed_document", typeof(DocumentSealingWorkflowEvent), "UserId",
				"Sealed or Failed",
				Map((WorkflowTriggerEventType.DocumentSealed, "Sealed"), (WorkflowTriggerEventType.DocumentSealingFailed, "Failed")),
				new[]
				{
					V("SealedDocumentId", "int", "Sealed document ID", 7),
					V("SubjectKind", "string", "What was sealed (ProtocolVersion, ProtocolBook, TimeReport, SkillEvaluation...)", "ProtocolVersion"),
					V("SubjectId", "string", "The sealed item's ID", "118"),
					V("DocumentTitle", "string", "The document's title", "SOG-12 Mayday procedures v2.0"),
					V("DocumentRole", "string", "Sealed, ProviderCompleted, ProviderAuditCertificate or SealedExternalPdf", "Sealed"),
					V("SignatureRequestId", "int", "The signature request it closes (0 when none)", 0),
					V("Status", "string", "Sealed or Failed", "Sealed"),
					V("PadesLevel", "string", "BaselineT, BaselineLT or BaselineLTA", "BaselineLTA"),
					V("Attempts", "int", "Sealing attempts made", 1),
					V("ErrorCode", "string", "Failed: a short error code", null),
					V("SealedOn", "datetime", "When it was sealed (UTC)", SampleDay),
					V("UserId", "string", "The member who asked for the document (empty for the system)", Officer),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("course_event", typeof(CourseWorkflowEvent), "UserId",
				"VersionPublished, Completed, AssessmentGraded, Assigned, AwaitingGrading or Retired",
				Map((WorkflowTriggerEventType.CourseVersionPublished, "VersionPublished"), (WorkflowTriggerEventType.CourseCompleted, "Completed"),
					(WorkflowTriggerEventType.AssessmentGraded, "AssessmentGraded"), (WorkflowTriggerEventType.CourseAssigned, "Assigned"),
					(WorkflowTriggerEventType.AssessmentAwaitingGrading, "AwaitingGrading"), (WorkflowTriggerEventType.CourseRetired, "Retired")),
				new[]
				{
					V("CourseId", "int", "LMS course ID", 31),
					V("CourseVersionId", "int", "LMS course version ID (0 when none)", 64),
					V("CourseCode", "string", "Course code", "SCBA-26"),
					V("CourseTitle", "string", "Course title", "SCBA refresher 2026"),
					V("VersionLabel", "string", "Version number", "1.0"),
					V("IsMajorVersion", "bool", "Version published: true for a major version", false),
					V("IsAcademy", "bool", "True for Resgrid Academy content", false),
					V("Hours", "decimal", "Credit hours", 2m),
					V("DueOn", "datetime", "Assigned: the due date the officer set (UTC), when there is one", null),
					V("UserId", "string", "The learner (empty for a version published or a course retired)", Member),
					V("ActorUserId", "string", "The member who published, assigned, graded or retired it", Officer),
					V("EnrollmentId", "int", "Enrollment ID (0 when none)", 512),
					V("AttemptId", "int", "Graded attempt ID (0 when none)", 0),
					V("LessonId", "int", "Graded lesson ID (0 when none)", 0),
					V("LessonTitle", "string", "Graded lesson title", null),
					V("Passed", "bool", "Assessment graded: whether it passed", true),
					V("Score", "decimal", "Assessment graded: the score (percent)", 85m),
					V("TestedOut", "bool", "Completed: true when a pre-test was passed instead", false),
					V("CertificationTypeId", "int", "The certification type a completion issues (0 when none)", 0),
					V("CompletedOn", "datetime", "When the course was completed (UTC)", SampleDay),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("skill_event", typeof(SkillWorkflowEvent), "UserId",
				"EvaluationCompleted or TaskBookClosed",
				Map((WorkflowTriggerEventType.SkillEvaluationCompleted, "EvaluationCompleted"), (WorkflowTriggerEventType.TaskBookClosed, "TaskBookClosed")),
				new[]
				{
					V("EvaluationId", "int", "Skill evaluation ID (0 when none)", 88),
					V("SkillSheetId", "int", "Skill sheet ID (0 when none)", 9),
					V("TaskBookId", "int", "Position task book ID (0 when none)", 0),
					V("SkillCode", "string", "Skill sheet code, or the task book's position code", "SS-SCBA"),
					V("SkillTitle", "string", "Evaluation title, or the task book's name", "SCBA donning"),
					V("UserId", "string", "The member evaluated, or the task book's trainee", Member),
					V("ActorUserId", "string", "The evaluator, or the certifying official", Officer),
					V("Result", "string", "Passed or Failed (evaluation); Certified, Expired or Cancelled (task book)", "Passed"),
					V("Score", "decimal", "Evaluation score (percent)", 92m),
					V("CriticalFailed", "bool", "True when a critical step failed", false),
					V("MemberCount", "int", "Members evaluated together", 1),
					V("CertificationTypeId", "int", "Task book: the certification type it certifies (0 when none)", 0),
					V("CompletedOn", "datetime", "When it was completed or closed (UTC)", SampleDay),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("enforcement", typeof(RequirementEnforcementWorkflowEvent), "UserId",
				"Undone",
				Map((WorkflowTriggerEventType.RequirementEnforcementUndone, "Undone")),
				new[]
				{
					V("EnforcementId", "int", "Enforcement ID", 14),
					V("RequirementId", "int", "Learning requirement ID", 12),
					V("RequirementName", "string", "Learning requirement name", "Annual SCBA refresher"),
					V("UserId", "string", "The member whose certification was restored", Member),
					V("CertificationId", "int", "The member's certification record ID", 301),
					V("CertificationTypeId", "int", "Certification type ID", 5),
					V("UndoReason", "string", "Completed, Lapse, NoLongerApplies or Manual", "Completed"),
					V("RestoredRoleCount", "int", "Roles given back", 1),
					V("EnforcedOn", "datetime", "When it was enforced (UTC)", SampleDay.AddDays(-10)),
					V("ActorUserId", "string", "The officer who undid it (empty for the system)", null),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("training_request", typeof(TrainingRequestWorkflowEvent), "UserId",
				"Submitted or Decided",
				Map((WorkflowTriggerEventType.TrainingRequestSubmitted, "Submitted"), (WorkflowTriggerEventType.TrainingRequestDecided, "Decided")),
				new[]
				{
					V("RequestId", "int", "Training request ID", 21),
					V("UserId", "string", "The requesting member", Member),
					V("RequestTitle", "string", "What the member asked to attend", "Fire Officer I"),
					V("Provider", "string", "Training provider", "State Fire Academy"),
					V("CourseCode", "string", "Course code", "FO-1"),
					V("CategoryId", "int", "Training category ID (0 when none)", 2),
					V("StartsOn", "datetime", "Start date", SampleDay.AddDays(30)),
					V("EndsOn", "datetime", "End date", SampleDay.AddDays(34)),
					V("Hours", "decimal", "Hours", 40m),
					V("TotalCost", "decimal", "Tuition, travel and other costs", 1250m),
					V("Currency", "string", "Currency", "USD"),
					V("Status", "string", "Submitted, Approved or Rejected", "Submitted"),
					V("AwaitingStep", "string", "Submitted: Supervisor or TrainingOfficer", "Supervisor"),
					V("DecidedByUserId", "string", "Decided: the approver's user ID", null),
					V("SubmittedOn", "datetime", "When it was submitted (UTC)", SampleDay),
					V("DecidedOn", "datetime", "When it was decided (UTC)", null),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("training_admin", typeof(TrainingAdminWorkflowEvent), "UserId",
				"AddonLapsed, AddonReactivated, DataExportReady or SeatsChanged",
				Map((WorkflowTriggerEventType.LmsAddonLapsed, "AddonLapsed"), (WorkflowTriggerEventType.LmsAddonReactivated, "AddonReactivated"),
					(WorkflowTriggerEventType.LmsDataExportReady, "DataExportReady"), (WorkflowTriggerEventType.LmsSeatsChanged, "SeatsChanged")),
				new[]
				{
					V("LapseReason", "string", "Lapsed: RenewalCancelled, PaymentFailed, Revoked or Unknown", "PaymentFailed"),
					V("LapsedSince", "datetime", "When the lapse began (UTC)", SampleDay.AddDays(-2)),
					V("PaidThrough", "datetime", "The add-on's paid-through date (UTC)", SampleDay.AddDays(-9)),
					V("Packs", "int", "Seat packs (seats changed by an officer: 0 when not read)", 2),
					V("PreviousPacks", "int", "Seats changed by a renewal: the packs before (0 otherwise)", 0),
					V("SeatsAdded", "int", "Seats changed: member seats enabled (a count, never who)", 3),
					V("SeatsRemoved", "int", "Seats changed: member seats disabled (a count, never who)", 0),
					V("EnabledSeats", "int", "Seats changed: member seats in use afterwards (0 when not read)", 18),
					V("ExportId", "int", "Data export ID (0 when none)", 0),
					V("ExportScope", "string", "Data export: Full or RecordsOnly", null),
					V("IncludesMedia", "bool", "Data export: true when course media is included", false),
					V("SizeBytes", "int", "Data export size in bytes", 0),
					V("ExpiresOn", "datetime", "Data export: when the download expires (UTC)", null),
					V("UserId", "string", "The member who asked for the export or changed the seats (empty otherwise)", null),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("signature_step", typeof(SignatureStepWorkflowEvent), "UserId",
				"Assigned",
				Map((WorkflowTriggerEventType.SignatureStepAssigned, "Assigned")),
				new[]
				{
					V("SignatureRequestId", "int", "Signature request ID", 42),
					V("SignerId", "int", "The step's signer row ID", 7),
					V("SubjectKind", "string", "What is signed (SkillEvaluation, TimeReport, ChecklistWitness...)", "SkillEvaluation"),
					V("SubjectId", "string", "The signed item's ID", "evaluation:88"),
					V("RequestTitle", "string", "The request's title", "SCBA donning evaluation"),
					V("StepOrder", "int", "The step's place in the order", 2),
					V("SignerRole", "string", "Member, CertifyingOfficial, Evaluator, Vendor or Recipient", "CertifyingOfficial"),
					V("UserId", "string", "The member whose turn it is", Officer),
					V("RequestedByUserId", "string", "The member who asked for the signatures", Member),
					V("ExpiresOn", "datetime", "When the request expires (UTC)", SampleDay.AddDays(14)),
					V("OccurredOn", "datetime", "When the turn started (UTC)", SampleDay)
				}),

			// Round 9A (275-278): certifications backed by training.
			new ContentWorkflowFamily("certification_event", typeof(CertificationTrainingWorkflowEvent), "UserId",
				"Issued, Renewed, RevokedByVoid or RequirementsDue",
				Map((WorkflowTriggerEventType.CertificationIssuedByTraining, "Issued"), (WorkflowTriggerEventType.CertificationRenewedByTraining, "Renewed"),
					(WorkflowTriggerEventType.CertificationRevokedByVoid, "RevokedByVoid"), (WorkflowTriggerEventType.CertificationRequirementsDue, "RequirementsDue")),
				new[]
				{
					V("CertificationId", "int", "The member's certification record ID", 301),
					V("CertificationTypeId", "int", "Certification type ID", 5),
					V("CertificationTypeName", "string", "Certification type name", "Vehicle operator"),
					V("CertificationTypeCode", "string", "Certification type code", "VEH-OP"),
					V("UserId", "string", "The holder", Member),
					V("Source", "string", "CourseCompletion, PathCompletion, RequirementSet or CycleAutoRenew", "RequirementSet"),
					V("Outcome", "string", "Issued, Renewed or NextCycle; a void: Withdrawn or Restored", "Renewed"),
					V("ExpiresOn", "datetime", "The expiry now (UTC date), when it expires", SampleDay.AddDays(365)),
					V("PreviousExpiresOn", "datetime", "The expiry before (UTC date), when there was one", SampleDay.AddDays(30)),
					V("ItemsDone", "int", "Requirement items complete", 4),
					V("ItemsTotal", "int", "Requirement items in the set (0 when no set is involved)", 4),
					V("DaysUntilExpiry", "int", "Requirements due: days until the expiry (negative in the grace period)", 30),
					V("IssuanceId", "int", "The issuance ledger row (0 when none)", 77),
					V("ActorUserId", "string", "The member who caused it (empty for the scheduled worker)", null),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			// Round 9B (280-285): members' training hours submissions with evidence, their optional review, and voided recorded completions.
			new ContentWorkflowFamily("training_submission", typeof(TrainingSubmissionWorkflowEvent), "UserId",
				"Received, Approved, Denied, InfoRequested or Resubmitted",
				Map((WorkflowTriggerEventType.TrainingSubmissionReceived, "Received"), (WorkflowTriggerEventType.TrainingSubmissionApproved, "Approved"),
					(WorkflowTriggerEventType.TrainingSubmissionDenied, "Denied"), (WorkflowTriggerEventType.TrainingSubmissionInfoRequested, "InfoRequested"),
					(WorkflowTriggerEventType.TrainingSubmissionResubmitted, "Resubmitted")),
				new[]
				{
					V("LearningRecordId", "int", "Transcript record ID of the submission", 1204),
					V("UserId", "string", "The member who submitted it", Member),
					V("RecordTitle", "string", "What the member completed", "Hazardous materials refresher"),
					V("CourseCode", "string", "Course or CE / CPD number", "HM-8-2026"),
					V("CategoryId", "int", "Training category ID (0 when none)", 2),
					V("Hours", "decimal", "Hours", 8m),
					V("CeHours", "decimal", "CE / CPD hours (0 when none)", 8m),
					V("ReviewState", "string", "AwaitingReview, InfoRequested, Approved, Denied or SelfReported", "AwaitingReview"),
					V("ReviewRequired", "bool", "Received: true when it waits for a reviewer, false when accepted as self-reported", true),
					V("EvidenceFiles", "int", "Evidence files attached (a count)", 2),
					V("ReviewerUserId", "string", "The reviewer who decided or asked for more information (empty otherwise)", null),
					V("CompletedOn", "datetime", "When the training was completed", SampleDay.AddDays(-3)),
					V("SubmittedOn", "datetime", "When it was (re)submitted (UTC)", SampleDay),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("completion_void", typeof(TrainingCompletionVoidedWorkflowEvent), "UserId",
				"Voided",
				Map((WorkflowTriggerEventType.TrainingCompletionRecordingVoided, "Voided")),
				new[]
				{
					V("RecordingId", "int", "Recorded completion ID", 88),
					V("UserId", "string", "The member whose recorded completion was voided", Member),
					V("VoidedByUserId", "string", "The member who voided it", Officer),
					V("TargetKind", "string", "Training (baseline training) or Course (LMS course)", "Training"),
					V("TargetId", "int", "Training or course ID", 31),
					V("TargetName", "string", "Training or course name", "CPR and AED recertification"),
					V("LearningRecordId", "int", "Transcript record ID (0 when none)", 1204),
					V("CompletedOn", "datetime", "The completion date that was voided", SampleDay.AddDays(-7)),
					V("OccurredOn", "datetime", "When it was voided (UTC)", SampleDay)
				}),

			// Round 10 A3 (286-295): protocol and course sets over Department Links, LMS sponsorship, and mutual-aid countersignature.
			new ContentWorkflowFamily("content_set", typeof(ContentSetWorkflowEvent), "ActorUserId",
				"UpdateArrived, UpdateApplied, Subscribed, ProposalReceived or ProposalAnswered",
				Map((WorkflowTriggerEventType.ContentSetUpdateArrived, "UpdateArrived"), (WorkflowTriggerEventType.ContentSetUpdateApplied, "UpdateApplied"),
					(WorkflowTriggerEventType.ContentSetSubscribed, "Subscribed"), (WorkflowTriggerEventType.ContentSetProposalReceived, "ProposalReceived"),
					(WorkflowTriggerEventType.ContentSetProposalAnswered, "ProposalAnswered")),
				new[]
				{
					V("SetId", "int", "Set ID", 12),
					V("SetName", "string", "Set name", "County EMS protocols"),
					V("SetKind", "string", "Protocols or Training", "Protocols"),
					V("ItemKind", "string", "Protocol, LmsCourse or Training (empty for a subscription)", "Protocol"),
					V("ItemTitle", "string", "The protocol, course or training", "Stroke"),
					V("PublisherDepartmentId", "int", "The publishing department ID", 40),
					V("SubscriberDepartmentId", "int", "The subscribing department ID", 1),
					V("VersionLabel", "string", "The publisher's version", "3.1"),
					V("EffectiveOn", "datetime", "When the update goes (or went) into force (UTC)", SampleDay.AddDays(2)),
					V("State", "string", "NewVersion or NewItem; Open, Accepted or Declined; Active", "NewVersion"),
					V("ActorUserId", "string", "The member who caused it (empty for the scheduled worker or an automatic acceptance)", Officer),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("lms_sponsorship", typeof(LmsSponsorshipWorkflowEvent), "ActorUserId",
				"Started or Ended",
				Map((WorkflowTriggerEventType.LmsSponsorshipStarted, "Started"), (WorkflowTriggerEventType.LmsSponsorshipEnded, "Ended")),
				new[]
				{
					V("SponsorshipId", "int", "Sponsorship ID", 5),
					V("SponsorDepartmentId", "int", "The department that pays", 40),
					V("SponsoredDepartmentId", "int", "The sponsored department", 1),
					V("OtherDepartment", "string", "The other party's department name", "Lakeside Volunteer Fire"),
					V("SeatLimit", "int", "The seat limit (0 when none)", 25),
					V("EndReason", "string", "Ended: EndedBySponsor, DeclinedBySponsored or LinkRevoked", null),
					V("ActorUserId", "string", "The member who caused it (empty for the scheduled worker)", Officer),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("mutual_aid_countersign", typeof(MutualAidCountersignWorkflowEvent), "ActorUserId",
				"Requested, Countersigned or Declined",
				Map((WorkflowTriggerEventType.MutualAidCountersignRequested, "Requested"), (WorkflowTriggerEventType.MutualAidAgreementCountersigned, "Countersigned"),
					(WorkflowTriggerEventType.MutualAidCountersignDeclined, "Declined")),
				new[]
				{
					V("MutualAidAgreementId", "int", "Agreement ID (in the originating department)", 9),
					V("AgreementTitle", "string", "Agreement title", "Automatic aid: north county"),
					V("OriginDepartmentId", "int", "The department whose agreement it is", 1),
					V("PartnerDepartmentId", "int", "The partner department that countersigns", 40),
					V("DepartmentLinkId", "int", "The Department Link it was routed over", 3),
					V("SignatureRequestId", "int", "The partner's signature request (0 when none)", 77),
					V("State", "string", "Requested, Countersigned, Declined or Lapsed", "Requested"),
					V("ActorUserId", "string", "The member who caused it (empty for the scheduled worker)", Officer),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				}),

			new ContentWorkflowFamily("deployment_partner_sign", typeof(RecordDeploymentPartnerWorkflowEvent), "ActorUserId",
				"Requested or Decided",
				Map((WorkflowTriggerEventType.RecordDeploymentPartnerSignRequested, "Requested"), (WorkflowTriggerEventType.RecordDeploymentPartnerSignDecided, "Decided")),
				new[]
				{
					V("OriginDepartmentId", "int", "The department filling the order", 1),
					V("PartnerDepartmentId", "int", "The linked partner department that accepts the assignment", 40),
					V("OrderNumber", "string", "The order number", "CA-XXX-012345"),
					V("RequestNumber", "string", "The request number the fill answers", "O-12"),
					V("ResourceType", "string", "The resource type", "Type 3 Engine"),
					V("SignatureRequestId", "int", "The partner's signature request (0 when none)", 77),
					V("State", "string", "Requested, Signed, Declined or Lapsed", "Requested"),
					V("ActorUserId", "string", "The member who caused it (empty for the scheduled worker)", Officer),
					V("OccurredOn", "datetime", "When it happened (UTC)", SampleDay)
				})
		};

		private static readonly Dictionary<WorkflowTriggerEventType, ContentWorkflowFamily> ByTrigger =
			Families.SelectMany(f => f.Changes.Keys.Select(t => (t, f))).ToDictionary(p => p.t, p => p.f);

		/// <summary>Every round-7C trigger.</summary>
		public static IReadOnlyCollection<WorkflowTriggerEventType> Triggers => ByTrigger.Keys;

		public static bool IsContent(int trigger) => ByTrigger.ContainsKey((WorkflowTriggerEventType)trigger);

		/// <summary>The family a trigger belongs to, or null.</summary>
		public static ContentWorkflowFamily For(WorkflowTriggerEventType trigger) => ByTrigger.TryGetValue(trigger, out var family) ? family : null;

		/// <summary>The variable catalog entries of a round-7C trigger (empty for any other).</summary>
		public static IEnumerable<TemplateVariableDescriptor> Catalog(WorkflowTriggerEventType trigger)
		{
			var family = For(trigger);
			if (family == null)
				yield break;

			yield return new TemplateVariableDescriptor(family.Prefix + ".change", family.ChangeDescription, "string", false);
			foreach (var variable in family.Variables)
				yield return new TemplateVariableDescriptor(family.Prefix + "." + variable.Variable, variable.Description, variable.DataType, false);
		}

		/// <summary>"ProtocolVersionId" to "protocol_version_id".</summary>
		public static string ToSnakeCase(string name)
		{
			if (string.IsNullOrEmpty(name))
				return name;

			var builder = new StringBuilder(name.Length + 8);
			for (var i = 0; i < name.Length; i++)
			{
				var c = name[i];
				if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
					builder.Append('_');
				builder.Append(char.ToLowerInvariant(c));
			}

			return builder.ToString();
		}
	}
}
