using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
	/// Position task books (T4 item 3, NWCG model, LMS add-on). A book is initiated for a member with a position code and a date; the
	/// definition's tasks are snapshotted and the book is valid for the definition's months (3 years by default; worker 96 expires it). Tasks
	/// are signed by passed, completed skill evaluations that reference the book (qualified evaluators by the book's rule; an incident task
	/// counts only evaluations that reference a call or a deployment). When every task is complete one S3 ordered request asks the final
	/// evaluator to recommend and then the certifying official to certify (subject "task-book:{id}"); completion moves the certification
	/// record from Trainee to Active (renewed to the re-evaluation interval: OSHA operators every 3 years) and writes a TaskBookSignoff
	/// learning record once.
	/// </summary>
	public class LmsTaskBookService : ILmsTaskBookService
	{
		private readonly ILmsTaskBookDefinitionsRepository _definitions;
		private readonly ILmsTaskBooksRepository _books;
		private readonly ILmsSkillEvaluationsRepository _evaluations;
		private readonly ILmsSkillSheetsRepository _sheets;
		private readonly ICertificationService _certifications;
		private readonly ILearningRecordWriter _writer;
		private readonly ISignatureRequestService _requests;
		private readonly ISignerEligibilityService _eligibility;
		private readonly IDepartmentsService _departments;
		private readonly ILmsSkillNotificationService _notifications;
		private readonly IEventAggregator _events;

		public LmsTaskBookService(ILmsTaskBookDefinitionsRepository definitions, ILmsTaskBooksRepository books, ILmsSkillEvaluationsRepository evaluations,
			ILmsSkillSheetsRepository sheets, ICertificationService certifications, ILearningRecordWriter writer, ISignatureRequestService requests,
			ISignerEligibilityService eligibility, IDepartmentsService departments, ILmsSkillNotificationService notifications, IEventAggregator events)
		{
			_definitions = definitions;
			_books = books;
			_evaluations = evaluations;
			_sheets = sheets;
			_certifications = certifications;
			_writer = writer;
			_requests = requests;
			_eligibility = eligibility;
			_departments = departments;
			_notifications = notifications;
			_events = events;
		}

		#region Definitions

		private static void RequireManager(LmsActor actor, bool needsAddon)
		{
			if (actor == null || !actor.IsCurrentMember || !actor.MayManage)
				throw new InvalidOperationException("lms_skill_not_allowed");
			if (needsAddon && (actor.Gate == null || !actor.Gate.CanAuthor))
				throw new InvalidOperationException("lms_skill_addon_required");
		}

		public async Task<List<LmsTaskBookDefinition>> GetDefinitionsAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsTaskBookDefinition>() : await _definitions.GetForDepartmentAsync(departmentId) ?? new List<LmsTaskBookDefinition>();

		public async Task<LmsTaskBookDefinition> GetDefinitionAsync(int departmentId, int lmsTaskBookDefinitionId)
		{
			var definition = lmsTaskBookDefinitionId <= 0 ? null : await _definitions.GetByIdForDepartmentAsync(departmentId, lmsTaskBookDefinitionId);
			return definition != null && definition.DepartmentId == departmentId ? definition : null;
		}

		public async Task<LmsTaskBookDefinition> SaveDefinitionAsync(LmsActor actor, LmsTaskBookDefinition definition, IReadOnlyCollection<LmsTaskBookTask> tasks,
			CancellationToken cancellationToken = default)
		{
			RequireManager(actor, true);
			if (definition == null)
				throw new InvalidOperationException("lms_task_book_not_found");
			var name = LmsSkillSheetService.Trim(definition.Name, 250) ?? throw new InvalidOperationException("lms_task_book_name_required");
			var code = LmsSkillSheetService.Trim(definition.PositionCode, 40) ?? throw new InvalidOperationException("lms_task_book_code_required");
			if (!Enum.IsDefined(typeof(LmsTaskBookFamilies), definition.Family))
				throw new InvalidOperationException("lms_task_book_family_invalid");
			var cleaned = (tasks ?? Array.Empty<LmsTaskBookTask>()).Where(t => t != null).Select(t => new LmsTaskBookTask
			{
				Key = (t.Key ?? string.Empty).Trim(),
				Number = LmsSkillSheetService.Trim(t.Number, 40),
				Title = LmsSkillSheetService.Trim(t.Title, 300),
				RequiredEvaluations = t.RequiredEvaluations,
				RequiresIncident = t.RequiresIncident,
				LmsSkillSheetId = t.LmsSkillSheetId > 0 ? t.LmsSkillSheetId : null
			}).ToList();
			var errors = LmsSkillRules.ValidateTasks(cleaned);
			if (errors.Count > 0)
				throw new InvalidOperationException(errors[0]);
			var sheetIds = cleaned.Where(t => t.LmsSkillSheetId.HasValue).Select(t => t.LmsSkillSheetId.Value).Distinct().ToList();
			if (sheetIds.Count > 0)
			{
				var sheets = await _sheets.GetByIdsAsync(actor.DepartmentId, sheetIds) ?? new List<LmsSkillSheet>();
				if (sheetIds.Any(id => !sheets.Any(s => s.LmsSkillSheetId == id && s.DepartmentId == actor.DepartmentId && !s.IsDeleted)))
					throw new InvalidOperationException("lms_task_book_sheet_invalid");
			}
			if (definition.CertificationTypeId.HasValue && definition.CertificationTypeId.Value > 0)
			{
				var type = await _certifications.GetCertificationTypeByIdAsync(definition.CertificationTypeId.Value);
				if (type == null || type.DepartmentId != actor.DepartmentId || type.IsDeleted || type.AppliesTo != (int)CertificationAppliesTo.Person)
					throw new InvalidOperationException("lms_task_book_certification_type_invalid");
			}

			var now = DateTime.UtcNow;
			LmsTaskBookDefinition target;
			if (definition.LmsTaskBookDefinitionId > 0)
			{
				target = await GetDefinitionAsync(actor.DepartmentId, definition.LmsTaskBookDefinitionId);
				if (target == null || target.IsDeleted)
					throw new InvalidOperationException("lms_task_book_not_found");
			}
			else
				target = new LmsTaskBookDefinition { DepartmentId = actor.DepartmentId, CreatedOn = now, CreatedByUserId = actor.UserId, TemplateKey = definition.TemplateKey };

			var expected = target.RowVersion;
			target.Name = name;
			target.PositionCode = code;
			target.Description = LmsSkillSheetService.Trim(definition.Description, 4000);
			target.Family = definition.Family;
			target.TasksJson = JsonConvert.SerializeObject(cleaned);
			target.CertificationTypeId = definition.CertificationTypeId > 0 ? definition.CertificationTypeId : null;
			target.ValidityMonths = definition.ValidityMonths <= 0 ? LmsTaskBookDefinition.DefaultValidityMonths : Math.Min(definition.ValidityMonths, 120);
			target.RecertifyMonths = Math.Clamp(definition.RecertifyMonths, 0, 120);
			target.EvaluatorEligibilityJson = SignerEligibility.Parse(definition.EvaluatorEligibilityJson)?.ToJson();
			target.FinalEvaluatorEligibilityJson = SignerEligibility.Parse(definition.FinalEvaluatorEligibilityJson)?.ToJson();
			target.CertifyingEligibilityJson = SignerEligibility.Parse(definition.CertifyingEligibilityJson)?.ToJson();

			if (target.LmsTaskBookDefinitionId > 0)
			{
				target.UpdatedOn = now;
				target.UpdatedByUserId = actor.UserId;
				if (!await _definitions.TryUpdateAsync(target, expected, cancellationToken))
					throw new InvalidOperationException("lms_skill_stale");
			}
			else
				target = await _definitions.InsertAsync(target, cancellationToken, true);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsTaskBookDefinitionChanged, null, new { target.LmsTaskBookDefinitionId, target.PositionCode, target.Name, tasks = cleaned.Count });
			return target;
		}

		public async Task<LmsTaskBookDefinition> CreateFromTemplateAsync(LmsActor actor, string templateKey, CancellationToken cancellationToken = default)
		{
			RequireManager(actor, true);
			var template = LmsSkillTemplateCatalog.FindTaskBook(templateKey) ?? throw new InvalidOperationException("lms_skill_template_not_found");
			return await SaveDefinitionAsync(actor, new LmsTaskBookDefinition
			{
				Name = template.Name,
				PositionCode = template.PositionCode,
				Description = template.Description,
				Family = (int)template.Family,
				ValidityMonths = template.ValidityMonths,
				RecertifyMonths = template.RecertifyMonths,
				TemplateKey = template.Key
			}, template.Tasks.Select(t => new LmsTaskBookTask { Key = t.Key, Number = t.Number, Title = t.Title, RequiredEvaluations = t.RequiredEvaluations, RequiresIncident = t.RequiresIncident }).ToList(),
				cancellationToken);
		}

		public async Task<bool> DeleteDefinitionAsync(LmsActor actor, int lmsTaskBookDefinitionId, CancellationToken cancellationToken = default)
		{
			RequireManager(actor, false);
			var definition = await GetDefinitionAsync(actor.DepartmentId, lmsTaskBookDefinitionId) ?? throw new InvalidOperationException("lms_task_book_not_found");
			var expected = definition.RowVersion;
			definition.IsDeleted = true;
			definition.UpdatedOn = DateTime.UtcNow;
			definition.UpdatedByUserId = actor.UserId;
			if (!await _definitions.TryUpdateAsync(definition, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsTaskBookDefinitionChanged, new { definition.LmsTaskBookDefinitionId, definition.Name }, new { deleted = true });
			return true;
		}

		#endregion Definitions

		#region Books

		public async Task<List<LmsTaskBook>> GetTaskBooksAsync(int departmentId, bool openOnly) =>
			departmentId <= 0 ? new List<LmsTaskBook>() : await _books.GetForDepartmentAsync(departmentId, openOnly) ?? new List<LmsTaskBook>();

		public async Task<List<LmsTaskBook>> GetForMemberAsync(int departmentId, string userId) =>
			await _books.GetForMemberAsync(departmentId, userId) ?? new List<LmsTaskBook>();

		private async Task<LmsTaskBook> LoadAsync(int departmentId, int lmsTaskBookId)
		{
			var book = departmentId <= 0 || lmsTaskBookId <= 0 ? null : await _books.GetByIdForDepartmentAsync(departmentId, lmsTaskBookId);
			return book != null && book.DepartmentId == departmentId ? book : null;
		}

		public async Task<LmsTaskBookView> GetViewAsync(int departmentId, int lmsTaskBookId)
		{
			var book = await LoadAsync(departmentId, lmsTaskBookId);
			if (book == null)
				return null;
			var evaluations = await _evaluations.GetForTaskBookAsync(departmentId, book.LmsTaskBookId) ?? new List<LmsSkillEvaluation>();
			return new LmsTaskBookView
			{
				Book = book,
				Tasks = LmsSkillRules.ParseTasks(book.TasksJson).Select(t => LmsSkillRules.TaskProgress(t, evaluations)).ToList()
			};
		}

		public async Task<LmsTaskBook> InitiateAsync(LmsActor actor, int lmsTaskBookDefinitionId, string userId, DateTime initiatedOnUtc, CancellationToken cancellationToken = default)
		{
			RequireManager(actor, true);
			var definition = await GetDefinitionAsync(actor.DepartmentId, lmsTaskBookDefinitionId);
			if (definition == null || definition.IsDeleted)
				throw new InvalidOperationException("lms_task_book_not_found");
			var trainee = LmsSkillSheetService.Trim(userId, 128) ?? throw new InvalidOperationException("lms_skill_member_invalid");
			var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(actor.DepartmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			if (!active.Contains(trainee))
				throw new InvalidOperationException("lms_skill_member_invalid");
			if ((await GetForMemberAsync(actor.DepartmentId, trainee)).Any(b => b.IsOpen && b.LmsTaskBookDefinitionId == definition.LmsTaskBookDefinitionId))
				throw new InvalidOperationException("lms_task_book_already_open");

			var now = DateTime.UtcNow;
			var validity = definition.ValidityMonths <= 0 ? LmsTaskBookDefinition.DefaultValidityMonths : definition.ValidityMonths;
			var initiated = initiatedOnUtc == default ? now : initiatedOnUtc;
			if (initiated > now.AddDays(1) || initiated < now.AddMonths(-validity))
				throw new InvalidOperationException("lms_task_book_date_invalid");

			var book = new LmsTaskBook
			{
				DepartmentId = actor.DepartmentId,
				LmsTaskBookDefinitionId = definition.LmsTaskBookDefinitionId,
				UserId = trainee,
				PositionCode = definition.PositionCode,
				Name = definition.Name,
				Family = definition.Family,
				TasksJson = definition.TasksJson,
				EvaluatorEligibilityJson = definition.EvaluatorEligibilityJson,
				FinalEvaluatorEligibilityJson = definition.FinalEvaluatorEligibilityJson,
				CertifyingEligibilityJson = definition.CertifyingEligibilityJson,
				CertificationTypeId = definition.CertificationTypeId,
				RecertifyMonths = definition.RecertifyMonths,
				InitiatedOn = initiated,
				InitiatedByUserId = actor.UserId,
				ExpiresOn = initiated.AddMonths(validity),
				Status = (int)LmsTaskBookStatuses.Open,
				UpdatedOn = now
			};
			book.PersonnelCertificationId = await OpenTraineeRecordAsync(actor.DepartmentId, trainee, definition.CertificationTypeId, cancellationToken);
			book = await _books.InsertAsync(book, cancellationToken, true);

			await _notifications.QueueAsync(book.DepartmentId, LmsSkillNotificationKinds.TaskBookInitiated,
				"Task book " + book.PositionCode + " (" + book.Name + ") initiated, valid until " + book.ExpiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ". " + Link(book.LmsTaskBookId),
				new[] { book.UserId }, true, cancellationToken);
			Audit(book.DepartmentId, actor.UserId, AuditLogTypes.LmsTaskBookInitiated, null,
				new { book.LmsTaskBookId, book.LmsTaskBookDefinitionId, book.UserId, book.PositionCode, book.InitiatedOn, book.ExpiresOn, book.PersonnelCertificationId });
			return book;
		}

		/// <summary>
		/// The certification record the book stands for: an existing live record of the type is kept (a re-evaluation); otherwise a Trainee
		/// record is opened ("trainee seats with an open task book"). Non-fatal: the book stands without one.
		/// </summary>
		private async Task<int?> OpenTraineeRecordAsync(int departmentId, string userId, int? certificationTypeId, CancellationToken cancellationToken)
		{
			if (!certificationTypeId.HasValue)
				return null;
			try
			{
				var type = await _certifications.GetCertificationTypeByIdAsync(certificationTypeId.Value);
				if (type == null || type.DepartmentId != departmentId || type.IsDeleted)
					return null;
				var live = (await _certifications.GetCertificationsForDepartmentAsync(departmentId, new[] { userId }) ?? new List<PersonnelCertification>())
					.Where(c => c != null && !c.IsDeleted && c.DepartmentCertificationTypeId == certificationTypeId && LmsSkillRules.SameUser(c.UserId, userId) &&
						(c.Status == (int)PersonnelCertificationStatuses.Active || c.Status == (int)PersonnelCertificationStatuses.Trainee || c.Status == (int)PersonnelCertificationStatuses.PendingVerification))
					.OrderByDescending(c => c.Status == (int)PersonnelCertificationStatuses.Trainee).ThenByDescending(c => c.ExpiresOn ?? DateTime.MaxValue).FirstOrDefault();
				if (live != null)
					return live.PersonnelCertificationId;
				var created = await _certifications.SaveCertificationAsync(new PersonnelCertification
				{
					DepartmentId = departmentId,
					UserId = userId,
					Name = type.Type,
					Type = type.Type,
					DepartmentCertificationTypeId = type.DepartmentCertificationTypeId,
					Status = (int)PersonnelCertificationStatuses.Trainee,
					StatusChangedOn = DateTime.UtcNow,
					StatusReason = "Position task book initiated"
				}, cancellationToken);
				return created?.PersonnelCertificationId;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The trainee certification record of a task book could not be opened.");
				return null;
			}
		}

		public async Task<LmsTaskBook> RequestCertificationAsync(LmsActor actor, int lmsTaskBookId, string finalEvaluatorUserId, string certifyingOfficialUserId, string culture,
			CancellationToken cancellationToken = default)
		{
			// Finishing an open book is allowed while the add-on is lapsed (only new work stops, lapse rule 3).
			RequireManager(actor, false);
			var view = await GetViewAsync(actor.DepartmentId, lmsTaskBookId) ?? throw new InvalidOperationException("lms_task_book_not_found");
			var book = view.Book;
			if (book.Status != (int)LmsTaskBookStatuses.Open)
				throw new InvalidOperationException("lms_task_book_not_open");
			if (book.ExpiresOn <= DateTime.UtcNow)
				throw new InvalidOperationException("lms_task_book_expired");
			if (!view.AllTasksComplete)
				throw new InvalidOperationException("lms_task_book_tasks_incomplete");

			var final = LmsSkillSheetService.Trim(finalEvaluatorUserId, 128) ?? throw new InvalidOperationException("lms_task_book_final_evaluator_required");
			var certifying = LmsSkillSheetService.Trim(certifyingOfficialUserId, 128) ?? throw new InvalidOperationException("lms_task_book_certifying_official_required");
			if (LmsSkillRules.SameUser(final, book.UserId) || LmsSkillRules.SameUser(certifying, book.UserId) || LmsSkillRules.SameUser(final, certifying))
				throw new InvalidOperationException("lms_task_book_signers_invalid");
			var finalRule = SignerEligibility.Parse(book.FinalEvaluatorEligibilityJson) ?? SignerEligibility.Parse(book.EvaluatorEligibilityJson);
			var certifyingRule = SignerEligibility.Parse(book.CertifyingEligibilityJson);
			if (!await QualifiesAsync(book.DepartmentId, final, finalRule))
				throw new InvalidOperationException("lms_task_book_final_evaluator_not_eligible");
			if (!await QualifiesAsync(book.DepartmentId, certifying, certifyingRule))
				throw new InvalidOperationException("lms_task_book_certifying_official_not_eligible");

			var expected = book.RowVersion;
			book.Status = (int)LmsTaskBookStatuses.AwaitingCertification;
			book.FinalEvaluatorUserId = final;
			book.CertifyingOfficialUserId = certifying;
			book.UpdatedOn = DateTime.UtcNow;
			if (!await _books.TryUpdateAsync(book, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");

			SignatureRequestResult created;
			try
			{
				created = await _requests.CreateMultiSignerAsync(new MultiSignerRequestInput
				{
					DepartmentId = book.DepartmentId,
					SubjectKind = SignatureSubjectKinds.SkillEvaluation,
					SubjectId = LmsSkillRules.TaskBookSubjectId(book.LmsTaskBookId),
					Ordered = true,
					Steps = new List<SignatureStepInput>
					{
						new SignatureStepInput { Kind = SignatureSignerKinds.Member, Role = SignatureSignerRoles.Evaluator, Order = 1, UserId = final, Eligibility = finalRule, Level = SignatureLevels.StepUp, StepLabel = "Final evaluator recommendation" },
						new SignatureStepInput { Kind = SignatureSignerKinds.Member, Role = SignatureSignerRoles.CertifyingOfficial, Order = 2, UserId = certifying, Eligibility = certifyingRule, Level = SignatureLevels.StepUp, StepLabel = "Certifying official" }
					},
					ExpiresInDays = 30,
					Culture = culture,
					RequestedByUserId = actor.UserId
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The task book certification request could not be created.");
				created = SignatureRequestResult.Refused(SignatureRequestRefusals.InvalidRequest);
			}

			if (created == null || !created.Succeeded || created.Request == null)
			{
				var reopen = book.RowVersion;
				book.Status = (int)LmsTaskBookStatuses.Open;
				book.FinalEvaluatorUserId = null;
				book.CertifyingOfficialUserId = null;
				book.UpdatedOn = DateTime.UtcNow;
				await _books.TryUpdateAsync(book, reopen, CancellationToken.None);
				throw new InvalidOperationException("lms_skill_signature_refused:" + (created?.Refusal ?? SignatureRequestRefusals.InvalidRequest));
			}

			var stored = book.RowVersion;
			book.SignatureRequestId = created.Request.SignatureRequestId;
			await _books.TryUpdateAsync(book, stored, CancellationToken.None);

			await _notifications.QueueAsync(book.DepartmentId, LmsSkillNotificationKinds.TaskBookCertificationRequested,
				"Task book " + book.PositionCode + " is complete and waiting for the final evaluator and the certifying official. " + Link(book.LmsTaskBookId), new[] { book.UserId }, true, cancellationToken);
			Audit(book.DepartmentId, actor.UserId, AuditLogTypes.LmsTaskBookCertificationRequested, null, new { book.LmsTaskBookId, book.SignatureRequestId, final, certifying });
			return book;
		}

		/// <summary>A named signer qualifies by the rule; without a rule only an active department admin does.</summary>
		private async Task<bool> QualifiesAsync(int departmentId, string userId, SignerEligibility rule)
		{
			if (rule != null)
				return await _eligibility.IsEligibleAsync(departmentId, userId, rule);
			var admins = await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>();
			return admins.Any(a => LmsSkillRules.SameUser(a?.UserId, userId));
		}

		public async Task<LmsTaskBook> CancelAsync(LmsActor actor, int lmsTaskBookId, string reason, CancellationToken cancellationToken = default)
		{
			RequireManager(actor, false);
			var book = await LoadAsync(actor.DepartmentId, lmsTaskBookId) ?? throw new InvalidOperationException("lms_task_book_not_found");
			if (!book.IsOpen)
				throw new InvalidOperationException("lms_task_book_not_open");
			var why = LmsSkillSheetService.Trim(reason, 1000) ?? throw new InvalidOperationException("lms_skill_void_reason_required");
			var expected = book.RowVersion;
			var wasAwaiting = book.Status == (int)LmsTaskBookStatuses.AwaitingCertification;
			book.Status = (int)LmsTaskBookStatuses.Cancelled;
			book.ClosedOn = DateTime.UtcNow;
			book.CloseReason = why;
			book.UpdatedOn = book.ClosedOn;
			if (!await _books.TryUpdateAsync(book, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");
			if (wasAwaiting && book.SignatureRequestId.HasValue)
			{
				try { await _requests.VoidAsync(book.DepartmentId, book.SignatureRequestId.Value, actor.UserId, why, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, "The task book certification request could not be withdrawn."); }
			}
			Audit(book.DepartmentId, actor.UserId, AuditLogTypes.LmsTaskBookClosed, null, new { book.LmsTaskBookId, status = book.Status, why });
			return book;
		}

		public async Task<bool> CertifyFromSignatureAsync(SignatureRequestRecord request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.Status != (int)SignatureRequestStatuses.Completed || request.SubjectKind != (int)SignatureSubjectKinds.SkillEvaluation)
				return false;
			var (kind, id) = LmsSkillRules.ParseSubject(request.SubjectId);
			if (kind != "task-book")
				return false;
			var book = await LoadAsync(request.DepartmentId, id);
			// Idempotent: OnSignedAsync runs once per signed step; the second call finds the book certified.
			if (book == null || book.SignatureRequestId != request.SignatureRequestId || book.Status != (int)LmsTaskBookStatuses.AwaitingCertification)
				return false;

			var signers = await _requests.GetSignersAsync(book.DepartmentId, request.SignatureRequestId) ?? new List<SignatureRequestSigner>();
			var evaluator = signers.FirstOrDefault(s => s.Role == (int)SignatureSignerRoles.Evaluator && s.Status == (int)SignatureSignerStatuses.Signed);
			var official = signers.FirstOrDefault(s => s.Role == (int)SignatureSignerRoles.CertifyingOfficial && s.Status == (int)SignatureSignerStatuses.Signed);
			var now = DateTime.UtcNow;
			var expected = book.RowVersion;
			book.Status = (int)LmsTaskBookStatuses.Certified;
			book.RecommendedOn = evaluator?.SignedOn ?? now;
			book.CertifiedOn = official?.SignedOn ?? request.CompletedOn ?? now;
			book.CertifiedByUserId = official?.UserId ?? book.CertifyingOfficialUserId;
			book.ClosedOn = book.CertifiedOn;
			book.UpdatedOn = now;
			if (!await _books.TryUpdateAsync(book, expected, cancellationToken))
				return false;

			await ActivateCertificationAsync(book, cancellationToken);

			try
			{
				await _writer.WriteSourceAsync(book.DepartmentId, LearningRecordSourceKinds.TaskBookSignoff, LmsSkillRules.TaskBookSourceRef(book.LmsTaskBookId),
					new[]
					{
						new LearningRecordDraft
						{
							UserId = book.UserId,
							ComponentKey = string.Empty,
							SessionKey = LmsSkillRules.TaskBookSourceRef(book.LmsTaskBookId),
							Title = LmsSkillSheetService.Trim(book.PositionCode + " position task book certified (" + book.Name + ")", 250),
							CourseCode = book.PositionCode,
							CompletedOn = book.CertifiedOn.Value,
							StartedOn = book.InitiatedOn,
							EndedOn = book.CertifiedOn,
							Hours = 0m,
							MethodOfTraining = (int)MethodsOfTraining.DrillField,
							InstructorUserIds = book.FinalEvaluatorUserId,
							Tags = "task-book",
							Status = LearningRecordStatuses.Verified,
							VerifiedByUserId = book.CertifiedByUserId
						}
					}, book.CertifiedByUserId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The learning record of task book {book.LmsTaskBookId} could not be written.");
			}

			await _notifications.QueueAsync(book.DepartmentId, LmsSkillNotificationKinds.TaskBookCertified,
				"Task book " + book.PositionCode + " (" + book.Name + ") certified. " + Link(book.LmsTaskBookId), new[] { book.UserId }, true, cancellationToken);
			Audit(book.DepartmentId, book.CertifiedByUserId, AuditLogTypes.LmsTaskBookCertified, null,
				new { book.LmsTaskBookId, book.UserId, book.PositionCode, book.CertifiedOn, book.PersonnelCertificationId, book.SignatureRequestId });
			return true;
		}

		/// <summary>The record moves from Trainee to Active (by the certifying official, never the holder) and gets the re-evaluation expiry.</summary>
		private async Task ActivateCertificationAsync(LmsTaskBook book, CancellationToken cancellationToken)
		{
			if (!book.CertificationTypeId.HasValue)
				return;
			try
			{
				var type = await _certifications.GetCertificationTypeByIdAsync(book.CertificationTypeId.Value);
				var certifiedOn = (book.CertifiedOn ?? DateTime.UtcNow).Date;
				var months = book.RecertifyMonths > 0 ? book.RecertifyMonths : type?.DefaultValidityMonths;
				DateTime? expires = type?.NeverExpires == true || !months.HasValue || months.Value <= 0 ? (DateTime?)null : certifiedOn.AddMonths(months.Value);
				var record = !book.PersonnelCertificationId.HasValue ? null :
					(await _certifications.GetCertificationsForDepartmentAsync(book.DepartmentId, new[] { book.UserId }) ?? new List<PersonnelCertification>())
						.FirstOrDefault(c => c.PersonnelCertificationId == book.PersonnelCertificationId.Value && !c.IsDeleted);

				if (record == null)
				{
					if (type == null || type.DepartmentId != book.DepartmentId || type.IsDeleted)
						return;
					var created = await _certifications.SaveCertificationAsync(new PersonnelCertification
					{
						DepartmentId = book.DepartmentId,
						UserId = book.UserId,
						Name = type.Type,
						Type = type.Type,
						DepartmentCertificationTypeId = type.DepartmentCertificationTypeId,
						RecievedOn = certifiedOn,
						ExpiresOn = expires,
						Status = (int)PersonnelCertificationStatuses.Active,
						StatusChangedOn = DateTime.UtcNow,
						StatusChangedByUserId = book.CertifiedByUserId,
						VerifiedOn = DateTime.UtcNow,
						VerifiedByUserId = book.CertifiedByUserId
					}, cancellationToken);
					if (created != null)
					{
						var expected = book.RowVersion;
						book.PersonnelCertificationId = created.PersonnelCertificationId;
						await _books.TryUpdateAsync(book, expected, cancellationToken);
					}
					return;
				}

				if (record.Status != (int)PersonnelCertificationStatuses.Active)
					record = await _certifications.SetCertificationStatusAsync(record.PersonnelCertificationId, book.DepartmentId, PersonnelCertificationStatuses.Active,
						"Position task book certified", book.CertifiedByUserId, cancellationToken) ?? record;
				if (expires.HasValue && (!record.ExpiresOn.HasValue || record.ExpiresOn.Value.Date < expires.Value.Date))
					await _certifications.RenewCertificationAsync(record.PersonnelCertificationId, book.DepartmentId, expires, null, book.CertifiedByUserId, cancellationToken);
			}
			catch (Exception ex)
			{
				// The certification stands on the book; the record can be activated by hand (the notice and audit say it was certified).
				Logging.LogException(ex, $"The certification record of task book {book.LmsTaskBookId} could not be activated.");
			}
		}

		public async Task<SignatureSubjectInfo> DescribeForSignatureAsync(int departmentId, int lmsTaskBookId)
		{
			var view = await GetViewAsync(departmentId, lmsTaskBookId);
			var book = view?.Book;
			if (book == null || (book.Status != (int)LmsTaskBookStatuses.AwaitingCertification && book.Status != (int)LmsTaskBookStatuses.Certified))
				return null;

			var stable = new
			{
				kind = "task-book/1",
				id = book.LmsTaskBookId,
				user = book.UserId.Trim().ToLowerInvariant(),
				position = book.PositionCode,
				name = book.Name,
				initiated = book.InitiatedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				expires = book.ExpiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				final = (book.FinalEvaluatorUserId ?? string.Empty).Trim().ToLowerInvariant(),
				certifying = (book.CertifyingOfficialUserId ?? string.Empty).Trim().ToLowerInvariant(),
				tasks = view.Tasks.Select(t => new
				{
					key = t.Task.Key,
					number = t.Task.Number,
					title = t.Task.Title,
					required = t.Task.RequiredEvaluations,
					incident = t.Task.RequiresIncident,
					evaluations = t.Evaluations.Select(e => new { id = e.LmsSkillEvaluationId, hash = e.ContentHash, on = (e.SubmittedOn ?? e.StartedOn).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.CallId, e.DeploymentId }).ToList()
				}).ToList()
			};

			var names = LmsSkillEvaluationService.Names(await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId));
			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var html = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px}" +
				"th,td{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}table{border-collapse:collapse;width:100%;margin-bottom:10px}</style></head><body>");
			html.Append("<h1>").Append(E(book.PositionCode)).Append(" - ").Append(E(book.Name)).Append("</h1><table>");
			void Row(string label, string value) => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			Row("Trainee", LmsSkillEvaluationService.Name(names, book.UserId));
			Row("Initiated", stable.initiated);
			Row("Valid until", stable.expires);
			Row("Final evaluator", LmsSkillEvaluationService.Name(names, book.FinalEvaluatorUserId));
			Row("Certifying official", LmsSkillEvaluationService.Name(names, book.CertifyingOfficialUserId));
			html.Append("</table><table><tr><th>Task</th><th>Evaluations</th></tr>");
			foreach (var task in view.Tasks)
			{
				html.Append("<tr><td>").Append(E((task.Task.Number ?? task.Task.Key) + " " + task.Task.Title)).Append("</td><td>");
				html.Append(E(string.Join("; ", task.Evaluations.Select(e => (e.SubmittedOn ?? e.StartedOn).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " +
					LmsSkillEvaluationService.Name(names, e.EvaluatorUserId) + (e.CallId.HasValue ? " (call " + e.CallId.Value.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty) +
					(e.DeploymentId.HasValue ? " (deployment " + e.DeploymentId.Value.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty) + " " + (string.IsNullOrEmpty(e.ContentHash) ? string.Empty : SignaturePolicy.ShortCode(e.ContentHash))))));
				html.Append("</td></tr>");
			}
			html.Append("</table></body></html>");

			return new SignatureSubjectInfo
			{
				Kind = SignatureSubjectKinds.SkillEvaluation,
				SubjectId = LmsSkillRules.TaskBookSubjectId(book.LmsTaskBookId),
				Title = LmsSkillSheetService.Trim(book.PositionCode + " task book - " + LmsSkillEvaluationService.Name(names, book.UserId), 250),
				RevisionId = "task-book-" + book.LmsTaskBookId.ToString(CultureInfo.InvariantCulture),
				RevisionSha256 = LmsSkillSheetEngine.Sha256(JsonConvert.SerializeObject(stable, Formatting.None)),
				DocumentHtml = html.ToString()
			};
		}

		public async Task<int> ExpireDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var expired = 0;
			foreach (var book in await _books.GetExpiredOpenAsync(utcNow, 200) ?? new List<LmsTaskBook>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (book.Status != (int)LmsTaskBookStatuses.Open || book.ExpiresOn > utcNow)
						continue;
					var expected = book.RowVersion;
					book.Status = (int)LmsTaskBookStatuses.Expired;
					book.ClosedOn = utcNow;
					book.CloseReason = "validity_expired";
					book.UpdatedOn = utcNow;
					if (!await _books.TryUpdateAsync(book, expected, cancellationToken))
						continue;
					expired++;
					await _notifications.QueueAsync(book.DepartmentId, LmsSkillNotificationKinds.TaskBookExpired,
						"Task book " + book.PositionCode + " (" + book.Name + ") expired before it was certified. " + Link(book.LmsTaskBookId), new[] { book.UserId }, true, cancellationToken);
					Audit(book.DepartmentId, null, AuditLogTypes.LmsTaskBookClosed, null, new { book.LmsTaskBookId, status = book.Status, book.CloseReason });
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Task book {book.LmsTaskBookId} could not be expired.");
				}
			}
			return expired;
		}

		public async Task OnTaskEvaluatedAsync(LmsSkillEvaluation evaluation, CancellationToken cancellationToken = default)
		{
			if (evaluation?.LmsTaskBookId == null || evaluation.Result != (int)LmsSkillEvaluationResults.Passed)
				return;
			var view = await GetViewAsync(evaluation.DepartmentId, evaluation.LmsTaskBookId.Value);
			if (view?.Book == null || view.Book.Status != (int)LmsTaskBookStatuses.Open)
				return;
			var task = view.Tasks.FirstOrDefault(t => string.Equals(t.Task.Key, evaluation.TaskKey, StringComparison.OrdinalIgnoreCase));
			var text = "Task book " + view.Book.PositionCode + ": task " + (task?.Task.Number ?? evaluation.TaskKey) + " signed (" +
				(task?.Counted ?? 0).ToString(CultureInfo.InvariantCulture) + " of " + Math.Max(1, task?.Task.RequiredEvaluations ?? 1).ToString(CultureInfo.InvariantCulture) + "); " +
				view.CompleteCount.ToString(CultureInfo.InvariantCulture) + " of " + view.Tasks.Count.ToString(CultureInfo.InvariantCulture) + " tasks complete" +
				(view.AllTasksComplete ? ", ready for certification" : string.Empty) + ". " + Link(view.Book.LmsTaskBookId);
			await _notifications.QueueAsync(view.Book.DepartmentId, LmsSkillNotificationKinds.TaskSigned, text, new[] { view.Book.UserId }, view.AllTasksComplete, cancellationToken);
		}

		#endregion Books

		public static string Link(int lmsTaskBookId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/LmsSkills/TaskBook/" + lmsTaskBookId.ToString(CultureInfo.InvariantCulture);

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
	}
}
