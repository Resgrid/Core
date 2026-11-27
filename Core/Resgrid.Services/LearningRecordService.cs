using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Transcript reads, officer actions, categories, credit mappings, settings, the hours report and history backfill requests
	/// (Training T1). Baseline (free): it never calls an LMS service (§17.3). Officer rights are checked by the callers
	/// (TrainingManagementScope); the rules that hold whoever calls are enforced here (nobody verifies their own record,
	/// verified records are immutable, categories and mappings belong to the department).
	/// </summary>
	public class LearningRecordService : ILearningRecordService
	{
		public const int ReportRowCap = 50000;
		public const decimal MaxHours = 999m;

		private readonly ILearningRecordsRepository _records;
		private readonly ILearningRecordFilesRepository _files;
		private readonly ITrainingCategoriesRepository _categories;
		private readonly ICategoryCreditMappingsRepository _mappings;
		private readonly ILearningRecordBackfillsRepository _backfills;
		private readonly ILearningRecordWriter _writer;
		private readonly ICertificationService _certifications;
		private readonly IPersonnelCertificationRepository _certificationRecords;
		private readonly IPersonnelCertificationCreditsRepository _credits;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IShiftsService _shifts;
		private readonly IEventAggregator _events;
		private readonly IRecordAttachmentScanner _scanner;

		public LearningRecordService(ILearningRecordsRepository records, ILearningRecordFilesRepository files, ITrainingCategoriesRepository categories,
			ICategoryCreditMappingsRepository mappings, ILearningRecordBackfillsRepository backfills, ILearningRecordWriter writer, ICertificationService certifications,
			IPersonnelCertificationRepository certificationRecords, IPersonnelCertificationCreditsRepository credits, IDepartmentSettingsService settings,
			IDepartmentsService departments, IDepartmentGroupsService groups, IShiftsService shifts, IEventAggregator events, IRecordAttachmentScanner scanner)
		{
			_scanner = scanner;
			_records = records;
			_files = files;
			_categories = categories;
			_mappings = mappings;
			_backfills = backfills;
			_writer = writer;
			_certifications = certifications;
			_certificationRecords = certificationRecords;
			_credits = credits;
			_settings = settings;
			_departments = departments;
			_groups = groups;
			_shifts = shifts;
			_events = events;
		}

		#region Reads

		public async Task<List<LearningRecord>> QueryAsync(LearningRecordQuery query) =>
			query == null ? new List<LearningRecord>() : await _records.QueryAsync(query) ?? new List<LearningRecord>();

		public Task<List<LearningRecord>> GetTranscriptAsync(int departmentId, string userId, DateTime? fromUtc, DateTime? toUtc, IEnumerable<int> categoryIds,
			IEnumerable<int> sourceKinds, bool includeVoided)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return Task.FromResult(new List<LearningRecord>());

			return QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				UserIds = new List<string> { userId },
				From = fromUtc,
				To = toUtc,
				CategoryIds = categoryIds?.ToList(),
				SourceKinds = sourceKinds?.ToList(),
				IncludeVoided = includeVoided,
				Take = 5000
			});
		}

		public Task<LearningRecord> GetRecordAsync(int departmentId, int learningRecordId) => _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);

		public async Task<List<LearningRecord>> GetHistoryChainAsync(int departmentId, int learningRecordId)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);
			if (record == null)
				return new List<LearningRecord>();

			// Every record one source wrote for this member and component, oldest first: the superseded and the current ones.
			return (await _records.GetBySourceAsync(departmentId, record.SourceKind, record.SourceRefId) ?? new List<LearningRecord>())
				.Where(r => string.Equals(r.UserId, record.UserId, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(r.ComponentKey ?? string.Empty, record.ComponentKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				.OrderBy(r => r.LearningRecordId)
				.ToList();
		}

		public Task<List<LearningRecord>> GetPendingAsync(int departmentId) =>
			QueryAsync(new LearningRecordQuery { DepartmentId = departmentId, Statuses = new List<int> { (int)LearningRecordStatuses.Pending }, Take = 2000 });

		public Task<LearningRecordFile> GetEvidenceFileAsync(int departmentId, int learningRecordFileId) => _files.GetWithDataAsync(departmentId, learningRecordFileId);

		#endregion

		#region Member and officer actions

		public Task<LearningRecord> SubmitExternalAsync(int departmentId, string userId, LearningRecordDraft draft, string fileName, string contentType, byte[] data,
			CancellationToken cancellationToken = default)
		{
			// T1's single-file form: one evidence upload through the round-9B path (checked, cleaned, scanned and linked like any other).
			var evidence = data != null && data.Length > 0
				? new[] { new TrainingEvidenceUpload { FileName = fileName, ContentType = contentType, Data = data } }
				: Array.Empty<TrainingEvidenceUpload>();
			return SubmitExternalAsync(departmentId, userId, draft, evidence, cancellationToken);
		}

		public async Task<LearningRecord> SubmitExternalAsync(int departmentId, string userId, LearningRecordDraft draft, IReadOnlyList<TrainingEvidenceUpload> evidence,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || draft == null)
				throw new InvalidOperationException("learning_record_not_found");

			var settings = await GetSettingsAsync(departmentId);
			if (settings.MemberSubmissionsDisabled)
				return null;

			if (string.IsNullOrWhiteSpace(draft.Title))
				throw new InvalidOperationException("learning_record_title_required");
			if (draft.CompletedOn == default || draft.CompletedOn > DateTime.UtcNow.AddDays(1) || draft.CompletedOn.Year < 1950)
				throw new InvalidOperationException("learning_record_date_invalid");
			if (draft.Hours < 0 || draft.Hours > MaxHours || (draft.CeHours.HasValue && (draft.CeHours < 0 || draft.CeHours > MaxHours)))
				throw new InvalidOperationException("learning_record_hours_invalid");
			if (draft.CategoryId.HasValue && !await IsLiveCategoryAsync(departmentId, draft.CategoryId.Value))
				throw new InvalidOperationException("learning_record_category_invalid");

			// Round 9B: every file is checked, cleaned and scanned before anything is stored.
			var files = await CheckEvidenceAsync(evidence, 0, cancellationToken);
			var reviewRequired = TrainingSubmissionRules.RequiresReview(settings, draft.CategoryId, draft.Hours);
			var now = DateTime.UtcNow;
			var fileIds = await StoreEvidenceAsync(departmentId, userId, files, null, now, cancellationToken);
			int? fileId = fileIds.Count > 0 ? fileIds[0] : (int?)null;

			var submission = new LearningRecordDraft
			{
				UserId = userId,
				Title = draft.Title,
				CourseCode = draft.CourseCode,
				CompletedOn = draft.CompletedOn,
				StartedOn = draft.StartedOn,
				EndedOn = draft.EndedOn,
				Hours = draft.Hours,
				CategoryId = draft.CategoryId,
				MethodOfTraining = draft.MethodOfTraining,
				Objectives = draft.Objectives,
				Location = draft.Location,
				CeProvider = draft.CeProvider,
				CeCourseNumber = draft.CeCourseNumber,
				CeHours = draft.CeHours,
				InstructorNames = draft.InstructorNames,
				ExternalId = draft.ExternalId,
				Notes = draft.Notes,
				// Round 9B: waits for a reviewer when the department asks for it; otherwise counts at once as self-reported (credits follow).
				Status = reviewRequired ? LearningRecordStatuses.Pending : LearningRecordStatuses.Verified,
				ReviewState = (int)(reviewRequired ? TrainingSubmissionReviewStates.AwaitingReview : TrainingSubmissionReviewStates.SelfReported),
				SubmittedOn = now,
				EvidenceFileId = fileId
			};

			var result = await _writer.WriteSourceAsync(departmentId, LearningRecordSourceKinds.ExternalSubmission, LearningRecordRules.SubmissionRef(Guid.NewGuid()),
				new[] { submission }, userId, cancellationToken);
			var record = result.Records.FirstOrDefault();
			if (record != null)
			{
				if (fileIds.Count > 0)
					await _files.LinkToRecordAsync(departmentId, fileIds, record.LearningRecordId, cancellationToken);
				Audit(departmentId, userId, AuditLogTypes.ExternalTrainingSubmitted, null, Snapshot(record));
			}
			return record;
		}

		/// <summary>Round 9B: checks, cleans and scans one upload's files (TrainingEvidencePolicy, then the attachment scanner); throws the refusal code.</summary>
		private async Task<List<TrainingEvidenceCheck>> CheckEvidenceAsync(IReadOnlyList<TrainingEvidenceUpload> evidence, int alreadyStored, CancellationToken cancellationToken)
		{
			var uploads = (evidence ?? Array.Empty<TrainingEvidenceUpload>()).Where(u => u?.Data != null && u.Data.Length > 0).ToList();
			var refusal = TrainingEvidencePolicy.CheckUpload(uploads, alreadyStored);
			if (refusal != null)
				throw new InvalidOperationException(refusal);

			var result = new List<TrainingEvidenceCheck>();
			foreach (var upload in uploads)
			{
				var check = TrainingEvidencePolicy.Check(upload);
				if (check.Refusal != null)
					throw new InvalidOperationException(check.Refusal);

				var scan = (_scanner == null ? null : await _scanner.ScanAsync(check.File.FileName, check.File.ContentType, check.File.Data, cancellationToken)) ?? new RecordAttachmentScanResult();
				if (scan.State == RmsAttachmentScanState.Rejected)
					throw new InvalidOperationException(TrainingEvidencePolicy.ScannerRejected);
				check.ScanState = (int)scan.State;
				result.Add(check);
			}

			return result;
		}

		/// <summary>Round 9B: stores checked files (linked to the record when it exists already); returns their ids in upload order.</summary>
		private async Task<List<int>> StoreEvidenceAsync(int departmentId, string userId, IEnumerable<TrainingEvidenceCheck> files, int? learningRecordId, DateTime now,
			CancellationToken cancellationToken)
		{
			var ids = new List<int>();
			foreach (var check in files ?? Enumerable.Empty<TrainingEvidenceCheck>())
			{
				var stored = await _files.InsertAsync(new LearningRecordFile
				{
					DepartmentId = departmentId,
					UserId = userId,
					FileName = LearningRecordRules.Trim(check.File.FileName, 255) ?? "evidence",
					ContentType = LearningRecordRules.Trim(check.File.ContentType, 100),
					Data = check.File.Data,
					UploadedOn = now,
					UploadedByUserId = userId,
					LearningRecordId = learningRecordId,
					SizeBytes = check.File.Data.LongLength,
					Sha256 = check.Sha256,
					EvidenceKind = check.File.Kind,
					ScanState = check.ScanState
				}, cancellationToken, true);
				if (stored != null && stored.LearningRecordFileId > 0)
					ids.Add(stored.LearningRecordFileId);
			}

			return ids;
		}

		public async Task<LearningRecord> RequestInfoAsync(int departmentId, int learningRecordId, string officerUserId, string note, CancellationToken cancellationToken = default)
		{
			var text = TrainingSubmissionRules.Note(note);
			if (text == null)
				throw new InvalidOperationException("learning_record_note_required");

			var record = await RequirePendingAsync(departmentId, learningRecordId, officerUserId);
			if (!TrainingSubmissionRules.IsSubmission(record))
				throw new InvalidOperationException("learning_record_not_submission");
			if (!TrainingSubmissionRules.IsAwaitingReview(record))
				throw new InvalidOperationException("learning_record_not_awaiting_review");

			var before = Snapshot(record);
			record.ReviewState = (int)TrainingSubmissionReviewStates.InfoRequested;
			record.ReviewNote = text;
			await _records.UpdateColumnsAsync(record, new[] { "ReviewState", "ReviewNote" }, cancellationToken);
			Audit(departmentId, officerUserId, AuditLogTypes.TrainingSubmissionInfoRequested, before, Snapshot(record));
			return record;
		}

		public async Task<LearningRecord> ResubmitAsync(int departmentId, int learningRecordId, string userId, TrainingSubmissionCorrection correction,
			IReadOnlyList<TrainingEvidenceUpload> evidence, CancellationToken cancellationToken = default)
		{
			var record = departmentId <= 0 ? null : await _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);
			// Only the member's own submission; anybody else's reads as not found.
			if (record == null || record.DepartmentId != departmentId || !TrainingSubmissionRules.IsSubmission(record) ||
				!string.Equals(record.UserId, userId, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("learning_record_not_found");
			if (record.Status != (int)LearningRecordStatuses.Pending)
				throw new InvalidOperationException("learning_record_not_pending");

			var before = Snapshot(record);
			var columns = new List<string> { "ReviewState", "SubmittedOn" };
			if (correction?.Hours.HasValue == true)
			{
				if (correction.Hours.Value <= 0 || correction.Hours.Value > MaxHours)
					throw new InvalidOperationException("learning_record_hours_invalid");
				record.Hours = Math.Round(correction.Hours.Value, 2, MidpointRounding.AwayFromZero);
				columns.Add("Hours");
			}
			if (correction?.CeHours.HasValue == true)
			{
				if (correction.CeHours.Value < 0 || correction.CeHours.Value > MaxHours)
					throw new InvalidOperationException("learning_record_hours_invalid");
				record.CeHours = Math.Round(correction.CeHours.Value, 2, MidpointRounding.AwayFromZero);
				columns.Add("CeHours");
			}
			if (correction?.CategoryId.HasValue == true)
			{
				if (correction.CategoryId.Value > 0 && !await IsLiveCategoryAsync(departmentId, correction.CategoryId.Value))
					throw new InvalidOperationException("learning_record_category_invalid");
				record.CategoryId = correction.CategoryId.Value > 0 ? correction.CategoryId : null;
				columns.Add("CategoryId");
			}

			var stored = await _files.GetForRecordsAsync(departmentId, new[] { record.LearningRecordId }) ?? new List<LearningRecordFile>();
			var files = await CheckEvidenceAsync(evidence, stored.Count, cancellationToken);
			var now = DateTime.UtcNow;
			var fileIds = await StoreEvidenceAsync(departmentId, record.UserId, files, record.LearningRecordId, now, cancellationToken);
			if (!record.EvidenceFileId.HasValue && fileIds.Count > 0)
			{
				record.EvidenceFileId = fileIds[0];
				columns.Add("EvidenceFileId");
			}

			// An answer to a request for more information goes back in line now; evidence added while it still waits keeps its place.
			if (TrainingSubmissionRules.StateOf(record) != TrainingSubmissionReviewStates.AwaitingReview || !record.SubmittedOn.HasValue)
				record.SubmittedOn = now;
			record.ReviewState = (int)TrainingSubmissionReviewStates.AwaitingReview;
			await _records.UpdateColumnsAsync(record, columns, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.TrainingSubmissionResubmitted, before, Snapshot(record));
			return record;
		}

		public async Task<List<LearningRecordFile>> GetEvidenceFilesAsync(int departmentId, IEnumerable<int> learningRecordIds)
		{
			var ids = learningRecordIds?.Where(i => i > 0).Distinct().ToList() ?? new List<int>();
			if (departmentId <= 0 || ids.Count == 0)
				return new List<LearningRecordFile>();
			return (await _files.GetForRecordsAsync(departmentId, ids) ?? new List<LearningRecordFile>()).Where(f => f != null && f.DepartmentId == departmentId).ToList();
		}

		public async Task<LearningRecord> VerifyAsync(int departmentId, int learningRecordId, string officerUserId, string note, decimal? hours,
			CancellationToken cancellationToken = default)
		{
			var record = await RequirePendingAsync(departmentId, learningRecordId, officerUserId);
			if (hours.HasValue && (hours.Value < 0 || hours.Value > MaxHours))
				throw new InvalidOperationException("learning_record_hours_invalid");

			var before = Snapshot(record);
			var columns = new List<string> { "Status", "VerifiedByUserId", "VerifiedOn", "ReviewNote" };
			record.Status = (int)LearningRecordStatuses.Verified;
			record.VerifiedByUserId = officerUserId;
			record.VerifiedOn = DateTime.UtcNow;
			record.ReviewNote = LearningRecordRules.Trim(note, 1000);
			if (hours.HasValue)
			{
				record.Hours = Math.Round(hours.Value, 2, MidpointRounding.AwayFromZero);
				columns.Add("Hours");
			}
			// Round 9B: a member submission says it was approved (a reviewed record, not a self-reported one).
			if (TrainingSubmissionRules.IsSubmission(record))
			{
				record.ReviewState = (int)TrainingSubmissionReviewStates.Approved;
				columns.Add("ReviewState");
			}

			await _records.UpdateColumnsAsync(record, columns, cancellationToken);
			await _writer.ApplyCreditsAsync(record, false, officerUserId, cancellationToken);
			Audit(departmentId, officerUserId, AuditLogTypes.LearningRecordVerified, before, Snapshot(record));
			// Round 9A: a verified submission can complete a certification's requirements (CE hours, an outside course).
			await _writer.NotifyChangedAsync(departmentId, new[] { record }, null, officerUserId, cancellationToken);
			return record;
		}

		public async Task<LearningRecord> RejectAsync(int departmentId, int learningRecordId, string officerUserId, string note, CancellationToken cancellationToken = default)
		{
			var record = await RequirePendingAsync(departmentId, learningRecordId, officerUserId);
			var before = Snapshot(record);
			record.Status = (int)LearningRecordStatuses.Rejected;
			record.ReviewNote = LearningRecordRules.Trim(note, 1000);
			record.VerifiedByUserId = officerUserId;
			record.VerifiedOn = DateTime.UtcNow;
			var columns = new List<string> { "Status", "ReviewNote", "VerifiedByUserId", "VerifiedOn" };
			if (TrainingSubmissionRules.IsSubmission(record))
			{
				record.ReviewState = (int)TrainingSubmissionReviewStates.Denied;
				columns.Add("ReviewState");
			}
			await _records.UpdateColumnsAsync(record, columns, cancellationToken);
			Audit(departmentId, officerUserId, AuditLogTypes.LearningRecordRejected, before, Snapshot(record));
			return record;
		}

		private async Task<LearningRecord> RequirePendingAsync(int departmentId, int learningRecordId, string officerUserId)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);
			if (record == null)
				throw new InvalidOperationException("learning_record_not_found");
			if (string.Equals(record.UserId, officerUserId, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("learning_record_self_review");
			if (record.Status != (int)LearningRecordStatuses.Pending)
				throw new InvalidOperationException("learning_record_not_pending");
			return record;
		}

		public async Task<bool> VoidAsync(int departmentId, int learningRecordId, string officerUserId, string reason, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reason))
				throw new InvalidOperationException("learning_record_reason_required");

			var record = await _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);
			if (record == null)
				throw new InvalidOperationException("learning_record_not_found");
			if (record.IsVoided)
				throw new InvalidOperationException("learning_record_voided");

			var before = Snapshot(record);
			if (!await _records.VoidAsync(departmentId, learningRecordId, reason, officerUserId, DateTime.UtcNow, cancellationToken))
				return false;

			record.Status = (int)LearningRecordStatuses.Voided;
			record.VoidReason = reason;
			await _writer.RemoveCreditsAsync(record, officerUserId, cancellationToken);
			Audit(departmentId, officerUserId, AuditLogTypes.LearningRecordVoided, before, Snapshot(record));
			// Round 9A: a certification this record issued, renewed or kept (its hours) goes back unless another completion still covers it.
			await _writer.NotifyChangedAsync(departmentId, null, new[] { record }, officerUserId, cancellationToken);
			return true;
		}

		public async Task<LearningRecord> ResolveDuplicateAsync(int departmentId, int learningRecordId, LearningRecordDuplicateResolutions resolution, string officerUserId,
			CancellationToken cancellationToken = default)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);
			if (record == null)
				throw new InvalidOperationException("learning_record_not_found");
			if (!record.HasUnresolvedDuplicate)
				throw new InvalidOperationException("learning_record_not_duplicate");

			var before = Snapshot(record);
			record.DuplicateResolvedOn = DateTime.UtcNow;
			record.DuplicateResolution = (int)resolution;
			await _records.UpdateColumnsAsync(record, new[] { "DuplicateResolvedOn", "DuplicateResolution" }, cancellationToken);

			if (resolution == LearningRecordDuplicateResolutions.Merged &&
				await _records.VoidAsync(departmentId, learningRecordId, "duplicate_merged", officerUserId, DateTime.UtcNow, cancellationToken))
			{
				record.Status = (int)LearningRecordStatuses.Voided;
				await _writer.RemoveCreditsAsync(record, officerUserId, cancellationToken);
				await _writer.NotifyChangedAsync(departmentId, null, new[] { record }, officerUserId, cancellationToken);
			}

			Audit(departmentId, officerUserId, AuditLogTypes.LearningRecordDuplicateResolved, before, Snapshot(record));
			return record;
		}

		#endregion

		#region Categories

		public async Task<List<TrainingCategory>> GetCategoriesAsync(int departmentId, bool includeDeleted = false) =>
			departmentId <= 0 ? new List<TrainingCategory>() : await _categories.GetForDepartmentAsync(departmentId, includeDeleted) ?? new List<TrainingCategory>();

		private async Task<bool> IsLiveCategoryAsync(int departmentId, int categoryId) =>
			(await GetCategoriesAsync(departmentId)).Any(c => c.TrainingCategoryId == categoryId);

		public async Task<TrainingCategory> SaveCategoryAsync(TrainingCategory category, string userId, CancellationToken cancellationToken = default)
		{
			if (category == null || category.DepartmentId <= 0)
				throw new InvalidOperationException("learning_record_category_invalid");
			if (string.IsNullOrWhiteSpace(category.Name))
				throw new InvalidOperationException("learning_record_category_name_required");
			if (category.TargetHours.HasValue && (category.TargetHours < 0 || category.TargetHours > 9999))
				throw new InvalidOperationException("learning_record_hours_invalid");

			var now = DateTime.UtcNow;
			TrainingCategory stored;
			string before = null;
			if (category.TrainingCategoryId > 0)
			{
				stored = (await _categories.GetForDepartmentAsync(category.DepartmentId, true) ?? new List<TrainingCategory>())
					.FirstOrDefault(c => c.TrainingCategoryId == category.TrainingCategoryId);
				if (stored == null || stored.IsDeleted)
					throw new InvalidOperationException("learning_record_category_invalid");
				before = Snapshot(stored);
			}
			else
			{
				stored = new TrainingCategory { DepartmentId = category.DepartmentId, CreatedOn = now, CreatedByUserId = userId, TemplateKey = LearningRecordRules.Trim(category.TemplateKey, 64) };
			}

			stored.Name = LearningRecordRules.Trim(category.Name, 150);
			stored.Code = LearningRecordRules.Trim(category.Code, 32);
			stored.Kind = Enum.IsDefined(typeof(TrainingCategoryKinds), category.Kind) ? category.Kind : 0;
			stored.SortOrder = category.SortOrder;
			stored.Description = LearningRecordRules.Trim(category.Description, 1000);
			stored.TargetHours = category.TargetHours.HasValue ? Math.Round(category.TargetHours.Value, 2) : (decimal?)null;
			stored.TargetPeriod = Enum.IsDefined(typeof(TrainingTargetPeriods), category.TargetPeriod) ? category.TargetPeriod : 0;
			stored.TargetAppliesTo = LearningRecordRules.Trim(category.TargetAppliesTo, 200);
			stored.ExternalCode = LearningRecordRules.Trim(category.ExternalCode, 64);
			stored.RetentionYears = category.RetentionYears.HasValue ? Math.Clamp(category.RetentionYears.Value, 0, 75) : (int?)null;
			if (stored.TrainingCategoryId > 0)
			{
				stored.UpdatedOn = now;
				stored.UpdatedByUserId = userId;
			}

			var saved = await _categories.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(saved.DepartmentId, userId, AuditLogTypes.TrainingCategoryChanged, before, Snapshot(saved));
			return saved;
		}

		public async Task<bool> DeleteCategoryAsync(int departmentId, int trainingCategoryId, string userId, CancellationToken cancellationToken = default)
		{
			var stored = (await _categories.GetForDepartmentAsync(departmentId, true) ?? new List<TrainingCategory>()).FirstOrDefault(c => c.TrainingCategoryId == trainingCategoryId);
			if (stored == null || stored.IsDeleted)
				return false;

			var before = Snapshot(stored);
			stored.IsDeleted = true;
			stored.UpdatedOn = DateTime.UtcNow;
			stored.UpdatedByUserId = userId;
			await _categories.SaveOrUpdateAsync(stored, cancellationToken, true);

			// A removed category credits nothing from now on; credits already given stay.
			foreach (var mapping in (await _mappings.GetForDepartmentAsync(departmentId) ?? new List<CategoryCreditMapping>()).Where(m => m.CategoryId == trainingCategoryId && m.IsEnabled))
			{
				mapping.IsEnabled = false;
				mapping.UpdatedOn = DateTime.UtcNow;
				mapping.UpdatedByUserId = userId;
				await _mappings.SaveOrUpdateAsync(mapping, cancellationToken, true);
			}

			Audit(departmentId, userId, AuditLogTypes.TrainingCategoryChanged, before, null);
			return true;
		}

		public Task<int> ApplyCategoryTemplateAsync(int departmentId, string templateKey, string userId, CancellationToken cancellationToken = default) =>
			ApplyCategoryTemplateAsync(departmentId, templateKey, userId, null, cancellationToken);

		public async Task<int> ApplyCategoryTemplateAsync(int departmentId, string templateKey, string userId, string culture, CancellationToken cancellationToken = default)
		{
			// Round 9C: names and descriptions in the applying user's language (English when none ships); codes match in every language.
			var template = TrainingCategoryTemplateCatalog.Find(templateKey, culture);
			if (template == null || departmentId <= 0)
				throw new InvalidOperationException("learning_record_category_invalid");

			var existing = await GetCategoriesAsync(departmentId);
			var codes = new HashSet<string>(existing.Where(c => !string.IsNullOrWhiteSpace(c.Code)).Select(c => c.Code), StringComparer.OrdinalIgnoreCase);
			var order = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;
			var added = 0;

			foreach (var item in template.Items.Where(i => !codes.Contains(i.Code)))
			{
				await SaveCategoryAsync(new TrainingCategory
				{
					DepartmentId = departmentId,
					Name = item.Name,
					Code = item.Code,
					Kind = (int)template.Kind,
					SortOrder = order++,
					Description = item.Description,
					TargetHours = item.TargetHours,
					TargetPeriod = (int)item.TargetPeriod,
					TargetAppliesTo = item.TargetAppliesTo,
					RetentionYears = item.RetentionYears,
					TemplateKey = template.Key
				}, userId, cancellationToken);
				added++;
			}

			return added;
		}

		public Task<List<LearningRecordGroup>> GetUncategorizedGroupsAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc) =>
			_records.GetUncategorizedGroupsAsync(departmentId, fromUtc, toUtc);

		public async Task<int> CategorizeAsync(int departmentId, int categoryId, string title, string courseCode, DateTime? fromUtc, DateTime? toUtc,
			IEnumerable<int> learningRecordIds, string userId, CancellationToken cancellationToken = default)
		{
			if (!await IsLiveCategoryAsync(departmentId, categoryId))
				throw new InvalidOperationException("learning_record_category_invalid");

			List<LearningRecord> targets;
			var ids = learningRecordIds?.Where(i => i > 0).Distinct().ToList();
			if (ids != null && ids.Count > 0)
			{
				targets = (await _records.GetByIdsForDepartmentAsync(departmentId, ids) ?? new List<LearningRecord>()).Where(r => !r.IsVoided).ToList();
			}
			else
			{
				if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(courseCode) && !fromUtc.HasValue && !toUtc.HasValue)
					throw new InvalidOperationException("learning_record_category_invalid");

				var code = (courseCode ?? string.Empty).Trim();
				targets = (await _records.QueryAsync(new LearningRecordQuery { DepartmentId = departmentId, UncategorizedOnly = true, From = fromUtc, To = toUtc, Take = LearningRecordsTake }) ?? new List<LearningRecord>())
					.Where(r => (string.IsNullOrWhiteSpace(title) || string.Equals((r.Title ?? string.Empty).Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase)) &&
						(courseCode == null || string.Equals((r.CourseCode ?? string.Empty).Trim(), code, StringComparison.OrdinalIgnoreCase)))
					.ToList();
			}

			targets = targets.Where(r => r.CategoryId != categoryId).ToList();
			if (targets.Count == 0)
				return 0;

			var count = await _records.SetCategoryAsync(departmentId, targets.Select(r => r.LearningRecordId), categoryId, cancellationToken);

			// Credits follow the category: a record moved out of a mapped category loses its derived credits, and a record moved
			// into one gains them (backfilled records only through "apply history to credits", R-T3).
			foreach (var record in targets)
			{
				if (record.CategoryId.HasValue)
					await _writer.RemoveCreditsAsync(record, userId, cancellationToken);
				record.CategoryId = categoryId;
				await _writer.ApplyCreditsAsync(record, false, userId, cancellationToken);
			}

			Audit(departmentId, userId, AuditLogTypes.LearningRecordsCategorized, null, JsonConvert.SerializeObject(new { CategoryId = categoryId, Count = count, Title = title, CourseCode = courseCode, From = fromUtc, To = toUtc }));
			return count;
		}

		private const int LearningRecordsTake = 5000;

		#endregion

		#region Credit mappings and history

		public async Task<List<CategoryCreditMapping>> GetCreditMappingsAsync(int departmentId) =>
			departmentId <= 0 ? new List<CategoryCreditMapping>() : await _mappings.GetForDepartmentAsync(departmentId) ?? new List<CategoryCreditMapping>();

		public async Task<CategoryCreditMapping> SaveCreditMappingAsync(CategoryCreditMapping mapping, string userId, CancellationToken cancellationToken = default)
		{
			if (mapping == null || mapping.DepartmentId <= 0 || !await IsLiveCategoryAsync(mapping.DepartmentId, mapping.CategoryId))
				throw new InvalidOperationException("learning_record_mapping_invalid");

			var type = await _certifications.GetCertificationTypeByIdAsync(mapping.DepartmentCertificationTypeId);
			if (type == null || type.DepartmentId != mapping.DepartmentId || type.IsDeleted || type.IsUnitScoped)
				throw new InvalidOperationException("learning_record_mapping_invalid");

			var now = DateTime.UtcNow;
			var all = await GetCreditMappingsAsync(mapping.DepartmentId);
			CategoryCreditMapping stored;
			string before = null;
			if (mapping.CategoryCreditMappingId > 0)
			{
				stored = all.FirstOrDefault(m => m.CategoryCreditMappingId == mapping.CategoryCreditMappingId);
				if (stored == null)
					throw new InvalidOperationException("learning_record_mapping_invalid");
				before = Snapshot(stored);
				// Turning a mapping on starts its credits from today; turning it off keeps the credits already given.
				if (mapping.IsEnabled && !stored.IsEnabled)
					stored.EnabledOn = now;
				stored.UpdatedOn = now;
				stored.UpdatedByUserId = userId;
			}
			else
			{
				var component = LearningRecordRules.Trim(mapping.ComponentKey, 64);
				if (all.Any(m => m.DepartmentCertificationTypeId == mapping.DepartmentCertificationTypeId && m.CategoryId == mapping.CategoryId &&
					string.Equals(m.ComponentKey ?? string.Empty, component ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
					throw new InvalidOperationException("learning_record_mapping_invalid");

				stored = new CategoryCreditMapping { DepartmentId = mapping.DepartmentId, CreatedOn = now, CreatedByUserId = userId, EnabledOn = mapping.IsEnabled ? now : (DateTime?)null };
			}

			stored.DepartmentCertificationTypeId = mapping.DepartmentCertificationTypeId;
			stored.CategoryId = mapping.CategoryId;
			stored.ComponentKey = LearningRecordRules.Trim(mapping.ComponentKey, 64);
			stored.IsEnabled = mapping.IsEnabled;

			var saved = await _mappings.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(saved.DepartmentId, userId, AuditLogTypes.CategoryCreditMappingChanged, before, Snapshot(saved));
			return saved;
		}

		public async Task<bool> DeleteCreditMappingAsync(int departmentId, int categoryCreditMappingId, string userId, CancellationToken cancellationToken = default)
		{
			var stored = (await GetCreditMappingsAsync(departmentId)).FirstOrDefault(m => m.CategoryCreditMappingId == categoryCreditMappingId);
			if (stored == null)
				return false;

			await _mappings.DeleteAsync(stored, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CategoryCreditMappingChanged, Snapshot(stored), null);
			return true;
		}

		public async Task<HistoryCreditPreview> PreviewHistoryCreditsAsync(int departmentId, int departmentCertificationTypeId)
		{
			var preview = new HistoryCreditPreview { DepartmentCertificationTypeId = departmentCertificationTypeId };
			var type = await _certifications.GetCertificationTypeByIdAsync(departmentCertificationTypeId);
			if (type == null || type.DepartmentId != departmentId || type.IsDeleted)
				return preview;

			var mappings = (await GetCreditMappingsAsync(departmentId)).Where(m => m.DepartmentCertificationTypeId == departmentCertificationTypeId && m.IsEnabled).ToList();
			if (mappings.Count == 0)
				return preview;

			var records = (await _records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				CategoryIds = mappings.Select(m => m.CategoryId).Distinct().ToList(),
				Statuses = new List<int> { (int)LearningRecordStatuses.Verified },
				Take = LearningRecordsTake
			}) ?? new List<LearningRecord>()).Where(r => LearningRecordRules.CanCredit(r) && mappings.Any(m => LearningRecordRules.MappingApplies(m, r, true))).ToList();
			if (records.Count == 0)
				return preview;

			var holders = (await _certificationRecords.GetByTypeIdsAsync(departmentId, new[] { departmentCertificationTypeId }) ?? Enumerable.Empty<PersonnelCertification>()).ToList();
			var credited = (await _credits.GetByLearningRecordIdsAsync(departmentId, records.Select(r => r.LearningRecordId)) ?? Enumerable.Empty<PersonnelCertificationCredit>()).ToList();
			var cycles = new Dictionary<int, HistoryCreditCycle>();

			foreach (var record in records.OrderBy(r => r.CompletedOn))
			{
				var cycle = LearningRecordRules.FindCycle(holders.Where(h => string.Equals(h.UserId, record.UserId, StringComparison.OrdinalIgnoreCase)), departmentCertificationTypeId, record.CompletedOn);
				if (cycle == null)
				{
					preview.OutsideEveryCycle++;
					continue;
				}

				if (credited.Any(c => c.LearningRecordId == record.LearningRecordId && c.PersonnelCertificationId == cycle.PersonnelCertificationId))
					continue;

				if (!cycles.TryGetValue(cycle.PersonnelCertificationId, out var group))
				{
					group = new HistoryCreditCycle
					{
						PersonnelCertificationId = cycle.PersonnelCertificationId,
						UserId = cycle.UserId,
						CycleStart = cycle.RecievedOn,
						CycleEnd = cycle.ExpiresOn,
						RequiredHours = type.RenewalCreditHoursRequired,
						ExistingCredits = await _certifications.GetCertificationCreditsAsync(cycle.PersonnelCertificationId) ?? new List<PersonnelCertificationCredit>()
					};
					cycles[cycle.PersonnelCertificationId] = group;
				}

				group.Proposed.Add(new HistoryCreditProposal
				{
					LearningRecordId = record.LearningRecordId,
					UserId = record.UserId,
					Title = record.Title,
					CompletedOn = record.CompletedOn,
					Hours = record.Hours,
					CategoryId = record.CategoryId,
					PersonnelCertificationId = cycle.PersonnelCertificationId,
					CycleStart = cycle.RecievedOn,
					CycleEnd = cycle.ExpiresOn,
					IsBackfill = record.IsBackfill
				});
			}

			preview.Cycles = cycles.Values.OrderBy(c => c.UserId, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.CycleStart).ToList();
			return preview;
		}

		public async Task<int> ApplyHistoryCreditsAsync(int departmentId, int departmentCertificationTypeId, IEnumerable<int> learningRecordIds, string userId,
			CancellationToken cancellationToken = default)
		{
			var preview = await PreviewHistoryCreditsAsync(departmentId, departmentCertificationTypeId);
			var keep = learningRecordIds == null ? null : new HashSet<int>(learningRecordIds);
			var categories = (await GetCategoriesAsync(departmentId, true)).ToDictionary(c => c.TrainingCategoryId, c => c.Name);
			var written = 0;

			foreach (var proposal in preview.Cycles.SelectMany(c => c.Proposed).Where(p => keep == null || keep.Contains(p.LearningRecordId)))
			{
				await _certifications.AddCertificationCreditAsync(new PersonnelCertificationCredit
				{
					PersonnelCertificationId = proposal.PersonnelCertificationId,
					DepartmentId = departmentId,
					CreditDate = proposal.CompletedOn.Date,
					Hours = proposal.Hours,
					Category = LearningRecordRules.Trim(proposal.CategoryId.HasValue && categories.TryGetValue(proposal.CategoryId.Value, out var name) ? name : null, 100),
					Description = LearningRecordRules.Trim(proposal.Title, 250),
					LearningRecordId = proposal.LearningRecordId
				}, userId, cancellationToken);
				written++;
			}

			if (written > 0)
				Audit(departmentId, userId, AuditLogTypes.TrainingHistoryApplied, null, JsonConvert.SerializeObject(new { DepartmentCertificationTypeId = departmentCertificationTypeId, Credits = written }));
			return written;
		}

		public async Task<Dictionary<int, CertificationRenewalHours>> GetRenewalHoursAsync(int departmentId, IEnumerable<int> personnelCertificationIds)
		{
			var result = new Dictionary<int, CertificationRenewalHours>();
			var ids = personnelCertificationIds?.Where(i => i > 0).Distinct().ToList() ?? new List<int>();
			if (departmentId <= 0 || ids.Count == 0)
				return result;

			var mappings = (await GetCreditMappingsAsync(departmentId)).Where(m => m.IsEnabled).ToList();
			foreach (var id in ids)
			{
				var holder = await _certificationRecords.GetByIdAsync(id);
				if (holder == null || holder.DepartmentId != departmentId)
					continue;

				var credits = await _certifications.GetCertificationCreditsAsync(id) ?? new List<PersonnelCertificationCredit>();
				var hours = new CertificationRenewalHours
				{
					PersonnelCertificationId = id,
					CreditedHours = credits.Sum(c => c.Hours),
					DerivedHours = credits.Where(c => c.IsDerived).Sum(c => c.Hours)
				};

				var categories = mappings.Where(m => holder.DepartmentCertificationTypeId.HasValue && m.DepartmentCertificationTypeId == holder.DepartmentCertificationTypeId.Value)
					.Select(m => m.CategoryId).Distinct().ToList();
				if (categories.Count > 0)
				{
					var derived = new HashSet<int>(credits.Where(c => c.IsDerived).Select(c => c.LearningRecordId.Value));
					var ledger = await _records.QueryAsync(new LearningRecordQuery
					{
						DepartmentId = departmentId,
						UserIds = new List<string> { holder.UserId },
						CategoryIds = categories,
						From = holder.RecievedOn?.Date,
						To = holder.ExpiresOn?.Date.AddDays(1),
						Statuses = new List<int> { (int)LearningRecordStatuses.Verified },
						Take = LearningRecordsTake
					}) ?? new List<LearningRecord>();
					hours.UncreditedLedgerHours = ledger.Where(r => LearningRecordRules.CanCredit(r) && !derived.Contains(r.LearningRecordId)).Sum(r => r.Hours);
				}

				result[id] = hours;
			}

			return result;
		}

		#endregion

		#region Settings and reports

		public async Task<TrainingRecordSettings> GetSettingsAsync(int departmentId) =>
			(await _settings.GetTrainingRecordSettingsAsync(departmentId))?.Normalized() ?? new TrainingRecordSettings();

		public async Task<TrainingRecordSettings> SaveSettingsAsync(int departmentId, TrainingRecordSettings settings, string userId, CancellationToken cancellationToken = default)
		{
			var before = await GetSettingsAsync(departmentId);
			var stored = (settings ?? new TrainingRecordSettings()).Normalized();
			await _settings.SetTrainingRecordSettingsAsync(departmentId, stored, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.TrainingRecordSettingsChanged, JsonConvert.SerializeObject(before), JsonConvert.SerializeObject(stored));
			return stored;
		}

		public async Task<TrainingHoursReport> GetHoursReportAsync(TrainingHoursReportQuery query)
		{
			var report = new TrainingHoursReport { Query = query, GeneratedOn = DateTime.UtcNow };
			if (query == null || query.DepartmentId <= 0)
				return report;

			var rows = new List<LearningRecord>();
			for (var skip = 0; skip < ReportRowCap; skip += LearningRecordsTake)
			{
				var page = await _records.QueryAsync(new LearningRecordQuery
				{
					DepartmentId = query.DepartmentId,
					From = query.From,
					To = query.To,
					CategoryIds = query.CategoryIds,
					UserIds = query.UserIds,
					Statuses = new List<int> { (int)LearningRecordStatuses.Verified },
					Skip = skip,
					Take = LearningRecordsTake
				}) ?? new List<LearningRecord>();
				rows.AddRange(page);
				if (page.Count < LearningRecordsTake)
					break;
			}

			// R-T13: former members (deleted, disabled, hidden) only behind the switch.
			var active = await _departments.GetActiveMemberUserIdsAsync(query.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (!query.IncludeFormerMembers)
				rows = rows.Where(r => active.Contains(r.UserId ?? string.Empty)).ToList();

			if (query.OnDutyNow)
			{
				var onDuty = await OnDutyNowAsync(query.DepartmentId);
				rows = rows.Where(r => onDuty.Contains(r.UserId ?? string.Empty)).ToList();
			}

			var categories = (await GetCategoriesAsync(query.DepartmentId, true)).ToDictionary(c => c.TrainingCategoryId);
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(query.DepartmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
			var department = await _departments.GetDepartmentByIdAsync(query.DepartmentId, false);

			IEnumerable<IGrouping<string, LearningRecord>> groups;
			switch (query.GroupBy)
			{
				case TrainingHoursGroupings.Member:
					groups = rows.GroupBy(r => r.UserId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
					break;
				case TrainingHoursGroupings.Month:
					groups = rows.GroupBy(r => Local(r.CompletedOn, department).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
					break;
				default:
					groups = rows.GroupBy(r => r.CategoryId.HasValue ? r.CategoryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
					break;
			}

			foreach (var group in groups)
			{
				var row = new TrainingHoursReportRow
				{
					Key = group.Key,
					Records = group.Count(),
					Members = group.Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
					Hours = group.Sum(r => r.Hours),
					CeHours = group.Sum(r => r.CeHours ?? 0m)
				};

				switch (query.GroupBy)
				{
					case TrainingHoursGroupings.Member:
						row.Label = names.TryGetValue(group.Key, out var name) ? name : group.Key;
						row.IsFormerMember = !active.Contains(group.Key);
						break;
					case TrainingHoursGroupings.Month:
						row.Label = group.Key;
						break;
					default:
						if (int.TryParse(group.Key, out var categoryId) && categories.TryGetValue(categoryId, out var category))
						{
							row.Label = category.Name;
							row.TargetHours = category.TargetHours;
						}
						break;
				}

				report.Rows.Add(row);
			}

			report.Rows = query.GroupBy == TrainingHoursGroupings.Month
				? report.Rows.OrderBy(r => r.Key, StringComparer.Ordinal).ToList()
				: report.Rows.OrderByDescending(r => r.Hours).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
			report.TotalHours = rows.Sum(r => r.Hours);
			report.TotalRecords = rows.Count;
			report.Members = rows.Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
			report.UnresolvedDuplicates = await _records.CountUnresolvedDuplicatesAsync(query.DepartmentId, query.From, query.To);
			report.PendingRecords = await _records.CountAsync(new LearningRecordQuery
			{
				DepartmentId = query.DepartmentId, From = query.From, To = query.To, Statuses = new List<int> { (int)LearningRecordStatuses.Pending }
			});
			return report;
		}

		private static DateTime Local(DateTime utc, Department department)
		{
			try
			{
				return department == null ? utc : utc.TimeConverter(department);
			}
			catch (Exception)
			{
				return utc;
			}
		}

		/// <summary>Members on duty now, from the department's shift groups (§13a "On duty now").</summary>
		private async Task<HashSet<string>> OnDutyNowAsync(int departmentId)
		{
			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				var groups = (await _groups.GetAllGroupsForDepartmentUnlimitedThinAsync(departmentId) ?? new List<DepartmentGroup>()).Select(g => g.DepartmentGroupId).ToList();
				if (groups.Count == 0)
					return result;

				foreach (var members in (await _shifts.GetOnDutyUserIdsForGroupsAsync(departmentId, groups, DateTime.UtcNow) ?? new Dictionary<int, List<string>>()).Values)
					foreach (var userId in members ?? new List<string>())
						if (!string.IsNullOrWhiteSpace(userId))
							result.Add(userId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"On-duty members unavailable for department {departmentId}");
			}

			return result;
		}

		public DateTime? RetainedUntil(LearningRecord record, TrainingCategory category, TrainingRecordSettings settings, DateTime? memberLeftOn)
		{
			if (record == null)
				return null;

			var years = category?.RetentionYears ?? (settings != null && settings.DefaultRetentionYears > 0 ? settings.DefaultRetentionYears : (int?)null);
			if (!years.HasValue || years.Value <= 0)
				return null;

			var from = memberLeftOn.HasValue && memberLeftOn.Value > record.CompletedOn ? memberLeftOn.Value : record.CompletedOn;
			return from.AddYears(years.Value);
		}

		#endregion

		#region Backfill

		public async Task<LearningRecordBackfill> RequestBackfillAsync(int departmentId, bool dryRun, string userId, CancellationToken cancellationToken = default)
		{
			var latest = await _backfills.GetLatestForDepartmentAsync(departmentId);
			if (latest != null && !latest.IsFinished)
				return latest;

			var job = await _backfills.InsertAsync(new LearningRecordBackfill
			{
				DepartmentId = departmentId,
				State = (int)LearningRecordBackfillStates.Queued,
				DryRun = dryRun,
				RequestedOn = DateTime.UtcNow,
				RequestedByUserId = userId
			}, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.TrainingHistoryBackfillRequested, null, JsonConvert.SerializeObject(new { job.LearningRecordBackfillId, DryRun = dryRun }));
			return job;
		}

		public Task<LearningRecordBackfill> GetLatestBackfillAsync(int departmentId) => _backfills.GetLatestForDepartmentAsync(departmentId);

		#endregion

		private static string Snapshot(object value) => value == null ? null : JsonConvert.SerializeObject(value, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true
			});
		}
	}
}
