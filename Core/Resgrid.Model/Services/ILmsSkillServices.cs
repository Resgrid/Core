using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T4 part 2 (round 5C): skill sheets, evaluations (S3 evaluator mode), position task books, the skill notification digest
	// (worker 96) and the post-class evaluation hook class sessions call. Refusals throw InvalidOperationException with an
	// "lms_skill_*" / "lms_task_book_*" code (the pages show its localized text).

	/// <summary>Skill sheet authoring on the content core: the sheet, its draft version, publishing and the template library.</summary>
	public interface ILmsSkillSheetService
	{
		Task<List<LmsSkillSheet>> GetSheetsAsync(int departmentId, bool includeDeleted = false);

		Task<LmsSkillSheet> GetSheetAsync(int departmentId, int lmsSkillSheetId);

		Task<List<LmsSkillSheetVersion>> GetVersionsAsync(int departmentId, int lmsSkillSheetId);

		Task<LmsSkillSheetVersion> GetVersionAsync(int departmentId, int lmsSkillSheetVersionId);

		/// <summary>The version in force (null before the first publish).</summary>
		Task<LmsSkillSheetVersion> GetPublishedVersionAsync(int departmentId, int lmsSkillSheetId);

		/// <summary>The working draft (null when the sheet has none).</summary>
		Task<LmsSkillSheetVersion> GetDraftAsync(int departmentId, int lmsSkillSheetId);

		/// <summary>A new sheet with an empty draft (LMS managers, add-on active).</summary>
		Task<LmsSkillSheet> CreateAsync(LmsActor actor, LmsSkillSheetInput input, CancellationToken cancellationToken = default);

		/// <summary>A new sheet from a catalog starter, its steps in a draft ready to adapt and publish.</summary>
		Task<LmsSkillSheet> CreateFromTemplateAsync(LmsActor actor, string templateKey, CancellationToken cancellationToken = default);

		Task<LmsSkillSheet> SaveSheetAsync(LmsActor actor, int lmsSkillSheetId, LmsSkillSheetInput input, CancellationToken cancellationToken = default);

		/// <summary>Saves the draft's steps and grading (a draft is started from the published version when there is none).</summary>
		Task<LmsSkillSheetVersion> SaveDraftAsync(LmsActor actor, int lmsSkillSheetId, LmsSkillSheetDraftInput input, CancellationToken cancellationToken = default);

		/// <summary>Publishes the draft (major or minor) with a change summary; the previous version is superseded.</summary>
		Task<LmsSkillSheetVersion> PublishAsync(LmsActor actor, int lmsSkillSheetId, bool major, string changeSummary, CancellationToken cancellationToken = default);

		/// <summary>Retires the sheet (soft delete: evaluations keep it).</summary>
		Task<bool> DeleteAsync(LmsActor actor, int lmsSkillSheetId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Skill evaluations (T4 item 2): start (eligibility, self-evaluation, attempts), grade (answers, Pass All, timers, annotations,
	/// evidence), crew-wide evaluation with split, submit (fixes the result, the evaluator picks the next signer, the S3 evaluator request),
	/// offline submission with an idempotency key, completion (learning records once, sealed evidence) and void.
	/// </summary>
	public interface ILmsSkillEvaluationService
	{
		Task<LmsSkillEvaluationView> GetViewAsync(int departmentId, int lmsSkillEvaluationId);

		Task<List<LmsSkillEvaluation>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, int? lmsSkillSheetId, int? status, int take = 500);

		Task<List<LmsSkillEvaluation>> GetOpenForEvaluatorAsync(int departmentId, string userId);

		Task<List<LmsSkillEvaluation>> GetForMemberAsync(int departmentId, string userId);

		Task<List<LmsSkillEvaluationMember>> GetMembersAsync(int departmentId, IEnumerable<int> lmsSkillEvaluationIds);

		/// <summary>Whether the member may evaluate on the sheet (its rule, or LMS managers when it has none).</summary>
		Task<bool> CanEvaluateAsync(LmsActor actor, LmsSkillSheet sheet);

		/// <summary>Active members the sheet's next-signer rule allows (the picker).</summary>
		Task<List<string>> GetNextSignerCandidatesAsync(int departmentId, LmsSkillSheet sheet);

		Task<LmsSkillEvaluation> StartAsync(LmsActor actor, LmsSkillEvaluationStart input, CancellationToken cancellationToken = default);

		/// <summary>Saves answers, notes and annotations of an evaluation in progress (optimistic RowVersion); <paramref name="passAll"/> fills Pass All first.</summary>
		Task<LmsSkillEvaluation> SaveAnswersAsync(LmsActor actor, int lmsSkillEvaluationId, long expectedRowVersion, LmsSkillAnswers answers, string notes, bool passAll,
			CancellationToken cancellationToken = default);

		Task<LmsSkillEvaluationFile> AddEvidenceAsync(LmsActor actor, int lmsSkillEvaluationId, string stepKey, string fileName, string contentType, byte[] data,
			CancellationToken cancellationToken = default);

		Task<LmsSkillEvaluationFile> GetEvidenceAsync(int departmentId, int lmsSkillEvaluationId, int lmsSkillEvaluationFileId);

		/// <summary>Moves one member of a crew evaluation in progress to their own evaluation (same answers so far, graded separately from now).</summary>
		Task<LmsSkillEvaluation> SplitMemberAsync(LmsActor actor, int lmsSkillEvaluationId, string userId, CancellationToken cancellationToken = default);

		/// <summary>Fixes the result and creates the S3 evaluator request (the evaluator signs at L2, then the next signer).</summary>
		Task<LmsSkillEvaluation> SubmitAsync(LmsActor actor, int lmsSkillEvaluationId, LmsSkillEvaluationSubmit input, CancellationToken cancellationToken = default);

		/// <summary>An offline evaluation in one call; a replay of the same idempotency key returns the evaluation the first call made.</summary>
		Task<LmsSkillEvaluation> SubmitOfflineAsync(LmsActor actor, LmsSkillOfflineSubmission submission, CancellationToken cancellationToken = default);

		Task<LmsSkillEvaluation> VoidAsync(LmsActor actor, int lmsSkillEvaluationId, string reason, CancellationToken cancellationToken = default);

		/// <summary>The completed S3 request's write-back (idempotent): status Completed, learning records for passed members, notices.</summary>
		Task<bool> CompleteFromSignatureAsync(SignatureRequestRecord request, CancellationToken cancellationToken = default);

		/// <summary>The evaluation as the signing layer sees it (hash over the submitted evaluation, the evidence document); null before submission.</summary>
		Task<SignatureSubjectInfo> DescribeForSignatureAsync(int departmentId, int lmsSkillEvaluationId);

		/// <summary>The sealed evidence PDF's bytes: null while sealing is pending or failed (never an unsealed copy, fails closed).</summary>
		Task<byte[]> GetSealedPdfAsync(int departmentId, int lmsSkillEvaluationId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The post-class evaluation hook class sessions (T4 classes, stream 5B) call: one crew evaluation of the roster on a skill sheet, linked to
	/// the session by its reference. 5B owns sessions; this stream owns the evaluation.
	/// </summary>
	public interface ILmsPostClassEvaluationHook
	{
		Task<LmsSkillEvaluation> StartPostClassEvaluationAsync(LmsPostClassEvaluationRequest request, CancellationToken cancellationToken = default);

		Task<List<LmsSkillEvaluation>> GetForClassSessionAsync(int departmentId, string classSessionRef);
	}

	/// <summary>Position task books (T4 item 3, NWCG model): definitions and starters, initiation, progress, certification and validity.</summary>
	public interface ILmsTaskBookService
	{
		Task<List<LmsTaskBookDefinition>> GetDefinitionsAsync(int departmentId);

		Task<LmsTaskBookDefinition> GetDefinitionAsync(int departmentId, int lmsTaskBookDefinitionId);

		Task<LmsTaskBookDefinition> SaveDefinitionAsync(LmsActor actor, LmsTaskBookDefinition definition, IReadOnlyCollection<LmsTaskBookTask> tasks, CancellationToken cancellationToken = default);

		Task<LmsTaskBookDefinition> CreateFromTemplateAsync(LmsActor actor, string templateKey, CancellationToken cancellationToken = default);

		Task<bool> DeleteDefinitionAsync(LmsActor actor, int lmsTaskBookDefinitionId, CancellationToken cancellationToken = default);

		Task<List<LmsTaskBook>> GetTaskBooksAsync(int departmentId, bool openOnly);

		Task<List<LmsTaskBook>> GetForMemberAsync(int departmentId, string userId);

		Task<LmsTaskBookView> GetViewAsync(int departmentId, int lmsTaskBookId);

		/// <summary>Initiates a member's book with a date: the tasks are snapshotted, and a Trainee certification record is opened when the definition names a type.</summary>
		Task<LmsTaskBook> InitiateAsync(LmsActor actor, int lmsTaskBookDefinitionId, string userId, DateTime initiatedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Every task complete: the final evaluator recommends, then the certifying official certifies (one S3 ordered request).</summary>
		Task<LmsTaskBook> RequestCertificationAsync(LmsActor actor, int lmsTaskBookId, string finalEvaluatorUserId, string certifyingOfficialUserId, string culture,
			CancellationToken cancellationToken = default);

		Task<LmsTaskBook> CancelAsync(LmsActor actor, int lmsTaskBookId, string reason, CancellationToken cancellationToken = default);

		/// <summary>The completed S3 request's write-back (idempotent): Certified, the record Trainee → Active, a TaskBookSignoff learning record.</summary>
		Task<bool> CertifyFromSignatureAsync(SignatureRequestRecord request, CancellationToken cancellationToken = default);

		Task<SignatureSubjectInfo> DescribeForSignatureAsync(int departmentId, int lmsTaskBookId);

		/// <summary>Worker 96: expires open books past their validity (a due-work query, no department sweep). Returns the count expired.</summary>
		Task<int> ExpireDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>A task was signed by a completed evaluation (notices; nothing changes state here).</summary>
		Task OnTaskEvaluatedAsync(LmsSkillEvaluation evaluation, CancellationToken cancellationToken = default);
	}

	/// <summary>The skill notification digest (EventTypes 70-71): queue by the department's mode, worker 96 sends due notices.</summary>
	public interface ILmsSkillNotificationService
	{
		/// <summary>Queues one notice per recipient (members and/or admins per the settings), due now or at the digest time.</summary>
		Task<int> QueueAsync(int departmentId, LmsSkillNotificationKinds kind, string text, IEnumerable<string> memberUserIds, bool includeOfficers,
			CancellationToken cancellationToken = default);

		/// <summary>Worker 96: due notices only (no department sweep), claimed first, inactive members skipped, one message per recipient.</summary>
		Task<int> SendDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsSkillSettingsService
	{
		Task<LmsSkillSettings> GetAsync(int departmentId);

		Task<LmsSkillSettings> SaveAsync(int departmentId, LmsSkillSettings settings, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>Skill sheets and task-book tasks as competency subjects (4C's frameworks): existence and a member's evidence.</summary>
	public interface ILmsSkillCompetencySubjects
	{
		Task<bool> ExistsAsync(int departmentId, LmsCompetencySubjectKinds kind, string subjectId);

		/// <summary>The member's passed, completed evaluations as evidence for SkillSheet (sheet id) and TaskBookTask ("{definitionId}:{taskKey}") tags.</summary>
		Task<List<LmsCompetencyEvidence>> GetEvidenceAsync(int departmentId, string userId);
	}
}
