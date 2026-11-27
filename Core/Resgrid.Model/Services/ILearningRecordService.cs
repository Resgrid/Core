using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The writers of the one transcript (Training T1, §12.2): every source turns its event into learning records through
	/// here, idempotently (R-T1), with one session counted once (R-T2), void-and-supersede when a source changes (R-T4) and
	/// certification credits derived through the category credit mappings (R-T5). Baseline (free). Callers treat a failure as
	/// non-fatal: the source's own save already happened and worker 85 / a re-save writes the record again.
	/// </summary>
	public interface ILearningRecordWriter
	{
		/// <summary>
		/// The core write for one source instance: stores <paramref name="drafts"/> (one per member and component), leaves an
		/// unchanged record alone, voids and supersedes a changed one, voids records whose member or component is gone.
		/// </summary>
		Task<LearningRecordWriteResult> WriteSourceAsync(int departmentId, LearningRecordSourceKinds kind, string sourceRefId,
			IReadOnlyCollection<LearningRecordDraft> drafts, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>Voids every live record of a source instance (the log, check-in or credit was deleted).</summary>
		Task<LearningRecordWriteResult> VoidSourceAsync(int departmentId, LearningRecordSourceKinds kind, string sourceRefId, string reason,
			string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>The member's completions of a baseline training: the live row and every completed cycle in the T0 history.</summary>
		Task<LearningRecordWriteResult> WriteCourseCompletionAsync(int departmentId, int trainingId, string userId, bool isBackfill = false,
			CancellationToken cancellationToken = default);

		/// <summary>A legacy Training Record (Log of type Training, participants loaded): one record per participant.</summary>
		Task<LearningRecordWriteResult> WriteTrainingLogAsync(Log log, string actingUserId, bool isBackfill = false, CancellationToken cancellationToken = default);

		Task<LearningRecordWriteResult> RemoveTrainingLogAsync(int departmentId, int logId, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>A Records (RMS) training record: its finalized revision's participants, or a void when it was voided or deleted.</summary>
		Task<LearningRecordWriteResult> WriteRecordsTrainingAsync(int departmentId, string rmsRecordId, string actingUserId, bool isBackfill = false,
			CancellationToken cancellationToken = default);

		/// <summary>Attendance on a calendar item whose type counts as training (R-T9); a check-in without check-out is Pending.</summary>
		Task<LearningRecordWriteResult> WriteCalendarCheckInAsync(CalendarItemCheckIn checkIn, string actingUserId, CancellationToken cancellationToken = default);

		Task<LearningRecordWriteResult> RemoveCalendarCheckInAsync(CalendarItemCheckIn checkIn, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>A credit typed in by hand on a certification record writes a ManualCredit record (never a derived credit).</summary>
		Task<LearningRecordWriteResult> WriteManualCreditAsync(PersonnelCertificationCredit credit, string userId, CancellationToken cancellationToken = default);

		Task<LearningRecordWriteResult> RemoveManualCreditAsync(PersonnelCertificationCredit credit, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>Adds the derived credits a verified record is due (mapped category, enabled since its date, its cycle); idempotent.</summary>
		Task<int> ApplyCreditsAsync(LearningRecord record, bool includeBackfill, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>Removes the credits derived from a record (it was voided, rejected or re-categorized).</summary>
		Task<int> RemoveCreditsAsync(LearningRecord record, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Round 9A: tells the registered <see cref="ILearningRecordObserver"/>s that records now count (<paramref name="written"/>) or were
		/// voided, after the change is stored. Backfilled records are left out. Non-fatal: an observer's failure is logged. The writer calls
		/// it itself at the end of every write and void; the other void paths (an officer's void, a merged duplicate, a recorded completion's
		/// void) call it once their whole change is stored.
		/// </summary>
		Task NotifyChangedAsync(int departmentId, IReadOnlyCollection<LearningRecord> written, IReadOnlyCollection<LearningRecord> voided, string actingUserId,
			CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Round 9A seam (baseline): told after learning records became countable or were voided. The LMS layer registers its certification
	/// engine here (a requirement set issues or renews when its last item completes; a void takes back what a voided completion issued), so
	/// baseline code never references LMS types (§17.3). Observers run after the change is stored and must not throw for routine misses.
	/// </summary>
	public interface ILearningRecordObserver
	{
		Task OnLearningRecordsChangedAsync(LearningRecordChange change, CancellationToken cancellationToken = default);
	}

	/// <summary>Transcript reads, officer actions, categories, credit mappings, settings, reports and history backfill (Training T1).</summary>
	public interface ILearningRecordService
	{
		// ---- Reads ------------------------------------------------------------------------------------------------------

		Task<List<LearningRecord>> QueryAsync(LearningRecordQuery query);

		/// <summary>One member's transcript (newest first). Voided records only when <paramref name="includeVoided"/> (the audit view).</summary>
		Task<List<LearningRecord>> GetTranscriptAsync(int departmentId, string userId, DateTime? fromUtc, DateTime? toUtc, IEnumerable<int> categoryIds,
			IEnumerable<int> sourceKinds, bool includeVoided);

		Task<LearningRecord> GetRecordAsync(int departmentId, int learningRecordId);

		/// <summary>The record a record replaced and the ones that replaced it (the audit chain, R-T4).</summary>
		Task<List<LearningRecord>> GetHistoryChainAsync(int departmentId, int learningRecordId);

		/// <summary>Pending records waiting for an officer: external submissions and open check-ins.</summary>
		Task<List<LearningRecord>> GetPendingAsync(int departmentId);

		Task<LearningRecordFile> GetEvidenceFileAsync(int departmentId, int learningRecordFileId);

		// ---- Member and officer actions ---------------------------------------------------------------------------------

		/// <summary>
		/// A member submits an outside certificate with hours (and optional evidence); it is Pending until an officer verifies
		/// it. Refused (null) when the department switched member submissions off.
		/// </summary>
		Task<LearningRecord> SubmitExternalAsync(int departmentId, string userId, LearningRecordDraft draft, string fileName, string contentType, byte[] data,
			CancellationToken cancellationToken = default);

		/// <summary>An officer verifies a Pending record (never their own); credits follow. Hours of an open check-in may be set here.</summary>
		Task<LearningRecord> VerifyAsync(int departmentId, int learningRecordId, string officerUserId, string note, decimal? hours,
			CancellationToken cancellationToken = default);

		Task<LearningRecord> RejectAsync(int departmentId, int learningRecordId, string officerUserId, string note, CancellationToken cancellationToken = default);

		// ---- Round 9B: hours submissions with evidence and their optional review (through ITrainingSubmissionService) --------------------

		/// <summary>
		/// A member submits hours with evidence files (each checked, cleaned and scanned, see TrainingEvidencePolicy): Pending and waiting
		/// for a reviewer when the department's review settings ask for it (TrainingSubmissionRules.RequiresReview), otherwise Verified at
		/// once as self-reported (credits follow). Null when the department switched member submissions off.
		/// </summary>
		Task<LearningRecord> SubmitExternalAsync(int departmentId, string userId, LearningRecordDraft draft, IReadOnlyList<TrainingEvidenceUpload> evidence,
			CancellationToken cancellationToken = default);

		/// <summary>A reviewer asks the member for more information (never on their own submission); the submission waits for the member.</summary>
		Task<LearningRecord> RequestInfoAsync(int departmentId, int learningRecordId, string officerUserId, string note, CancellationToken cancellationToken = default);

		/// <summary>
		/// The member resubmits their own pending submission with more evidence and corrections; it waits for a reviewer again (SubmittedOn
		/// is now).
		/// </summary>
		Task<LearningRecord> ResubmitAsync(int departmentId, int learningRecordId, string userId, TrainingSubmissionCorrection correction,
			IReadOnlyList<TrainingEvidenceUpload> evidence, CancellationToken cancellationToken = default);

		/// <summary>The evidence of these submissions, without the bytes.</summary>
		Task<List<LearningRecordFile>> GetEvidenceFilesAsync(int departmentId, IEnumerable<int> learningRecordIds);

		/// <summary>An officer voids a record with a reason (a correction is a new record); its credits are removed.</summary>
		Task<bool> VoidAsync(int departmentId, int learningRecordId, string officerUserId, string reason, CancellationToken cancellationToken = default);

		/// <summary>Keep both, or merge (void this one as the same session as its duplicate).</summary>
		Task<LearningRecord> ResolveDuplicateAsync(int departmentId, int learningRecordId, LearningRecordDuplicateResolutions resolution, string officerUserId,
			CancellationToken cancellationToken = default);

		// ---- Categories -------------------------------------------------------------------------------------------------

		Task<List<TrainingCategory>> GetCategoriesAsync(int departmentId, bool includeDeleted = false);

		Task<TrainingCategory> SaveCategoryAsync(TrainingCategory category, string userId, CancellationToken cancellationToken = default);

		/// <summary>Soft delete: records keep the id and show it as removed.</summary>
		Task<bool> DeleteCategoryAsync(int departmentId, int trainingCategoryId, string userId, CancellationToken cancellationToken = default);

		/// <summary>Adds the template's categories the department does not have yet (matched by code); returns the count added.</summary>
		Task<int> ApplyCategoryTemplateAsync(int departmentId, string templateKey, string userId, CancellationToken cancellationToken = default);

		/// <summary>As above, the categories' names and descriptions in a language (round 9C; English when none ships). Codes stay.</summary>
		Task<int> ApplyCategoryTemplateAsync(int departmentId, string templateKey, string userId, string culture, CancellationToken cancellationToken = default);

		Task<List<LearningRecordGroup>> GetUncategorizedGroupsAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc);

		/// <summary>
		/// Bulk categorize (R-T3): every uncategorized record (not voided) matching the title and/or course code in the window
		/// gets the category; or the listed record ids. Derived credits follow for records that are not backfilled.
		/// </summary>
		Task<int> CategorizeAsync(int departmentId, int categoryId, string title, string courseCode, DateTime? fromUtc, DateTime? toUtc, IEnumerable<int> learningRecordIds,
			string userId, CancellationToken cancellationToken = default);

		// ---- Credit mappings and history ---------------------------------------------------------------------------------

		Task<List<CategoryCreditMapping>> GetCreditMappingsAsync(int departmentId);

		/// <summary>Adds or changes a mapping; turning it on stamps EnabledOn (credits flow from that date, T1 item 4).</summary>
		Task<CategoryCreditMapping> SaveCreditMappingAsync(CategoryCreditMapping mapping, string userId, CancellationToken cancellationToken = default);

		Task<bool> DeleteCreditMappingAsync(int departmentId, int categoryCreditMappingId, string userId, CancellationToken cancellationToken = default);

		/// <summary>"Apply history to credits" preview for one certification type: proposed credits beside the existing ones (R-T3).</summary>
		Task<HistoryCreditPreview> PreviewHistoryCreditsAsync(int departmentId, int departmentCertificationTypeId);

		/// <summary>Writes the proposed credits the officer kept (null = all proposed); returns the count written.</summary>
		Task<int> ApplyHistoryCreditsAsync(int departmentId, int departmentCertificationTypeId, IEnumerable<int> learningRecordIds, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>Hours toward renewal per certification record (T1 item 4).</summary>
		Task<Dictionary<int, CertificationRenewalHours>> GetRenewalHoursAsync(int departmentId, IEnumerable<int> personnelCertificationIds);

		// ---- Settings and reports ----------------------------------------------------------------------------------------

		Task<TrainingRecordSettings> GetSettingsAsync(int departmentId);

		Task<TrainingRecordSettings> SaveSettingsAsync(int departmentId, TrainingRecordSettings settings, string userId, CancellationToken cancellationToken = default);

		/// <summary>Hours by category, member or month (free). Former members only with the switch (R-T13).</summary>
		Task<TrainingHoursReport> GetHoursReportAsync(TrainingHoursReportQuery query);

		/// <summary>The minimum date a record is kept to after its member leaves (§14), from its category or the department default; null for none.</summary>
		DateTime? RetainedUntil(LearningRecord record, TrainingCategory category, TrainingRecordSettings settings, DateTime? memberLeftOn);

		// ---- Backfill (R-T3) ----------------------------------------------------------------------------------------------

		/// <summary>Queues a backfill (or a dry-run count) for worker 85; returns the running job when one is already queued.</summary>
		Task<LearningRecordBackfill> RequestBackfillAsync(int departmentId, bool dryRun, string userId, CancellationToken cancellationToken = default);

		Task<LearningRecordBackfill> GetLatestBackfillAsync(int departmentId);
	}

	/// <summary>Runs history backfill jobs (worker 85).</summary>
	public interface ILearningRecordBackfillService
	{
		/// <summary>Runs (or resumes) one claimed job until it finishes or <paramref name="budget"/> sources were handled.</summary>
		Task<LearningRecordBackfill> RunAsync(LearningRecordBackfill backfill, int budget, CancellationToken cancellationToken = default);
	}

	/// <summary>History import (R-C1, free): CSV of completions with a dry run and a member-match review.</summary>
	public interface ITrainingHistoryImportService
	{
		/// <summary>The CSV template (header row and one example), UTF-8 with a byte order mark.</summary>
		byte[] Template();

		/// <summary>
		/// Parses and matches the file without writing anything. <paramref name="memberOverrides"/> maps a row number to the
		/// user id the officer picked on the review screen.
		/// </summary>
		Task<TrainingHistoryImportResult> DryRunAsync(int departmentId, byte[] file, IReadOnlyDictionary<int, string> memberOverrides);

		/// <summary>Writes the importable rows as Import learning records, and the certification records and credits they name.</summary>
		Task<TrainingHistoryImportResult> ImportAsync(int departmentId, byte[] file, IReadOnlyDictionary<int, string> memberOverrides, string userId,
			CancellationToken cancellationToken = default);
	}

	/// <summary>What a member, an LMS item or a sponsored set asks the gate to run.</summary>
	public enum LmsRunnableOrigins
	{
		/// <summary>The department's own LMS content.</summary>
		Department = 0,
		/// <summary>Resgrid system-published content (Academy): runs for everyone (R-S2).</summary>
		SystemPublished = 1,
		/// <summary>A publisher's sponsored set (T5 item 2a). Stubbed in T1: not sponsored until A1/T5 fill it in.</summary>
		Sponsored = 2
	}

	/// <summary>
	/// LMS add-on gate (§17.3), cloned from ReadinessAccessService / EnhancedAiAccessService: the Training.Lms operator flag,
	/// the Trainings module switch and a PaymentAddons window for PlanAddonTypes.Lms, with no entitlement cache. Fails closed.
	/// Never called from a sweep (R-L9): workers query for departments with due work first, then ask once per department.
	/// </summary>
	public interface ILmsAccessService
	{
		/// <summary>The operator flag is on for the department and its Trainings module is on (no billing call).</summary>
		Task<bool> IsFlagEnabledAsync(int departmentId);

		/// <summary>The department's Trainings module is on (no flag, no billing): all system-published Academy content needs (A1).</summary>
		Task<bool> IsTrainingModuleOnAsync(int departmentId);

		/// <summary>May author and manage LMS content and see the LMS reports: flag, module and an active add-on.</summary>
		Task<bool> CanAuthorAndManageAsync(int departmentId);

		/// <summary>May run this item: the add-on, or system-published content (every department with the Trainings module on, A1), or a sponsored set (stub).</summary>
		Task<bool> CanRunItemAsync(int departmentId, LmsRunnableOrigins origin, int? sponsorDepartmentId = null);

		/// <summary>
		/// The add-on works for the department: a running Lms PaymentAddons window, or a failed renewal still inside the dunning
		/// grace (lapse rule 13). True, false, or null when billing could not answer.
		/// </summary>
		Task<bool?> GetActiveAddonStateAsync(int departmentId);

		/// <summary>
		/// The add-on's state and packs from one billing read (no flag check): active, in dunning grace, lapsed, none, or unknown.
		/// Records what it saw (paid-through date, packs, a lapse opening or closing) only when something changed.
		/// </summary>
		Task<LmsSubscriptionSnapshot> GetSubscriptionAsync(int departmentId);

		/// <summary>
		/// May this member be trained through LMS features (enrolled in a course, class, path, skill sheet or task book, counted by
		/// LMS requirements): flag, module, a working add-on and a licensed seat (§17.4 seat rules). A member without one keeps the
		/// whole free baseline. Authoring, teaching, evaluating and reporting never need a seat; they use
		/// <see cref="CanAuthorAndManageAsync"/>. One member in one request: a report, an enrolment list or a requirement run asks
		/// ILmsSeatService.GetLicensedUserIdsAsync once per department instead (each call here is a billing read, R-L9).
		/// </summary>
		Task<bool> CanBeTrainedAsync(int departmentId, string userId);

		/// <summary>
		/// Round 10 A7 (owner decision 2026-11-26): the members who may act as learners on department LMS content (take a course, a sponsor's
		/// managed copy, an assessment, hosted media, a lesson file, a survey, a class), which every learner path in the LMS services asks before
		/// the action runs. Unlike <see cref="CanBeTrainedAsync"/> it is lapse-aware: while the add-on works, the licensed seats (the sponsor's
		/// capacity for a sponsored department); while it is lapsed (or billing cannot say, or a sponsorship stopped paying), the members who held
		/// a seat (lapse rule 1: nothing is removed), so the lapse rules (assigned work until its due date, rule 4) keep working for them. Empty
		/// with the flag off; fails closed. One billing read; a worker asks once per department per run (R-L9).
		/// </summary>
		Task<HashSet<string>> GetLearnerSeatHoldersAsync(int departmentId);
	}

	/// <summary>
	/// LMS add-on features built dark in wave 2 (§24, §25.4a item 7): the formal ISO FSRS and NREMT NCCP reports, certificate
	/// verification codes and the public verify page, and custom certificate templates. Consumes baseline services; nothing
	/// baseline calls it (§17.3). Callers check <see cref="ILmsAccessService"/> first.
	/// </summary>
	public interface ILmsTrainingService
	{
		Task<IsoFsrsReport> GetIsoFsrsReportAsync(int departmentId, DateTime fromUtc, DateTime toUtc, bool includeFormerMembers);

		Task<NccpProgressReport> GetNccpProgressAsync(int departmentId, bool includeFormerMembers, DateTime utcNow);

		/// <summary>The record's verification code, issuing one when it has none (Verified records only).</summary>
		Task<string> EnsureVerificationCodeAsync(int departmentId, int learningRecordId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The public answer for a code: name, course, date and status only. Keeps working after the add-on lapses (lapse rule
		/// 9); only the operator flag hides it. Null for an unknown code.
		/// </summary>
		Task<CertificateVerification> VerifyCertificateAsync(string verificationCode);

		Task<List<LmsCertificateTemplate>> GetCertificateTemplatesAsync(int departmentId);

		Task<LmsCertificateTemplate> SaveCertificateTemplateAsync(LmsCertificateTemplate template, string userId, CancellationToken cancellationToken = default);

		Task<bool> DeleteCertificateTemplateAsync(int departmentId, int lmsCertificateTemplateId, CancellationToken cancellationToken = default);
	}
}
