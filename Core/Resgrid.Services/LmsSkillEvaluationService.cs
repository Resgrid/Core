using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Skill evaluations (T4 item 2, §13a Evaluations+, LMS add-on) on the S3 evaluator platform.
	/// <list type="bullet">
	/// <item>Start: the evaluator must be eligible (the sheet's certification-type or role rule, or an LMS manager when it has none; a task
	/// book's own rule for its tasks), may not evaluate themselves unless the sheet allows it (never on a task book), and each member must have
	/// an attempt left. New evaluations need the add-on (lapse rule 3); open ones can be finished while lapsed.</item>
	/// <item>Grade: answers, Pass All, annotations, timestamped notes and evidence (photo, video, file) while in progress; a crew evaluation
	/// grades everyone on one form, and a failing member can be split out to their own.</item>
	/// <item>Submit: the engine fixes one result for the form (critical steps fail it, the time limit fails it), the evaluator picks the next
	/// signer the sheet's rule allows, and an S3 evaluator request is created (subject "evaluation:{id}"): the evaluator signs at L2 (or an
	/// outside evaluator at L4), then the next signer.</item>
	/// <item>Completion (the request's write-back, idempotent): learning records of the SkillEvaluation kind for each passed member, once
	/// (course code = the sheet code, hours = the duration); S3 seals the evidence document (fails closed: "sealing pending").</item>
	/// </list>
	/// </summary>
	public class LmsSkillEvaluationService : ILmsSkillEvaluationService, ILmsPostClassEvaluationHook
	{
		public const int MaxMembers = 50;
		public const int MaxFilesPerEvaluation = 50;
		private const int MaxEmbeddedImageBytes = 1536 * 1024;
		private const int MaxEmbeddedTotalBytes = 8 * 1024 * 1024;

		private static readonly string[] AllowedContentPrefixes = { "image/", "video/" };
		private static readonly string[] AllowedContentTypes = { "application/pdf", "text/plain" };

		private readonly ILmsSkillSheetsRepository _sheets;
		private readonly ILmsSkillSheetVersionsRepository _versions;
		private readonly ILmsSkillEvaluationsRepository _evaluations;
		private readonly ILmsSkillEvaluationMembersRepository _members;
		private readonly ILmsSkillEvaluationFilesRepository _files;
		private readonly ILmsTaskBooksRepository _books;
		private readonly ISignatureRequestService _requests;
		private readonly ISignerEligibilityService _eligibility;
		private readonly IDocumentSealingService _sealing;
		private readonly ILearningRecordWriter _writer;
		private readonly ILmsTaskBookService _taskBooks;
		private readonly ILmsSkillNotificationService _notifications;
		private readonly IDepartmentsService _departments;
		private readonly ILmsGateService _gate;
		private readonly IEventAggregator _events;

		public LmsSkillEvaluationService(ILmsSkillSheetsRepository sheets, ILmsSkillSheetVersionsRepository versions, ILmsSkillEvaluationsRepository evaluations,
			ILmsSkillEvaluationMembersRepository members, ILmsSkillEvaluationFilesRepository files, ILmsTaskBooksRepository books, ISignatureRequestService requests,
			ISignerEligibilityService eligibility, IDocumentSealingService sealing, ILearningRecordWriter writer, ILmsTaskBookService taskBooks,
			ILmsSkillNotificationService notifications, IDepartmentsService departments, ILmsGateService gate, IEventAggregator events)
		{
			_sheets = sheets;
			_versions = versions;
			_evaluations = evaluations;
			_members = members;
			_files = files;
			_books = books;
			_requests = requests;
			_eligibility = eligibility;
			_sealing = sealing;
			_writer = writer;
			_taskBooks = taskBooks;
			_notifications = notifications;
			_departments = departments;
			_gate = gate;
			_events = events;
		}

		#region Reads

		private async Task<LmsSkillEvaluation> LoadAsync(int departmentId, int lmsSkillEvaluationId)
		{
			var evaluation = departmentId <= 0 || lmsSkillEvaluationId <= 0 ? null : await _evaluations.GetByIdForDepartmentAsync(departmentId, lmsSkillEvaluationId);
			return evaluation != null && evaluation.DepartmentId == departmentId ? evaluation : null;
		}

		private async Task<(LmsSkillSheet Sheet, LmsSkillSheetVersion Version, LmsSkillSheetContent Content)> ContentAsync(LmsSkillEvaluation evaluation)
		{
			LmsSkillSheet sheet = null;
			LmsSkillSheetVersion version = null;
			if (evaluation.LmsSkillSheetId.HasValue)
				sheet = await _sheets.GetByIdForDepartmentAsync(evaluation.DepartmentId, evaluation.LmsSkillSheetId.Value);
			if (evaluation.LmsSkillSheetVersionId.HasValue)
				version = await _versions.GetByIdForDepartmentAsync(evaluation.DepartmentId, evaluation.LmsSkillSheetVersionId.Value);
			var content = version != null ? LmsSkillSheetContent.Parse(version.ContentJson) : LmsSkillSheetContent.TaskSignoff(evaluation.Title);
			return (sheet, version, content);
		}

		public async Task<LmsSkillEvaluationView> GetViewAsync(int departmentId, int lmsSkillEvaluationId)
		{
			var evaluation = await LoadAsync(departmentId, lmsSkillEvaluationId);
			if (evaluation == null)
				return null;
			var (sheet, version, content) = await ContentAsync(evaluation);
			var files = await _files.GetForEvaluationAsync(departmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationFile>();
			var answers = LmsSkillAnswers.Parse(evaluation.AnswersJson);
			var view = new LmsSkillEvaluationView
			{
				Evaluation = evaluation,
				Sheet = sheet,
				Version = version,
				Content = content,
				Answers = answers,
				Members = await _members.GetForEvaluationAsync(departmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationMember>(),
				Files = files,
				Outcome = LmsSkillSheetEngine.Evaluate(content, answers, version?.Grading, evaluation.DurationSeconds > 0 ? evaluation.DurationSeconds :
					LmsSkillRules.Elapsed(evaluation.StartedOn, DateTime.UtcNow), EvidenceKeys(files), false)
			};
			if (evaluation.LmsTaskBookId.HasValue)
				view.TaskBook = await _books.GetByIdForDepartmentAsync(departmentId, evaluation.LmsTaskBookId.Value);
			if (evaluation.SignatureRequestId.HasValue && evaluation.Status == (int)LmsSkillEvaluationStatuses.Completed)
			{
				var request = await _requests.GetAsync(departmentId, evaluation.SignatureRequestId.Value);
				if (request?.SealedDocumentId != null)
				{
					var document = await _sealing.GetByIdAsync(departmentId, request.SealedDocumentId.Value);
					view.SealedDocumentId = document?.SealedDocumentId;
					view.SealState = document == null ? "pending" : (SealedDocumentStatuses)document.Status switch
					{
						SealedDocumentStatuses.Sealed => "sealed",
						SealedDocumentStatuses.Failed => "failed",
						_ => "pending"
					};
				}
				else
					view.SealState = "pending";
			}
			return view;
		}

		private static HashSet<string> EvidenceKeys(IEnumerable<LmsSkillEvaluationFile> files) =>
			new HashSet<string>((files ?? Enumerable.Empty<LmsSkillEvaluationFile>()).Where(f => !string.IsNullOrWhiteSpace(f.StepKey)).Select(f => f.StepKey), StringComparer.OrdinalIgnoreCase);

		public async Task<List<LmsSkillEvaluation>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, int? lmsSkillSheetId, int? status, int take = 500) =>
			departmentId <= 0 ? new List<LmsSkillEvaluation>() : await _evaluations.GetForDepartmentAsync(departmentId, fromUtc, toUtc, lmsSkillSheetId, status, take) ?? new List<LmsSkillEvaluation>();

		public async Task<List<LmsSkillEvaluation>> GetOpenForEvaluatorAsync(int departmentId, string userId) =>
			await _evaluations.GetOpenForEvaluatorAsync(departmentId, userId) ?? new List<LmsSkillEvaluation>();

		public async Task<List<LmsSkillEvaluation>> GetForMemberAsync(int departmentId, string userId) =>
			await _evaluations.GetForMemberAsync(departmentId, userId, null) ?? new List<LmsSkillEvaluation>();

		public async Task<List<LmsSkillEvaluationMember>> GetMembersAsync(int departmentId, IEnumerable<int> lmsSkillEvaluationIds) =>
			await _members.GetForEvaluationsAsync(departmentId, lmsSkillEvaluationIds) ?? new List<LmsSkillEvaluationMember>();

		public async Task<LmsSkillEvaluationFile> GetEvidenceAsync(int departmentId, int lmsSkillEvaluationId, int lmsSkillEvaluationFileId)
		{
			var file = await _files.GetWithDataAsync(departmentId, lmsSkillEvaluationFileId);
			return file != null && file.DepartmentId == departmentId && file.LmsSkillEvaluationId == lmsSkillEvaluationId ? file : null;
		}

		#endregion Reads

		#region Eligibility

		public async Task<bool> CanEvaluateAsync(LmsActor actor, LmsSkillSheet sheet)
		{
			if (actor == null || !actor.IsCurrentMember || sheet == null || sheet.DepartmentId != actor.DepartmentId || sheet.IsDeleted)
				return false;
			return await IsEvaluatorAsync(actor, SignerEligibility.Parse(sheet.EvaluatorEligibilityJson));
		}

		/// <summary>An eligibility rule decides; without one only LMS managers evaluate.</summary>
		private async Task<bool> IsEvaluatorAsync(LmsActor actor, SignerEligibility rule)
		{
			if (rule == null)
				return actor.MayManage;
			try
			{
				return await _eligibility.IsEligibleAsync(actor.DepartmentId, actor.UserId, rule);
			}
			catch (Exception ex)
			{
				// Unknown eligibility is no eligibility.
				Logging.LogException(ex, "Evaluator eligibility could not be checked.");
				return false;
			}
		}

		public async Task<List<string>> GetNextSignerCandidatesAsync(int departmentId, LmsSkillSheet sheet)
		{
			var rule = SignerEligibility.Parse(sheet?.NextSignerEligibilityJson);
			if (rule == null)
				return new List<string>();
			return await _eligibility.GetEligibleUserIdsAsync(departmentId, rule) ?? new List<string>();
		}

		#endregion Eligibility

		#region Start

		public Task<LmsSkillEvaluation> StartAsync(LmsActor actor, LmsSkillEvaluationStart input, CancellationToken cancellationToken = default) =>
			StartInternalAsync(actor, input, false, cancellationToken);

		private sealed class StartPlan
		{
			public LmsSkillSheet Sheet;
			public LmsSkillSheetVersion Version;
			public LmsTaskBook Book;
			public LmsTaskBookTask Task;
			public List<string> Members;
			public Dictionary<string, int> Attempts;
		}

		/// <summary>Every rule a new evaluation must pass (shared by online start, offline submission and the post-class hook).</summary>
		private async Task<StartPlan> PlanAsync(LmsActor actor, int? sheetId, int? versionId, IEnumerable<string> memberUserIds, int? taskBookId, string taskKey, bool crewAllowed)
		{
			if (actor == null || !actor.IsCurrentMember || actor.DepartmentId <= 0)
				throw new InvalidOperationException("lms_skill_not_allowed");
			if (actor.Gate == null || !actor.Gate.CanAuthor)
				throw new InvalidOperationException("lms_skill_addon_required");

			var departmentId = actor.DepartmentId;
			var members = (memberUserIds ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (members.Count == 0 || members.Count > MaxMembers)
				throw new InvalidOperationException("lms_skill_members_required");
			var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			if (members.Any(m => !active.Contains(m)))
				throw new InvalidOperationException("lms_skill_member_invalid");

			var plan = new StartPlan { Members = members };
			SignerEligibility rule;
			if (taskBookId.HasValue)
			{
				var book = await _books.GetByIdForDepartmentAsync(departmentId, taskBookId.Value);
				if (book == null || book.DepartmentId != departmentId || book.Status != (int)LmsTaskBookStatuses.Open)
					throw new InvalidOperationException("lms_task_book_not_open");
				var task = LmsSkillRules.ParseTasks(book.TasksJson).FirstOrDefault(t => string.Equals(t.Key, taskKey, StringComparison.OrdinalIgnoreCase))
					?? throw new InvalidOperationException("lms_task_book_task_not_found");
				if (members.Count != 1 || !LmsSkillRules.SameUser(members[0], book.UserId))
					throw new InvalidOperationException("lms_task_book_member_mismatch");
				// A trainee never signs their own task (self-evaluation is not a task-book option).
				if (LmsSkillRules.SameUser(actor.UserId, book.UserId))
					throw new InvalidOperationException("lms_skill_self_evaluation");
				plan.Book = book;
				plan.Task = task;
				rule = SignerEligibility.Parse(book.EvaluatorEligibilityJson);
				sheetId = task.LmsSkillSheetId;
				if (!sheetId.HasValue)
					versionId = null;
			}
			else
			{
				if (!sheetId.HasValue)
					throw new InvalidOperationException("lms_skill_sheet_not_found");
				rule = null;
			}

			if (sheetId.HasValue)
			{
				var sheet = await _sheets.GetByIdForDepartmentAsync(departmentId, sheetId.Value);
				if (sheet == null || sheet.DepartmentId != departmentId || sheet.IsDeleted)
					throw new InvalidOperationException("lms_skill_sheet_not_found");
				LmsSkillSheetVersion version;
				if (versionId.HasValue)
				{
					version = await _versions.GetByIdForDepartmentAsync(departmentId, versionId.Value);
					if (version == null || version.LmsSkillSheetId != sheet.LmsSkillSheetId ||
						(version.Status != (int)ProtocolVersionStatuses.Published && version.Status != (int)ProtocolVersionStatuses.Superseded))
						throw new InvalidOperationException("lms_skill_sheet_not_published");
				}
				else
				{
					version = sheet.CurrentVersionId.HasValue ? await _versions.GetByIdForDepartmentAsync(departmentId, sheet.CurrentVersionId.Value) : null;
					if (version == null || version.Status != (int)ProtocolVersionStatuses.Published)
						throw new InvalidOperationException("lms_skill_sheet_not_published");
				}

				plan.Sheet = sheet;
				plan.Version = version;
				if (plan.Book == null)
				{
					rule = SignerEligibility.Parse(sheet.EvaluatorEligibilityJson);
					if (members.Count > 1 && !sheet.AllowCrewEvaluation && !crewAllowed)
						throw new InvalidOperationException("lms_skill_crew_not_allowed");
					if (!sheet.AllowSelfEvaluation && members.Any(m => LmsSkillRules.SameUser(m, actor.UserId)))
						throw new InvalidOperationException("lms_skill_self_evaluation");
				}
			}

			if (!await IsEvaluatorAsync(actor, rule))
				throw new InvalidOperationException("lms_skill_not_eligible");

			// Attempts: failures since the last pass in the version's window, per member, on this sheet.
			plan.Attempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			foreach (var member in members)
			{
				var prior = plan.Sheet == null ? new List<LmsSkillEvaluation>() :
					(await _evaluations.GetForMemberAsync(departmentId, member, plan.Sheet.LmsSkillSheetId) ?? new List<LmsSkillEvaluation>())
						.Where(e => e.Status == (int)LmsSkillEvaluationStatuses.AwaitingSignature || e.Status == (int)LmsSkillEvaluationStatuses.Completed).ToList();
				if (plan.Version != null && !LmsSkillRules.CanAttempt(prior.Select(e => (e.SubmittedOn ?? e.StartedOn, e.Result == (int)LmsSkillEvaluationResults.Passed)),
					plan.Version.MaxAttempts, plan.Version.AttemptWindowDays, now))
					throw new InvalidOperationException("lms_skill_attempts_exhausted");
				plan.Attempts[member] = prior.Count + 1;
			}
			return plan;
		}

		private async Task<LmsSkillEvaluation> StartInternalAsync(LmsActor actor, LmsSkillEvaluationStart input, bool crewAllowed, CancellationToken cancellationToken)
		{
			if (input == null)
				throw new InvalidOperationException("lms_skill_not_found");
			if (!string.IsNullOrWhiteSpace(input.EvaluatorUserId) && !LmsSkillRules.SameUser(input.EvaluatorUserId, actor?.UserId))
				throw new InvalidOperationException("lms_skill_not_evaluator");
			var key = LmsSkillSheetService.Trim(input.IdempotencyKey, 100);
			if (key != null && actor != null)
			{
				var existing = await _evaluations.GetByIdempotencyKeyAsync(actor.DepartmentId, key);
				if (existing != null && existing.DepartmentId == actor.DepartmentId)
					return existing;
			}

			var plan = await PlanAsync(actor, input.LmsSkillSheetId, null, input.MemberUserIds, input.LmsTaskBookId, input.TaskKey, crewAllowed);
			var now = DateTime.UtcNow;
			var started = input.StartedOnUtc.HasValue && input.StartedOnUtc.Value <= now && input.StartedOnUtc.Value >= now.AddDays(-1) ? input.StartedOnUtc.Value : now;
			return await InsertAsync(actor, plan, started, key, input.CallId, input.DeploymentId, input.Location, input.ClassSessionRef, cancellationToken);
		}

		private async Task<LmsSkillEvaluation> InsertAsync(LmsActor actor, StartPlan plan, DateTime startedUtc, string idempotencyKey, int? callId, int? deploymentId,
			string location, string classSessionRef, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var evaluation = new LmsSkillEvaluation
			{
				DepartmentId = actor.DepartmentId,
				LmsSkillSheetId = plan.Sheet?.LmsSkillSheetId,
				LmsSkillSheetVersionId = plan.Version?.LmsSkillSheetVersionId,
				Title = LmsSkillSheetService.Trim(plan.Task != null ? (plan.Book.PositionCode + " " + (plan.Task.Number ?? plan.Task.Key) + " " + plan.Task.Title) : plan.Sheet.Title, 300),
				Code = plan.Task != null ? LmsSkillSheetService.Trim(plan.Sheet?.Code ?? plan.Book.PositionCode, 100) : plan.Sheet.Code,
				Mode = plan.Members.Count > 1 ? (int)LmsSkillEvaluationModes.Crew : (int)LmsSkillEvaluationModes.Individual,
				Status = (int)LmsSkillEvaluationStatuses.InProgress,
				Result = (int)LmsSkillEvaluationResults.Pending,
				EvaluatorUserId = actor.UserId,
				CreatedByUserId = actor.UserId,
				StartedOn = startedUtc,
				AnswersJson = new LmsSkillAnswers().ToJson(),
				CallId = callId > 0 ? callId : null,
				DeploymentId = deploymentId > 0 ? deploymentId : null,
				Location = LmsSkillSheetService.Trim(location, 250),
				LmsTaskBookId = plan.Book?.LmsTaskBookId,
				TaskKey = plan.Task?.Key,
				ClassSessionRef = LmsSkillSheetService.Trim(classSessionRef, 100),
				IdempotencyKey = idempotencyKey,
				UpdatedOn = now
			};

			if (idempotencyKey != null)
			{
				if (!await _evaluations.TryInsertAsync(evaluation, cancellationToken))
					return await _evaluations.GetByIdempotencyKeyAsync(actor.DepartmentId, idempotencyKey) ?? throw new InvalidOperationException("lms_skill_stale");
			}
			else
				evaluation = await _evaluations.InsertAsync(evaluation, cancellationToken, true);

			foreach (var member in plan.Members)
				await _members.InsertAsync(new LmsSkillEvaluationMember
				{
					LmsSkillEvaluationId = evaluation.LmsSkillEvaluationId,
					DepartmentId = evaluation.DepartmentId,
					UserId = member,
					State = (int)LmsSkillEvaluationMemberStates.Included,
					AttemptNumber = plan.Attempts.TryGetValue(member, out var attempt) ? attempt : 1,
					CreatedOn = now
				}, cancellationToken, true);

			Audit(evaluation.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillEvaluationStarted, null,
				new { evaluation.LmsSkillEvaluationId, evaluation.LmsSkillSheetId, evaluation.LmsSkillSheetVersionId, evaluation.Mode, members = plan.Members.Count, evaluation.LmsTaskBookId, evaluation.TaskKey });
			return evaluation;
		}

		#endregion Start

		#region Grade

		private async Task<LmsSkillEvaluation> RequireOpenAsync(LmsActor actor, int lmsSkillEvaluationId)
		{
			if (actor == null || !actor.IsCurrentMember)
				throw new InvalidOperationException("lms_skill_not_allowed");
			var evaluation = await LoadAsync(actor.DepartmentId, lmsSkillEvaluationId) ?? throw new InvalidOperationException("lms_skill_evaluation_not_found");
			if (!LmsSkillRules.SameUser(evaluation.EvaluatorUserId, actor.UserId))
				throw new InvalidOperationException("lms_skill_not_evaluator");
			if (evaluation.Status != (int)LmsSkillEvaluationStatuses.InProgress)
				throw new InvalidOperationException("lms_skill_evaluation_closed");
			return evaluation;
		}

		public async Task<LmsSkillEvaluation> SaveAnswersAsync(LmsActor actor, int lmsSkillEvaluationId, long expectedRowVersion, LmsSkillAnswers answers, string notes, bool passAll,
			CancellationToken cancellationToken = default)
		{
			var evaluation = await RequireOpenAsync(actor, lmsSkillEvaluationId);
			var (_, _, content) = await ContentAsync(evaluation);
			var known = new HashSet<string>(content.Steps.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Key)).Select(s => s.Key), StringComparer.OrdinalIgnoreCase);
			var cleaned = new LmsSkillAnswers();
			foreach (var pair in (answers ?? LmsSkillAnswers.Parse(evaluation.AnswersJson)).Steps.Where(p => known.Contains(p.Key) && p.Value != null))
				cleaned.Steps[pair.Key] = pair.Value;
			if (passAll)
				cleaned = LmsSkillSheetEngine.PassAll(content, cleaned, DateTime.UtcNow);

			evaluation.AnswersJson = cleaned.ToJson();
			evaluation.Notes = LmsSkillSheetService.Trim(notes, 4000);
			evaluation.UpdatedOn = DateTime.UtcNow;
			if (!await _evaluations.TryUpdateAsync(evaluation, expectedRowVersion > 0 ? expectedRowVersion : evaluation.RowVersion, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");
			return evaluation;
		}

		public async Task<LmsSkillEvaluationFile> AddEvidenceAsync(LmsActor actor, int lmsSkillEvaluationId, string stepKey, string fileName, string contentType, byte[] data,
			CancellationToken cancellationToken = default)
		{
			var evaluation = await RequireOpenAsync(actor, lmsSkillEvaluationId);
			if (data == null || data.Length == 0 || data.Length > LmsSkillEvaluationFile.MaxBytes)
				throw new InvalidOperationException("lms_skill_evidence_size");
			var type = (contentType ?? string.Empty).Trim().ToLowerInvariant();
			if (!AllowedContentPrefixes.Any(p => type.StartsWith(p, StringComparison.Ordinal)) && !AllowedContentTypes.Contains(type))
				throw new InvalidOperationException("lms_skill_evidence_type");
			var (_, _, content) = await ContentAsync(evaluation);
			var step = LmsSkillSheetService.Trim(stepKey, 40);
			if (step != null && !content.Steps.Any(s => s != null && string.Equals(s.Key, step, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException("lms_skill_evidence_step");
			var existing = await _files.GetForEvaluationAsync(evaluation.DepartmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationFile>();
			var sha = Sha256Hex(data);
			// An outbox replay of the same file for the same step is the same evidence.
			var duplicate = existing.FirstOrDefault(f => string.Equals(f.Sha256, sha, StringComparison.OrdinalIgnoreCase) && string.Equals(f.StepKey, step, StringComparison.OrdinalIgnoreCase));
			if (duplicate != null)
				return duplicate;
			if (existing.Count >= MaxFilesPerEvaluation)
				throw new InvalidOperationException("lms_skill_evidence_count");

			var file = await _files.InsertAsync(new LmsSkillEvaluationFile
			{
				LmsSkillEvaluationId = evaluation.LmsSkillEvaluationId,
				DepartmentId = evaluation.DepartmentId,
				StepKey = step,
				FileName = LmsSkillSheetService.Trim(System.IO.Path.GetFileName(fileName ?? "evidence"), 250) ?? "evidence",
				ContentType = type,
				Size = data.Length,
				Sha256 = sha,
				Data = data,
				UploadedByUserId = actor.UserId,
				CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);
			// What the caller gets back never carries the bytes.
			return new LmsSkillEvaluationFile
			{
				LmsSkillEvaluationFileId = file.LmsSkillEvaluationFileId, LmsSkillEvaluationId = file.LmsSkillEvaluationId, DepartmentId = file.DepartmentId, StepKey = file.StepKey,
				FileName = file.FileName, ContentType = file.ContentType, Size = file.Size, Sha256 = file.Sha256, UploadedByUserId = file.UploadedByUserId, CreatedOn = file.CreatedOn
			};
		}

		public async Task<LmsSkillEvaluation> SplitMemberAsync(LmsActor actor, int lmsSkillEvaluationId, string userId, CancellationToken cancellationToken = default)
		{
			var evaluation = await RequireOpenAsync(actor, lmsSkillEvaluationId);
			if (evaluation.Mode != (int)LmsSkillEvaluationModes.Crew)
				throw new InvalidOperationException("lms_skill_split_not_crew");
			var members = await _members.GetForEvaluationAsync(evaluation.DepartmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationMember>();
			var included = members.Where(m => m.IsIncluded).ToList();
			var member = included.FirstOrDefault(m => LmsSkillRules.SameUser(m.UserId, userId)) ?? throw new InvalidOperationException("lms_skill_member_invalid");
			if (included.Count < 2)
				throw new InvalidOperationException("lms_skill_split_last_member");

			var now = DateTime.UtcNow;
			var split = await _evaluations.InsertAsync(new LmsSkillEvaluation
			{
				DepartmentId = evaluation.DepartmentId,
				LmsSkillSheetId = evaluation.LmsSkillSheetId,
				LmsSkillSheetVersionId = evaluation.LmsSkillSheetVersionId,
				Title = evaluation.Title,
				Code = evaluation.Code,
				Mode = (int)LmsSkillEvaluationModes.Individual,
				Status = (int)LmsSkillEvaluationStatuses.InProgress,
				Result = (int)LmsSkillEvaluationResults.Pending,
				EvaluatorUserId = evaluation.EvaluatorUserId,
				CreatedByUserId = actor.UserId,
				StartedOn = evaluation.StartedOn,
				AnswersJson = evaluation.AnswersJson,
				Notes = evaluation.Notes,
				CallId = evaluation.CallId,
				DeploymentId = evaluation.DeploymentId,
				Location = evaluation.Location,
				ClassSessionRef = evaluation.ClassSessionRef,
				ParentEvaluationId = evaluation.LmsSkillEvaluationId,
				UpdatedOn = now
			}, cancellationToken, true);

			await _members.InsertAsync(new LmsSkillEvaluationMember
			{
				LmsSkillEvaluationId = split.LmsSkillEvaluationId,
				DepartmentId = split.DepartmentId,
				UserId = member.UserId,
				State = (int)LmsSkillEvaluationMemberStates.Included,
				AttemptNumber = member.AttemptNumber,
				CreatedOn = now
			}, cancellationToken, true);

			member.State = (int)LmsSkillEvaluationMemberStates.SplitOut;
			member.SplitToEvaluationId = split.LmsSkillEvaluationId;
			await _members.UpdateAsync(member, cancellationToken, true);

			// One member left: the form is now that member's own evaluation.
			if (included.Count == 2)
			{
				var expected = evaluation.RowVersion;
				evaluation.Mode = (int)LmsSkillEvaluationModes.Individual;
				evaluation.UpdatedOn = now;
				await _evaluations.TryUpdateAsync(evaluation, expected, cancellationToken);
			}

			Audit(evaluation.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillEvaluationSplit, new { evaluation.LmsSkillEvaluationId },
				new { split.LmsSkillEvaluationId, member.UserId });
			return split;
		}

		#endregion Grade

		#region Submit

		public async Task<LmsSkillEvaluation> SubmitAsync(LmsActor actor, int lmsSkillEvaluationId, LmsSkillEvaluationSubmit input, CancellationToken cancellationToken = default)
		{
			input ??= new LmsSkillEvaluationSubmit();
			var evaluation = await RequireOpenAsync(actor, lmsSkillEvaluationId);
			var (sheet, version, content) = await ContentAsync(evaluation);
			var files = await _files.GetForEvaluationAsync(evaluation.DepartmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationFile>();
			var members = await _members.GetForEvaluationAsync(evaluation.DepartmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationMember>();
			var included = members.Where(m => m.IsIncluded).Select(m => m.UserId).ToList();
			if (included.Count == 0)
				throw new InvalidOperationException("lms_skill_members_required");

			var now = DateTime.UtcNow;
			var ended = input.EndedOnUtc.HasValue && input.EndedOnUtc.Value >= evaluation.StartedOn && input.EndedOnUtc.Value <= now ? input.EndedOnUtc.Value : now;
			var elapsed = LmsSkillRules.Elapsed(evaluation.StartedOn, ended);
			var outcome = LmsSkillSheetEngine.Evaluate(content, LmsSkillAnswers.Parse(evaluation.AnswersJson), version?.Grading, elapsed, EvidenceKeys(files), true);
			if (!outcome.Complete)
				throw new InvalidOperationException(outcome.Errors[0]);

			// The next signer the evaluator picks, restricted by the sheet's rule (task-book tasks have none: the certifying official signs the book).
			var nextRule = evaluation.LmsTaskBookId.HasValue ? null : SignerEligibility.Parse(sheet?.NextSignerEligibilityJson);
			var next = LmsSkillSheetService.Trim(input.NextSignerUserId, 128);
			if (nextRule == null && next != null)
				throw new InvalidOperationException("lms_skill_next_signer_not_allowed");
			if (nextRule != null)
			{
				if (next == null)
					throw new InvalidOperationException("lms_skill_next_signer_required");
				var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(evaluation.DepartmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
				if (!active.Contains(next) || LmsSkillRules.SameUser(next, evaluation.EvaluatorUserId) || included.Any(m => LmsSkillRules.SameUser(m, next)))
					throw new InvalidOperationException("lms_skill_next_signer_not_eligible");
				if (!await _eligibility.IsEligibleAsync(evaluation.DepartmentId, next, nextRule))
					throw new InvalidOperationException("lms_skill_next_signer_not_eligible");
			}

			// An outside evaluator (L4) signs instead of the member who ran the form: only while the add-on is active.
			var externalName = LmsSkillSheetService.Trim(input.ExternalEvaluatorName, 200);
			if (externalName != null && (actor.Gate == null || !actor.Gate.CanAuthor))
				throw new InvalidOperationException("lms_skill_addon_required");

			var expected = input.ExpectedRowVersion > 0 ? input.ExpectedRowVersion : evaluation.RowVersion;
			evaluation.Result = (int)(outcome.Passed ? LmsSkillEvaluationResults.Passed : LmsSkillEvaluationResults.Failed);
			evaluation.Score = outcome.Score;
			evaluation.CriticalFailed = outcome.CriticalFailed;
			evaluation.TimeExceeded = outcome.TimeExceeded;
			evaluation.DurationSeconds = elapsed;
			evaluation.SubmittedOn = now;
			evaluation.NextSignerUserId = next;
			evaluation.ExternalEvaluatorName = externalName;
			evaluation.Status = (int)LmsSkillEvaluationStatuses.AwaitingSignature;
			evaluation.ContentHash = LmsSkillSheetEngine.Sha256(LmsSkillSheetEngine.CanonicalEvaluation(evaluation, version?.ContentHash, members, files));
			evaluation.UpdatedOn = now;
			if (!await _evaluations.TryUpdateAsync(evaluation, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");

			SignatureRequestResult created;
			try
			{
				var evaluatorRule = evaluation.LmsTaskBookId.HasValue
					? SignerEligibility.Parse((await _books.GetByIdForDepartmentAsync(evaluation.DepartmentId, evaluation.LmsTaskBookId.Value))?.EvaluatorEligibilityJson)
					: SignerEligibility.Parse(sheet?.EvaluatorEligibilityJson);
				created = await _requests.CreateEvaluationAsync(new EvaluationRequestInput
				{
					DepartmentId = evaluation.DepartmentId,
					SubjectKind = SignatureSubjectKinds.SkillEvaluation,
					SubjectId = LmsSkillRules.EvaluationSubjectId(evaluation.LmsSkillEvaluationId),
					EvaluatorUserId = externalName == null ? evaluation.EvaluatorUserId : null,
					Eligibility = externalName == null ? evaluatorRule : null,
					ExternalEvaluatorName = externalName,
					ExternalEvaluatorEmail = externalName == null ? null : input.ExternalEvaluatorEmail,
					ExternalChannel = input.ExternalRemote ? SignatureRequestChannels.Remote : SignatureRequestChannels.InPerson,
					CertifyingOfficialUserId = next,
					CertifyingEligibility = next == null ? null : nextRule,
					Culture = input.Culture,
					RequestedByUserId = actor.UserId,
					IpAddress = input.IpAddress,
					UserAgent = input.UserAgent
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The evaluator signature request could not be created.");
				created = SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			}

			if (created == null || !created.Succeeded || created.Request == null)
			{
				// Back to grading: nothing is fixed without a request to sign.
				var reopen = evaluation.RowVersion;
				evaluation.Status = (int)LmsSkillEvaluationStatuses.InProgress;
				evaluation.Result = (int)LmsSkillEvaluationResults.Pending;
				evaluation.SubmittedOn = null;
				evaluation.ContentHash = null;
				evaluation.UpdatedOn = DateTime.UtcNow;
				await _evaluations.TryUpdateAsync(evaluation, reopen, CancellationToken.None);
				throw new InvalidOperationException("lms_skill_signature_refused:" + (created?.Refusal ?? SignatureRequestRefusals.InvalidRequest));
			}

			var stored = evaluation.RowVersion;
			evaluation.SignatureRequestId = created.Request.SignatureRequestId;
			await _evaluations.TryUpdateAsync(evaluation, stored, CancellationToken.None);

			Audit(evaluation.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillEvaluationSubmitted, null,
				new { evaluation.LmsSkillEvaluationId, evaluation.Result, evaluation.Score, evaluation.CriticalFailed, evaluation.TimeExceeded, evaluation.DurationSeconds, evaluation.ContentHash, evaluation.SignatureRequestId });
			return evaluation;
		}

		public async Task<LmsSkillEvaluation> SubmitOfflineAsync(LmsActor actor, LmsSkillOfflineSubmission submission, CancellationToken cancellationToken = default)
		{
			var key = LmsSkillSheetService.Trim(submission?.IdempotencyKey, 100) ?? throw new InvalidOperationException("lms_skill_idempotency_key_required");
			if (actor == null || !actor.IsCurrentMember)
				throw new InvalidOperationException("lms_skill_not_allowed");

			// A replay (the outbox resending after a lost response) returns what the first delivery made, whatever its state now.
			var existing = await _evaluations.GetByIdempotencyKeyAsync(actor.DepartmentId, key);
			if (existing != null && existing.DepartmentId == actor.DepartmentId)
				return existing;

			var plan = await PlanAsync(actor, submission.LmsSkillSheetId > 0 ? submission.LmsSkillSheetId : (int?)null,
				submission.LmsSkillSheetVersionId > 0 ? submission.LmsSkillSheetVersionId : (int?)null, submission.MemberUserIds, submission.LmsTaskBookId, submission.TaskKey, false);
			var now = DateTime.UtcNow;
			var started = submission.StartedOnUtc > DateTime.MinValue && submission.StartedOnUtc <= now && submission.StartedOnUtc >= now.AddDays(-30) ? submission.StartedOnUtc : now;
			var evaluation = await InsertAsync(actor, plan, started, key, submission.CallId, submission.DeploymentId, submission.Location, null, cancellationToken);
			if (evaluation.Status != (int)LmsSkillEvaluationStatuses.InProgress)
				return evaluation;

			evaluation = await SaveAnswersAsync(actor, evaluation.LmsSkillEvaluationId, evaluation.RowVersion, submission.Answers ?? new LmsSkillAnswers(), submission.Notes, false, cancellationToken);
			var ended = submission.EndedOnUtc >= started && submission.EndedOnUtc <= now ? submission.EndedOnUtc : now;
			// A submission the engine refuses stays in progress (the evaluator finishes it online); the replay returns it.
			return await SubmitAsync(actor, evaluation.LmsSkillEvaluationId, new LmsSkillEvaluationSubmit
			{
				ExpectedRowVersion = evaluation.RowVersion,
				NextSignerUserId = submission.NextSignerUserId,
				EndedOnUtc = ended,
				Culture = submission.Culture
			}, cancellationToken);
		}

		#endregion Submit

		#region Complete, void and signatures

		public async Task<bool> CompleteFromSignatureAsync(SignatureRequestRecord request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.Status != (int)SignatureRequestStatuses.Completed || request.SubjectKind != (int)SignatureSubjectKinds.SkillEvaluation)
				return false;
			var (kind, id) = LmsSkillRules.ParseSubject(request.SubjectId);
			if (kind != "evaluation")
				return false;
			var evaluation = await LoadAsync(request.DepartmentId, id);
			// Idempotent: OnSignedAsync runs once per signed step, and a second call finds the evaluation completed.
			if (evaluation == null || evaluation.SignatureRequestId != request.SignatureRequestId || evaluation.Status != (int)LmsSkillEvaluationStatuses.AwaitingSignature)
				return false;

			var expected = evaluation.RowVersion;
			evaluation.Status = (int)LmsSkillEvaluationStatuses.Completed;
			evaluation.CompletedOn = request.CompletedOn ?? DateTime.UtcNow;
			evaluation.UpdatedOn = DateTime.UtcNow;
			if (!await _evaluations.TryUpdateAsync(evaluation, expected, cancellationToken))
				return false;

			var members = (await _members.GetForEvaluationAsync(evaluation.DepartmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationMember>()).Where(m => m.IsIncluded).ToList();
			if (evaluation.Result == (int)LmsSkillEvaluationResults.Passed && members.Count > 0)
			{
				try
				{
					var (sheet, version, _) = await ContentAsync(evaluation);
					await _writer.WriteSourceAsync(evaluation.DepartmentId, LearningRecordSourceKinds.SkillEvaluation, LmsSkillRules.EvaluationSourceRef(evaluation.LmsSkillEvaluationId),
						Drafts(evaluation, sheet, version, members), evaluation.EvaluatorUserId, cancellationToken);
				}
				catch (Exception ex)
				{
					// Non-fatal: the evaluation stands; a re-run writes the records again (the writer is idempotent).
					Logging.LogException(ex, $"Learning records of skill evaluation {evaluation.LmsSkillEvaluationId} could not be written.");
				}
			}

			if (evaluation.LmsTaskBookId.HasValue)
			{
				try { await _taskBooks.OnTaskEvaluatedAsync(evaluation, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, "The task book could not be told about an evaluation."); }
			}

			// Queued as a ContentNotices key: worker 96 writes it in each recipient's language (round 9C).
			var passed = evaluation.Result == (int)LmsSkillEvaluationResults.Passed;
			var notice = evaluation.Score.HasValue
				? LmsSkillNoticeText.Encode(passed ? "Skill.EvaluationPassedScore" : "Skill.EvaluationNotPassedScore", evaluation.Title, evaluation.Score.Value, Link(evaluation.LmsSkillEvaluationId))
				: LmsSkillNoticeText.Encode(passed ? "Skill.EvaluationPassed" : "Skill.EvaluationNotPassed", evaluation.Title, Link(evaluation.LmsSkillEvaluationId));
			await _notifications.QueueAsync(evaluation.DepartmentId, LmsSkillNotificationKinds.EvaluationCompleted, notice, members.Select(m => m.UserId), true, cancellationToken);

			Audit(evaluation.DepartmentId, request.CreatedByUserId, AuditLogTypes.LmsSkillEvaluationCompleted, null,
				new { evaluation.LmsSkillEvaluationId, evaluation.Result, evaluation.SignatureRequestId, members = members.Count });
			// Workflow trigger 262, one per evaluated member (the compare-and-set above completed it once): pass or fail and the score.
			var raisedOn = DateTime.UtcNow;
			foreach (var member in members.Where(m => !string.IsNullOrWhiteSpace(m.UserId)).GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
				ContentWorkflowContext.Publish(_events, LmsWorkflowEvents.EvaluationCompleted(evaluation, member.UserId, members.Count, raisedOn));
			return true;
		}

		/// <summary>One learning record per included member: identical for a crew (one form), the sheet code as the course code, the duration as hours.</summary>
		public static List<LearningRecordDraft> Drafts(LmsSkillEvaluation evaluation, LmsSkillSheet sheet, LmsSkillSheetVersion version, IEnumerable<LmsSkillEvaluationMember> members)
		{
			var completed = evaluation.SubmittedOn ?? evaluation.CompletedOn ?? evaluation.StartedOn;
			return members.Where(m => m.IsIncluded && !string.IsNullOrWhiteSpace(m.UserId)).Select(m => new LearningRecordDraft
			{
				UserId = m.UserId,
				ComponentKey = string.Empty,
				// A post-class evaluation is the same session as its class (R-T2: one session counted once).
				SessionKey = evaluation.ClassSessionRef ?? LmsSkillRules.EvaluationSourceRef(evaluation.LmsSkillEvaluationId),
				Title = evaluation.Title,
				CourseCode = evaluation.Code,
				CompletedOn = completed,
				StartedOn = evaluation.StartedOn,
				EndedOn = evaluation.SubmittedOn,
				Hours = LmsSkillRules.Hours(evaluation.DurationSeconds, version?.DefaultMinutes ?? 0),
				CategoryId = sheet?.CategoryId,
				MethodOfTraining = (int)MethodsOfTraining.DrillField,
				Location = evaluation.Location,
				InstructorUserIds = evaluation.EvaluatorUserId,
				Tags = "skill-evaluation",
				Status = LearningRecordStatuses.Verified,
				VerifiedByUserId = evaluation.EvaluatorUserId
			}).ToList();
		}

		public async Task<LmsSkillEvaluation> VoidAsync(LmsActor actor, int lmsSkillEvaluationId, string reason, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				throw new InvalidOperationException("lms_skill_not_allowed");
			var evaluation = await LoadAsync(actor.DepartmentId, lmsSkillEvaluationId) ?? throw new InvalidOperationException("lms_skill_evaluation_not_found");
			if (evaluation.Status == (int)LmsSkillEvaluationStatuses.Voided)
				return evaluation;
			var why = LmsSkillSheetService.Trim(reason, 1000) ?? throw new InvalidOperationException("lms_skill_void_reason_required");
			var before = evaluation.Status;

			var expected = evaluation.RowVersion;
			evaluation.Status = (int)LmsSkillEvaluationStatuses.Voided;
			evaluation.VoidedOn = DateTime.UtcNow;
			evaluation.VoidedByUserId = actor.UserId;
			evaluation.VoidReason = why;
			evaluation.UpdatedOn = DateTime.UtcNow;
			if (!await _evaluations.TryUpdateAsync(evaluation, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");

			if (before == (int)LmsSkillEvaluationStatuses.AwaitingSignature && evaluation.SignatureRequestId.HasValue)
			{
				try { await _requests.VoidAsync(evaluation.DepartmentId, evaluation.SignatureRequestId.Value, actor.UserId, why, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, "The evaluator signature request could not be withdrawn."); }
			}
			if (before == (int)LmsSkillEvaluationStatuses.Completed)
			{
				try
				{
					await _writer.VoidSourceAsync(evaluation.DepartmentId, LearningRecordSourceKinds.SkillEvaluation, LmsSkillRules.EvaluationSourceRef(evaluation.LmsSkillEvaluationId),
						why, actor.UserId, cancellationToken);
				}
				catch (Exception ex) { Logging.LogException(ex, "The learning records of a voided evaluation could not be voided."); }
			}

			Audit(evaluation.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillEvaluationVoided, new { evaluation.LmsSkillEvaluationId, status = before }, new { why });
			return evaluation;
		}

		public async Task<SignatureSubjectInfo> DescribeForSignatureAsync(int departmentId, int lmsSkillEvaluationId)
		{
			var evaluation = await LoadAsync(departmentId, lmsSkillEvaluationId);
			if (evaluation == null || (evaluation.Status != (int)LmsSkillEvaluationStatuses.AwaitingSignature && evaluation.Status != (int)LmsSkillEvaluationStatuses.Completed))
				return null;
			var (sheet, version, content) = await ContentAsync(evaluation);
			var members = await _members.GetForEvaluationAsync(departmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationMember>();
			var files = await _files.GetForEvaluationAsync(departmentId, evaluation.LmsSkillEvaluationId) ?? new List<LmsSkillEvaluationFile>();
			// Recomputed from what is stored now: any change after submission is a different document (the signature is refused).
			var hash = LmsSkillSheetEngine.Sha256(LmsSkillSheetEngine.CanonicalEvaluation(evaluation, version?.ContentHash, members, files));
			var names = Names(await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId));
			var included = members.Where(m => m.IsIncluded).Select(m => Name(names, m.UserId)).ToList();
			var title = evaluation.Title + " - " + string.Join(", ", included.Take(5)) + (included.Count > 5 ? " +" + (included.Count - 5).ToString(CultureInfo.InvariantCulture) : string.Empty);
			return new SignatureSubjectInfo
			{
				Kind = SignatureSubjectKinds.SkillEvaluation,
				SubjectId = LmsSkillRules.EvaluationSubjectId(evaluation.LmsSkillEvaluationId),
				Title = title.Length > 250 ? title.Substring(0, 250) : title,
				RevisionId = "evaluation-" + evaluation.LmsSkillEvaluationId.ToString(CultureInfo.InvariantCulture),
				RevisionSha256 = hash,
				DocumentHtml = await DocumentAsync(evaluation, sheet, version, content, members, files, names),
				ContextId = evaluation.LmsTaskBookId?.ToString(CultureInfo.InvariantCulture)
			};
		}

		/// <summary>The evaluation as the sealed PDF shows it: result, every step's answer and annotation, timed notes, and the evidence with its SHA-256 (images embedded).</summary>
		private async Task<string> DocumentAsync(LmsSkillEvaluation evaluation, LmsSkillSheet sheet, LmsSkillSheetVersion version, LmsSkillSheetContent content,
			List<LmsSkillEvaluationMember> members, List<LmsSkillEvaluationFile> files, Dictionary<string, string> names)
		{
			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var answers = LmsSkillAnswers.Parse(evaluation.AnswersJson);
			var html = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px}" +
				"th,td{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}table{border-collapse:collapse;width:100%;margin-bottom:10px}" +
				"img{max-width:320px;max-height:240px}h2{font-size:14px;margin:12px 0 4px}</style></head><body>");
			html.Append("<h1>").Append(E(evaluation.Title)).Append("</h1><table>");
			void Row(string label, string value) => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			Row("Skill sheet", sheet == null ? "Task sign-off" : sheet.Title + (version?.Label == null ? string.Empty : " v" + version.Label) + (string.IsNullOrWhiteSpace(sheet.Code) ? string.Empty : " (" + sheet.Code + ")"));
			Row("Evaluated", string.Join(", ", members.Where(m => m.IsIncluded).Select(m => Name(names, m.UserId) + " (attempt " + m.AttemptNumber.ToString(CultureInfo.InvariantCulture) + ")")));
			Row("Evaluator", evaluation.ExternalEvaluatorName ?? Name(names, evaluation.EvaluatorUserId));
			if (!string.IsNullOrWhiteSpace(evaluation.NextSignerUserId))
				Row("Next signer", Name(names, evaluation.NextSignerUserId));
			Row("Started (UTC)", evaluation.StartedOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
			Row("Elapsed", TimeSpan.FromSeconds(evaluation.DurationSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
			Row("Result", evaluation.Result == (int)LmsSkillEvaluationResults.Passed ? "Passed" : "Not passed");
			Row("Score", evaluation.Score.HasValue ? evaluation.Score.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%" : "-");
			if (evaluation.CriticalFailed)
				Row("Critical criteria", "A critical step was not met");
			if (evaluation.TimeExceeded)
				Row("Time limit", "Exceeded");
			if (evaluation.CallId.HasValue)
				Row("Call", evaluation.CallId.Value.ToString(CultureInfo.InvariantCulture));
			if (evaluation.DeploymentId.HasValue)
				Row("Deployment", evaluation.DeploymentId.Value.ToString(CultureInfo.InvariantCulture));
			if (!string.IsNullOrWhiteSpace(evaluation.Location))
				Row("Location", evaluation.Location);
			html.Append("</table><h2>Steps</h2><table><tr><th>Step</th><th>Answer</th><th>Annotation</th></tr>");
			foreach (var step in content.Steps.Where(s => s != null))
			{
				if (step.StepType == LmsSkillStepTypes.Heading)
				{
					html.Append("<tr><th colspan=\"3\">").Append(E(step.Label)).Append("</th></tr>");
					continue;
				}
				if (step.StepType == LmsSkillStepTypes.Instructions)
					continue;
				var answer = answers.Get(step.Key);
				var value = answer == null ? "-" : answer.Status == (int)LmsSkillAnswerStatuses.NotApplicable ? "N/A: " + answer.NotApplicableReason
					: step.StepType == LmsSkillStepTypes.ChooseAny ? string.Join(", ", (answer.Values ?? new List<string>()).Select(v => step.Options?.FirstOrDefault(o => string.Equals(o.Key, v, StringComparison.OrdinalIgnoreCase))?.Label ?? v))
					: step.StepType == LmsSkillStepTypes.ChooseOne ? step.Options?.FirstOrDefault(o => string.Equals(o.Key, answer.Value, StringComparison.OrdinalIgnoreCase))?.Label ?? answer.Value
					: step.StepType == LmsSkillStepTypes.TimestampedNotes ? string.Join("; ", (answer.Notes ?? new List<LmsSkillTimedNote>()).Select(n => n.At.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + n.Text))
					: answer.Value;
				html.Append("<tr><td>").Append(E(step.Label)).Append(step.Critical ? " <strong>(critical)</strong>" : string.Empty).Append("</td><td>").Append(E(value))
					.Append("</td><td>").Append(E(answer?.Note)).Append("</td></tr>");
			}
			html.Append("</table>");
			if (!string.IsNullOrWhiteSpace(evaluation.Notes))
				html.Append("<h2>Evaluator comments</h2><p>").Append(E(evaluation.Notes)).Append("</p>");

			if (files.Count > 0)
			{
				html.Append("<h2>Evidence</h2><table><tr><th>File</th><th>Step</th><th>SHA-256</th><th></th></tr>");
				var embedded = 0;
				foreach (var file in files)
				{
					string image = null;
					if (file.IsImage && file.Size <= MaxEmbeddedImageBytes && embedded + file.Size <= MaxEmbeddedTotalBytes)
					{
						var full = await _files.GetWithDataAsync(evaluation.DepartmentId, file.LmsSkillEvaluationFileId);
						if (full?.Data != null)
						{
							image = "<img alt=\"\" src=\"data:" + E(file.ContentType) + ";base64," + Convert.ToBase64String(full.Data) + "\">";
							embedded += file.Size;
						}
					}
					html.Append("<tr><td>").Append(E(file.FileName)).Append(" (").Append(E(file.ContentType)).Append(", ").Append(file.Size.ToString(CultureInfo.InvariantCulture)).Append(" bytes)</td><td>")
						.Append(E(content.Steps.FirstOrDefault(s => string.Equals(s?.Key, file.StepKey, StringComparison.OrdinalIgnoreCase))?.Label)).Append("</td><td style=\"font-family:monospace;font-size:10px\">")
						.Append(E(file.Sha256)).Append("</td><td>").Append(image ?? string.Empty).Append("</td></tr>");
				}
				html.Append("</table>");
			}
			html.Append("</body></html>");
			return html.ToString();
		}

		public async Task<byte[]> GetSealedPdfAsync(int departmentId, int lmsSkillEvaluationId, CancellationToken cancellationToken = default)
		{
			var evaluation = await LoadAsync(departmentId, lmsSkillEvaluationId);
			if (evaluation == null || evaluation.Status != (int)LmsSkillEvaluationStatuses.Completed || !evaluation.SignatureRequestId.HasValue)
				return null;
			var request = await _requests.GetAsync(departmentId, evaluation.SignatureRequestId.Value);
			if (request?.SealedDocumentId == null)
				return null;
			var document = await _sealing.GetByIdAsync(departmentId, request.SealedDocumentId.Value);
			// Fails closed: only a sealed document is handed out as the evaluation's record.
			if (document == null || document.Status != (int)SealedDocumentStatuses.Sealed)
				return null;
			return await _sealing.OpenAsync(departmentId, document.SealedDocumentId, cancellationToken);
		}

		#endregion Complete, void and signatures

		#region Post-class hook

		public async Task<LmsSkillEvaluation> StartPostClassEvaluationAsync(LmsPostClassEvaluationRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.ClassSessionRef) || request.LmsSkillSheetId <= 0)
				throw new InvalidOperationException("lms_skill_not_found");
			var evaluatorId = LmsSkillSheetService.Trim(request.EvaluatorUserId ?? request.RequestedByUserId, 128) ?? throw new InvalidOperationException("lms_skill_not_evaluator");
			var actor = await _gate.GetActorAsync(request.DepartmentId, evaluatorId) ?? LmsActor.Nobody(request.DepartmentId);
			// One evaluation per session and sheet: calling again returns it.
			var key = "class:" + request.ClassSessionRef.Trim() + ":" + request.LmsSkillSheetId.ToString(CultureInfo.InvariantCulture);
			return await StartInternalAsync(actor, new LmsSkillEvaluationStart
			{
				LmsSkillSheetId = request.LmsSkillSheetId,
				MemberUserIds = request.MemberUserIds,
				ClassSessionRef = request.ClassSessionRef,
				Location = request.Location,
				IdempotencyKey = key.Length > 100 ? key.Substring(0, 100) : key
			}, true, cancellationToken);
		}

		public async Task<List<LmsSkillEvaluation>> GetForClassSessionAsync(int departmentId, string classSessionRef) =>
			await _evaluations.GetForClassSessionAsync(departmentId, classSessionRef) ?? new List<LmsSkillEvaluation>();

		#endregion Post-class hook

		#region Helpers

		public static string Link(int lmsSkillEvaluationId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/LmsSkills/Evaluation/" + lmsSkillEvaluationId.ToString(CultureInfo.InvariantCulture);

		internal static Dictionary<string, string> Names(IEnumerable<PersonName> names) =>
			(names ?? Enumerable.Empty<PersonName>()).Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		internal static string Name(IReadOnlyDictionary<string, string> names, string userId) =>
			userId != null && names != null && names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		internal static string Sha256Hex(byte[] data)
		{
			var hash = SHA256.HashData(data ?? Array.Empty<byte>());
			var builder = new StringBuilder(64);
			foreach (var b in hash)
				builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, object before, object after) =>
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before == null ? null : JsonConvert.SerializeObject(before),
				After = after == null ? null : JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});

		#endregion Helpers
	}
}
