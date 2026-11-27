using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Training;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Baseline training (protocols-training-knowledge-plan §13 T0, §18.3). Every action is gated twice: a policy admits the
	/// member to the kind of page (Training_View for members, Training_Create to author, Training_Update to assign and
	/// report, Training_Delete to archive and delete) and TrainingManagementScope decides for the one training (a group admin
	/// only their own group's, under Everyone a member only their own). The Trainings module switch is enforced first.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class TrainingsController : SecureBaseController
	{
		private const long MaxAttachmentBytes = 30000000;

		private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "ppt", "pptx", "pps", "ppsx", "odt", "xls", "xlsx", "txt", "mpg", "avi", "mpeg", "mp4"
		};

		private static readonly Regex QuestionKey = new Regex(@"^question_([1-9][0-9]{0,3})$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ITrainingService _trainingService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ITrainingAssignmentService _assignmentService;
		private readonly IMyTrainingService _myTrainingService;
		private readonly IMemberLeaveService _leaveService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICertificationService _certificationService;
		private readonly IUnitsService _unitsService;
		private readonly IShiftsService _shiftsService;
		private readonly ILearningRecordService _learningRecordService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _localizer;

		private TrainingManagementScope _scope;
		private Department _department;
		private bool _departmentRead;

		public TrainingsController(IDepartmentGroupsService departmentGroupsService, IDepartmentsService departmentService, ITrainingService trainingService,
			IPersonnelRolesService personnelRolesService, IAuthorizationService authorizationService, ITrainingAssignmentService assignmentService,
			IMyTrainingService myTrainingService, IMemberLeaveService leaveService, IDepartmentSettingsService departmentSettingsService,
			ICertificationService certificationService, IUnitsService unitsService, IShiftsService shiftsService,
			ILearningRecordService learningRecordService, IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> localizer,
			ITrainingCompletionRecordingService recordings, IContentSetCopyLocks setLocks)
		{
			_departmentGroupsService = departmentGroupsService;
			_departmentsService = departmentService;
			_trainingService = trainingService;
			_personnelRolesService = personnelRolesService;
			_authorizationService = authorizationService;
			_assignmentService = assignmentService;
			_myTrainingService = myTrainingService;
			_leaveService = leaveService;
			_departmentSettingsService = departmentSettingsService;
			_certificationService = certificationService;
			_unitsService = unitsService;
			_shiftsService = shiftsService;
			_learningRecordService = learningRecordService;
			_localizer = localizer;
			_recordings = recordings;
			_setLocks = setLocks;
		}

		private readonly ITrainingCompletionRecordingService _recordings;
		private readonly IContentSetCopyLocks _setLocks;

		/// <summary>
		/// A training that is a managed copy of a linked department's course set item (round 10 A3) changes only through the set: its content and
		/// quiz cannot be edited here, but it can still be assigned, reported on, copied into an unmanaged training, archived or deleted.
		/// </summary>
		private Task<bool> ManagedBySetAsync(int trainingId) => _setLocks.IsManagedCopyAsync(DepartmentId, ContentSetItemKinds.Training, trainingId);

		#region Gates

		/// <summary>The Trainings module switch (DepartmentModuleSettings.TrainingDisabled) closes every training page.</summary>
		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (await IsModuleDisabledAsync())
			{
				context.Result = NotFound();
				return;
			}

			// The section strip on every training page follows the member's rights.
			ViewData["Scope"] = await ScopeAsync();
			await next();
		}

		/// <summary>True when the department switched the Trainings module off.</summary>
		[NonAction]
		public async Task<bool> IsModuleDisabledAsync()
		{
			try
			{
				var modules = await _departmentSettingsService.GetDepartmentModuleSettingsAsync(DepartmentId);
				return modules != null && modules.TrainingDisabled;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		private async Task<TrainingManagementScope> ScopeAsync()
		{
			return _scope ??= await _authorizationService.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
		}

		private async Task<Department> DepartmentAsync()
		{
			if (!_departmentRead)
			{
				_department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
				_departmentRead = true;
			}

			return _department;
		}

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			return (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		}

		private string L(string key)
		{
			var value = _localizer?[key]?.Value;
			return string.IsNullOrEmpty(value) ? key : value;
		}

		private void Flash(string message, string error = null)
		{
			if (TempData == null)
				return;
			if (!string.IsNullOrWhiteSpace(message))
				TempData["TrainingMessage"] = message;
			if (!string.IsNullOrWhiteSpace(error))
				TempData["TrainingError"] = error;
		}

		private (string Message, string Error) Flashed()
		{
			return (TempData?["TrainingMessage"] as string, TempData?["TrainingError"] as string);
		}

		#endregion Gates

		#region My Training and the officer list

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Index()
		{
			var (message, error) = Flashed();
			var model = new TrainingIndexModel
			{
				Scope = await ScopeAsync(),
				Department = await DepartmentAsync(),
				MyTraining = await _myTrainingService.GetOverviewAsync(DepartmentId, UserId, DateTime.UtcNow) ?? new MyTrainingOverview(),
				Message = message,
				Error = error
			};

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Manage(string status = "active", string search = null)
		{
			var scope = await ScopeAsync();
			if (!scope.IsManager)
				return Unauthorized();

			var now = DateTime.UtcNow;
			var managed = (await _trainingService.GetAllTrainingsForDepartmentAsync(DepartmentId) ?? new List<Training>())
				.Where(t => t.DepartmentId == DepartmentId && scope.CanManage(t)).ToList();

			status = (status ?? "active").ToLowerInvariant();
			IEnumerable<Training> shown = status switch
			{
				"archived" => managed.Where(t => t.IsArchived),
				"scheduled" => managed.Where(t => !t.IsArchived && !t.HasStarted(now)),
				"all" => managed,
				_ => managed.Where(t => !t.IsArchived && t.HasStarted(now))
			};

			if (!string.IsNullOrWhiteSpace(search))
				shown = shown.Where(t => (t.Name ?? string.Empty).IndexOf(search.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);

			var (message, error) = Flashed();
			var model = new TrainingManageModel
			{
				Scope = scope,
				Status = status,
				Search = search,
				Department = await DepartmentAsync(),
				Summary = await _trainingService.GetManagerSummaryAsync(DepartmentId, managed, now) ?? new TrainingManagerSummary(),
				Message = message,
				Error = error,
				Rows = shown.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).Select(t => new TrainingManageRow
				{
					Training = t,
					Assigned = t.Users?.Count(u => u != null && !u.RemovedOn.HasValue) ?? 0,
					Complete = t.Users?.Count(u => u != null && u.Complete) ?? 0,
					CanAuthor = scope.CanAuthor(t),
					CanAssign = scope.CanAssign(t),
					CanDelete = scope.CanDelete(t),
					IsScheduled = !t.HasStarted(now)
				}).ToList()
			};

			return View(model);
		}

		#endregion My Training and the officer list

		#region New and Edit

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_Create)]
		public async Task<IActionResult> New()
		{
			var scope = await ScopeAsync();
			if (!scope.CanCreate)
				return Unauthorized();

			var model = new NewTrainingModel { SendOverdueNotices = true, CanAuthor = true, CanAssign = scope.AssignRight.HoldsAny };

			// K1: "New training" from a knowledge gap starts from the question (carried in TempData, never the URL); the author writes it.
			if (TempData?[KnowledgeController.TrainingPrefillKey] is string question && !string.IsNullOrWhiteSpace(question))
			{
				var line = question.Trim();
				model.Name = line.Length > 200 ? line.Substring(0, 200) : line;
				model.Description = "<p>" + System.Net.WebUtility.HtmlEncode(line) + "</p>";
			}

			await FillOptionsAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Create)]
		public async Task<IActionResult> New(NewTrainingModel model, IFormCollection form, ICollection<IFormFile> attachments)
		{
			var scope = await ScopeAsync();
			if (!scope.CanCreate)
				return Unauthorized();

			model ??= new NewTrainingModel();
			model.TrainingId = 0;
			model.CanAuthor = true;
			model.CanAssign = scope.AssignRight.HoldsAny;

			var now = DateTime.UtcNow;
			var department = await DepartmentAsync();
			model.Questions = ParseQuestions(form);

			// A new training is always a new row: nothing posted can reach its id, creator, stamps or children (T-D23).
			var training = new Training { DepartmentId = DepartmentId, CreatedByUserId = UserId, CreatedOn = now };
			ApplyContent(training, model, department);
			await ValidateCategoryAsync(training, null);
			training.Questions = ToEntities(model.Questions, 0);
			ValidateQuiz(training);
			await AddAttachmentsAsync(training, attachments);

			var rules = model.CanAssign ? BuildRules(model) : new List<ContentAudienceRule>();
			if (rules.Count == 0 && !training.OpenToAllMembers)
				ModelState.AddModelError(nameof(model.UserIds), L("NoAssigneesError"));

			if (!ModelState.IsValid)
			{
				await FillOptionsAsync(model);
				return View(model);
			}

			var saved = await _trainingService.SaveAsync(training);
			var outcome = rules.Count > 0
				? await _assignmentService.SaveAudienceAsync(saved, rules, UserId, scope.AssignRight, now)
				: new TrainingAssignmentOutcome();

			await NotifyAddedAsync(saved, outcome?.Added, now);
			Flash(L("TrainingSavedMessage") + " " + OutcomeMessage(outcome), await SkippedMessageAsync(outcome));

			return RedirectToAction("Manage");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Edit(int trainingId)
		{
			var training = await _trainingService.GetTrainingByIdAsync(trainingId);

			if (training == null)
				return NotFound();

			if (training.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await ScopeAsync();
			var canAuthor = scope.CanAuthor(training);
			var canAssign = scope.CanAssign(training);

			// The edit page shows the answer key: never to a member who merely may create trainings of their own.
			if (!canAuthor && !canAssign)
				return Unauthorized();
			var managedBySet = canAuthor && await ManagedBySetAsync(trainingId);
			if (managedBySet)
				canAuthor = false;

			var (message, error) = Flashed();
			if (managedBySet)
				message = string.IsNullOrWhiteSpace(message) ? L("ManagedBySetMessage") : message + " " + L("ManagedBySetMessage");
			var model = ToEditor(training, await DepartmentAsync(), canAuthor, canAssign);
			model.Message = message;
			model.Error = error;
			model.Flags = canAuthor ? await _trainingService.GetQuestionFlagsAsync(DepartmentId, trainingId, false) ?? new List<TrainingQuestionFlag>() : new List<TrainingQuestionFlag>();
			await FillOptionsAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Edit(int trainingId, EditTrainingModel model, IFormCollection form, ICollection<IFormFile> attachments)
		{
			var existing = await _trainingService.GetTrainingByIdAsync(trainingId);

			if (existing == null)
				return NotFound();

			if (existing.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await ScopeAsync();
			var canAuthor = scope.CanAuthor(existing);
			var canAssign = scope.CanAssign(existing);

			if (!canAuthor && !canAssign)
				return Unauthorized();
			if (canAuthor && await ManagedBySetAsync(trainingId))
			{
				// Only the audience of a managed copy is the department's to change; posted content and quiz are ignored.
				canAuthor = false;
				if (!canAssign)
				{
					Flash(null, L("ManagedBySetMessage"));
					return RedirectToAction("Edit", new { trainingId });
				}
			}

			model ??= new EditTrainingModel();
			model.TrainingId = trainingId;
			model.CanAuthor = canAuthor;
			model.CanAssign = canAssign;
			model.IsDynamic = existing.Audiences?.Count > 0;
			model.AssignedCount = existing.Users?.Count(u => u != null && !u.RemovedOn.HasValue) ?? 0;
			model.CompletedCount = existing.Users?.Count(u => u != null && u.Complete && !u.RemovedOn.HasValue) ?? 0;
			model.Attachments = existing.Attachments?.ToList() ?? new List<TrainingAttachment>();

			var now = DateTime.UtcNow;
			var department = await DepartmentAsync();
			var rules = canAssign ? BuildRules(model) : new List<ContentAudienceRule>();
			if (canAuthor)
				model.Questions = ParseQuestions(form);

			// Preview: who the audience would add and who would be skipped, without saving anything.
			if (string.Equals(model.Command, "Preview", StringComparison.OrdinalIgnoreCase) && canAssign)
			{
				model.Preview = await PreviewAsync(await _assignmentService.PreviewAudienceAsync(existing, rules));
				await FillOptionsAsync(model);
				return View(model);
			}

			var quizChanged = false;
			var dueRuleChanged = false;
			if (canAuthor)
			{
				// Compared before the edit is applied: any change to a question, an option, the key or the pass mark (R-T6).
				var storedHash = TrainingQuizScorer.RevisionHash(existing);
				var previousFixed = existing.ToBeCompletedBy;
				var previousDays = existing.DueDays;
				var previousCategory = existing.CategoryId;

				ApplyContent(existing, model, department);
				await ValidateCategoryAsync(existing, previousCategory);
				existing.Questions = ToEntities(model.Questions, trainingId);
				existing.UpdatedByUserId = UserId;
				ValidateQuiz(existing);
				await AddAttachmentsAsync(existing, attachments);

				quizChanged = !string.Equals(storedHash, TrainingQuizScorer.RevisionHash(existing), StringComparison.Ordinal);
				dueRuleChanged = previousFixed != existing.ToBeCompletedBy || previousDays != existing.DueDays;

				var choice = (model.MaterialChange ?? string.Empty).Trim().ToLowerInvariant();
				if (quizChanged && model.CompletedCount > 0 && choice != "material" && choice != "keep")
				{
					model.AskMaterialChange = true;
					ModelState.AddModelError(nameof(model.MaterialChange), L("MaterialChangeRequired"));
				}
			}

			if (!ModelState.IsValid)
			{
				await FillOptionsAsync(model);
				return View(model);
			}

			var messages = new List<string> { L("TrainingSavedMessage") };
			var saved = existing;
			if (canAuthor)
			{
				saved = await _trainingService.SaveAsync(existing);

				if (dueRuleChanged)
					await _assignmentService.RescheduleOpenAssignmentsAsync(saved, now);

				if (quizChanged && string.Equals(model.MaterialChange, "material", StringComparison.OrdinalIgnoreCase))
				{
					var reassigned = await _assignmentService.ReassignCompletedAsync(saved, UserId, now);
					messages.Add(string.Format(L("MaterialReassignedMessage"), reassigned));
				}
			}

			TrainingAssignmentOutcome outcome = null;
			if (canAssign)
			{
				// A dynamic training (or one made dynamic now) stores the picks as rules; a static list only adds the picks.
				if (model.IsDynamic || model.MakeDynamic)
					outcome = await _assignmentService.SaveAudienceAsync(saved, rules, UserId, scope.AssignRight, now);
				else if (rules.Count > 0)
					outcome = await _assignmentService.AssignAsync(saved, model.UserIds, rules.Where(r => r.Kind != (int)ContentAudienceKinds.User).ToList(),
						TrainingAssignmentSources.Manual, scope.AssignRight, now);
			}

			if (outcome != null)
			{
				await NotifyAddedAsync(saved, outcome.Added, now);
				messages.Add(OutcomeMessage(outcome));
			}

			Flash(string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m))), outcome == null ? null : await SkippedMessageAsync(outcome));

			return RedirectToAction("Manage");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Create)]
		public async Task<IActionResult> RemoveAttachment(int trainingId, int trainingAttachmentId)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();
			if (training.DepartmentId != DepartmentId || !(await ScopeAsync()).CanAuthor(training))
				return Unauthorized();
			if (await ManagedBySetAsync(trainingId))
			{
				Flash(null, L("ManagedBySetMessage"));
				return RedirectToAction("Edit", new { trainingId });
			}

			var removed = await _trainingService.RemoveAttachmentAsync(DepartmentId, trainingId, trainingAttachmentId);
			Flash(removed ? L("AttachmentRemovedMessage") : null, removed ? null : L("AttachmentNotRemovedError"));
			return RedirectToAction("Edit", new { trainingId });
		}

		#endregion New and Edit

		#region Taking a training

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> View(int trainingId)
		{
			var training = await _trainingService.GetTrainingByIdAsync(trainingId);

			if (training == null)
				return NotFound();

			if (training.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await ScopeAsync();
			var now = DateTime.UtcNow;

			// Its assignees, whoever manages it, and every member when it is open to all; scheduled and archived trainings
			// stay with the managers.
			if (!scope.CanOpen(training, now))
				return Unauthorized();

			var department = await DepartmentAsync();
			var assignment = scope.AssignmentOf(training);
			if (assignment != null && !assignment.RemovedOn.HasValue && training.HasStarted(now) && !training.IsArchived)
				assignment = await _trainingService.SetTrainingAsViewedAsync(trainingId, UserId) ?? assignment;

			var (message, error) = Flashed();
			var model = new ViewTrainingModel
			{
				Training = training,
				Assignment = assignment,
				Department = department,
				CanAuthor = scope.CanAuthor(training),
				CanAssign = scope.CanAssign(training),
				CanDelete = scope.CanDelete(training),
				HasQuiz = training.Questions?.Count > 0,
				Message = message,
				Error = error
			};

			var names = await NamesAsync();
			model.CreatorUserName = training.CreatedByUserId != null && names.TryGetValue(training.CreatedByUserId, out var creator) ? creator : null;

			if (assignment != null && !assignment.RemovedOn.HasValue)
			{
				var leaves = await _leaveService.GetForMemberAsync(DepartmentId, UserId);
				model.OnLeave = TrainingDueDates.IsOnLeave(now, leaves);
				model.DueUtc = TrainingDueDates.EffectiveDueUtc(training, assignment, department, leaves);
				model.State = TrainingDueDates.StateOf(training, assignment, model.DueUtc, model.OnLeave, now);
				model.AttemptsLeft = AttemptsLeft(training, assignment);
				model.CanTakeQuiz = model.HasQuiz && !assignment.Complete && !assignment.IsExemptOn(now) && (model.AttemptsLeft == null || model.AttemptsLeft > 0);
				model.LastAttempt = (await _trainingService.GetAttemptsForMemberAsync(DepartmentId, trainingId, UserId) ?? new List<TrainingQuizAttempt>())
					.OrderByDescending(a => a.AttemptNumber).FirstOrDefault();
			}
			else
			{
				model.CanEnroll = assignment == null && training.OpenToAllMembers && training.AllowSelfEnroll && !training.ClosedToNewAssignees;
			}

			return View(model);
		}

		private static int? AttemptsLeft(Training training, TrainingUser assignment)
		{
			var limit = training?.QuizAttemptLimit.GetValueOrDefault() ?? 0;
			return limit > 0 ? Math.Max(0, limit - (assignment?.AttemptCount ?? 0)) : (int?)null;
		}

		/// <summary>The one authorized download path for training attachments (T-D22): bytes only after the open check.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> GetTrainingAttachment(int trainingAttachmentId)
		{
			var attachment = await _trainingService.GetTrainingAttachmentByIdAsync(trainingAttachmentId);
			if (attachment == null)
				return NotFound();

			var training = await _trainingService.GetTrainingRowByIdAsync(attachment.TrainingId);
			if (training == null || training.DepartmentId != DepartmentId)
				return NotFound();

			if (!(await ScopeAsync()).CanOpen(training, DateTime.UtcNow))
				return Unauthorized();

			var file = await _trainingService.GetTrainingAttachmentWithDataAsync(trainingAttachmentId);
			if (file?.Data == null || file.TrainingId != training.TrainingId)
				return NotFound();

			return File(file.Data, string.IsNullOrWhiteSpace(file.FileType) ? "application/octet-stream" : file.FileType, file.FileName);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Quiz(int trainingId, CancellationToken cancellationToken)
		{
			var training = await _trainingService.GetTrainingByIdAsync(trainingId);

			if (training == null)
				return NotFound();

			if (training.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await ScopeAsync();
			var now = DateTime.UtcNow;
			if (!scope.CanOpenQuiz(training, now))
				return Unauthorized();

			if (training.Questions == null || training.Questions.Count == 0)
				return RedirectToAction("View", new { trainingId });

			var assignment = scope.IsAssigned(training) ? scope.AssignmentOf(training) : null;
			var attemptsLeft = AttemptsLeft(training, assignment);

			if (assignment != null)
			{
				if (assignment.Complete)
				{
					Flash(L("QuizAlreadyPassedMessage"));
					return RedirectToAction("View", new { trainingId });
				}

				if (attemptsLeft == 0)
				{
					Flash(null, L("QuizAttemptLimitError"));
					return RedirectToAction("View", new { trainingId });
				}

				await _trainingService.SetTrainingAsViewedAsync(trainingId, UserId, cancellationToken);
			}

			// Questions and options only (R-T11): the answer key stays on the server.
			var model = TrainingQuizView.From(training, attemptsLeft);
			model.IsPreview = assignment == null;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SubmitQuiz(int trainingId, IFormCollection form, CancellationToken cancellationToken)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);

			if (training == null)
				return NotFound();

			if (training.DepartmentId != DepartmentId)
				return Unauthorized();

			// A quiz is answered by the people it was assigned to.
			if (!(await ScopeAsync()).IsAssigned(training))
				return Unauthorized();

			// Canonical question keys only, each question once, option ids validated against the question by the service
			// (T-D4, T-D11); a malformed post is an empty answer, never an error page.
			var posted = (form ?? new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()))
				.Select(f => new KeyValuePair<string, IEnumerable<string>>(f.Key, f.Value.ToArray()));
			var submission = await _trainingService.RecordQuizAttemptAsync(DepartmentId, trainingId, UserId, TrainingQuizScorer.ParsePostedAnswers(posted), cancellationToken);

			switch (submission?.Status)
			{
				case TrainingQuizSubmissionStatuses.Recorded:
					var full = await _trainingService.GetTrainingByIdAsync(trainingId);
					var result = new TrainingQuizResultView
					{
						TrainingId = trainingId,
						Name = training.Name,
						Status = submission.Status,
						Score = submission.Result?.Score ?? 0,
						Passed = submission.Result?.Passed ?? false,
						CorrectCount = submission.Result?.CorrectCount ?? 0,
						QuestionCount = submission.Result?.QuestionCount ?? 0,
						MinimumScore = training.MinimumScore,
						AttemptsLeft = submission.AttemptsLeft,
						CanRetake = !(submission.Result?.Passed ?? false) && (submission.AttemptsLeft == null || submission.AttemptsLeft > 0),
						Questions = TrainingQuizView.From(full ?? training, null).Questions
					};
					return View("QuizResult", result);
				case TrainingQuizSubmissionStatuses.NotAssigned:
					return Unauthorized();
				case TrainingQuizSubmissionStatuses.NotFound:
				case null:
					return NotFound();
				case TrainingQuizSubmissionStatuses.AlreadyPassed:
					Flash(L("QuizAlreadyPassedMessage"));
					break;
				case TrainingQuizSubmissionStatuses.AttemptLimitReached:
					Flash(null, L("QuizAttemptLimitError"));
					break;
				default:
					Flash(null, L("QuizNotAvailableError"));
					break;
			}

			return RedirectToAction("View", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> FlagQuestion(int trainingId, int trainingQuestionId, string comment)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();
			if (training.DepartmentId != DepartmentId || !(await ScopeAsync()).CanOpenQuiz(training, DateTime.UtcNow))
				return Unauthorized();

			var flag = await _trainingService.FlagQuestionAsync(DepartmentId, trainingId, trainingQuestionId, UserId, comment);
			Flash(flag != null ? L("QuestionFlaggedMessage") : null, flag == null ? L("QuestionNotFlaggedError") : null);
			return RedirectToAction("View", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Enroll(int trainingId)
		{
			var enrolled = await _assignmentService.SelfEnrollAsync(DepartmentId, trainingId, UserId, DateTime.UtcNow);
			Flash(enrolled != null ? L("EnrolledMessage") : null, enrolled == null ? L("EnrollError") : null);
			return enrolled != null ? RedirectToAction("View", new { trainingId }) : RedirectToAction("Index");
		}

		/// <summary>
		/// Round 9A read and confirm: the member confirms they read and reviewed a training that asks for it (a handbook or policy review); the
		/// assignment completes and goes on their transcript. The statement box must be ticked.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Acknowledge(int trainingId, bool confirmed, CancellationToken cancellationToken)
		{
			if (!confirmed)
			{
				Flash(null, L("AckRequired"));
				return RedirectToAction("View", new { trainingId });
			}
			var assignment = await _trainingService.AcknowledgeTrainingAsync(DepartmentId, trainingId, UserId, cancellationToken);
			Flash(assignment?.Complete == true ? L("AckDone") : null, assignment?.Complete == true ? null : L("AckError"));
			return RedirectToAction("View", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> PinCertification(int personnelCertificationId, bool pinned)
		{
			var done = await _myTrainingService.PinCertificationAsync(DepartmentId, UserId, personnelCertificationId, pinned);
			Flash(done ? (pinned ? L("CertificationPinnedMessage") : L("CertificationUnpinnedMessage")) : null, done ? null : L("CertificationPinError"));
			return RedirectToAction("Index");
		}

		#endregion Taking a training

		#region Archive, restore, copy, delete

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Delete)]
		public async Task<IActionResult> Archive(int trainingId)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();
			if (training.DepartmentId != DepartmentId || !(await ScopeAsync()).CanDelete(training))
				return Unauthorized();

			var archived = await _trainingService.ArchiveTrainingAsync(DepartmentId, trainingId, UserId);
			Flash(archived ? L("TrainingArchivedMessage") : null, archived ? null : L("TrainingNotArchivedError"));
			return RedirectToAction("Manage");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Delete)]
		public async Task<IActionResult> Restore(int trainingId)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();
			if (training.DepartmentId != DepartmentId || !(await ScopeAsync()).CanDelete(training))
				return Unauthorized();

			var restored = await _trainingService.RestoreTrainingAsync(DepartmentId, trainingId, UserId);
			Flash(restored ? L("TrainingRestoredMessage") : null, restored ? null : L("TrainingNotRestoredError"));
			return RedirectToAction("Manage", new { status = "archived" });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Delete)]
		public async Task<IActionResult> DeleteTraining(int trainingId)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);

			if (training == null)
				return NotFound();

			if (training.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await ScopeAsync()).CanDelete(training))
				return Unauthorized();

			// T-D21: a training somebody completed is archived, never deleted.
			var outcome = await _trainingService.DeleteTrainingAsync(DepartmentId, trainingId, UserId);
			if (outcome == TrainingDeleteOutcomes.HasCompletions)
				Flash(null, L("DeleteHasCompletionsError"));
			else if (outcome == TrainingDeleteOutcomes.Deleted)
				Flash(L("TrainingDeletedMessage"));
			else
				return NotFound();

			return RedirectToAction("Manage");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Create)]
		public async Task<IActionResult> Copy(int trainingId)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();

			var scope = await ScopeAsync();
			if (training.DepartmentId != DepartmentId || !scope.CanAuthor(training) || !scope.CanCreate)
				return Unauthorized();

			var copy = await _trainingService.CopyTrainingAsync(DepartmentId, trainingId, UserId, string.Format(L("CopyNameFormat"), training.Name));
			if (copy == null)
				return NotFound();

			Flash(L("TrainingCopiedMessage"));
			return RedirectToAction("Edit", new { trainingId = copy.TrainingId });
		}

		#endregion Archive, restore, copy, delete

		#region Report and per-assignee actions

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> Report(int trainingId, bool showFormer = false)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);

			if (training == null)
				return NotFound();

			var scope = await ScopeAsync();

			// The report lists every member's progress, scores and failed attempts (R-T12): assign-and-report holders only.
			if (training.DepartmentId != DepartmentId || !scope.CanAssign(training))
				return Unauthorized();

			var report = await _trainingService.GetTrainingReportAsync(DepartmentId, trainingId, DateTime.UtcNow);
			if (report == null)
				return NotFound();

			var (message, error) = Flashed();
			var model = new TrainingReportView
			{
				Report = report,
				Department = await DepartmentAsync(),
				ShowFormer = showFormer,
				HasQuiz = report.Training?.Questions?.Count > 0,
				CanAuthor = scope.CanAuthor(training),
				CanDelete = scope.CanDelete(training),
				Names = await NamesAsync(),
				QuestionTexts = (report.Training?.Questions ?? new List<TrainingQuestion>()).GroupBy(q => q.TrainingQuestionId).ToDictionary(g => g.Key, g => g.First().Question),
				Message = message,
				Error = error
			};

			return View(model);
		}

		#region Recorded completions (round 7B, permission 135)

		/// <summary>Record that members completed this training together (the assignee list first); voids of earlier recordings.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> RecordCompletions(int targetId)
		{
			var view = await _recordings.GetTrainingRecordingViewAsync(DepartmentId, targetId, UserId);
			if (view == null)
				return NotFound();
			if (!view.CanRecord)
				return Unauthorized();
			var (message, error) = Flashed();
			var department = await DepartmentAsync();
			return View(new Resgrid.Web.Areas.User.Models.RecordCompletionsPageView
			{
				View = view,
				Controller = "Trainings",
				Names = await NamesAsync(),
				Local = d => department == null ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc).TimeConverter(department),
				Message = message,
				Error = error
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> RecordCompletions(int targetId, List<string> userIds, string note, bool attested, string attestationVersion, CancellationToken cancellationToken)
		{
			var outcome = await _recordings.RecordTrainingAsync(DepartmentId, targetId, new TrainingCompletionRecordingRequest
			{
				RecorderUserId = UserId,
				MemberUserIds = userIds ?? new List<string>(),
				Note = note,
				Attested = attested,
				AttestationVersion = string.IsNullOrWhiteSpace(attestationVersion) ? TrainingCompletionAttestation.CurrentVersion : attestationVersion,
				Source = TrainingCompletionRecordingSources.Manual
			}, cancellationToken);
			FlashRecording(outcome);
			return RedirectToAction("RecordCompletions", new { targetId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> VoidRecordedCompletion(int targetId, int recordingId, string reason, CancellationToken cancellationToken)
		{
			var recording = await _recordings.GetAsync(DepartmentId, recordingId);
			if (recording == null || recording.TargetKind != (int)TrainingCompletionRecordingTargets.Training || recording.TargetId != targetId)
				return NotFound();
			var result = await _recordings.VoidAsync(DepartmentId, recordingId, UserId, reason, cancellationToken);
			Flash(result.Refusal == null ? L("RecordCompletions.VoidDone") : null, result.Refusal == null ? null : L(result.Refusal));
			return RedirectToAction("RecordCompletions", new { targetId });
		}

		private void FlashRecording(TrainingCompletionRecordingOutcome outcome)
		{
			if (!outcome.Succeeded)
			{
				Flash(null, L(outcome.Error));
				return;
			}
			var refusals = outcome.Results.Where(r => !r.Recorded).GroupBy(r => r.Refusal).Select(g => L(g.Key) + " (" + g.Count().ToString(CultureInfo.CurrentCulture) + ")").ToList();
			Flash(string.Format(CultureInfo.CurrentCulture, L("RecordCompletions.ResultFormat"), outcome.Recorded, outcome.Refused), refusals.Count > 0 ? string.Join(" ", refusals) : null);
		}

		#endregion Recorded completions (round 7B, permission 135)

		/// <summary>The training and the right to act on this one member (a group admin only their own group's).</summary>
		private async Task<IActionResult> AssigneeGateAsync(int trainingId, string userId)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();

			var scope = await ScopeAsync();
			if (training.DepartmentId != DepartmentId || !scope.CanAssign(training) || string.IsNullOrWhiteSpace(userId) || !scope.AssignRight.CoversMember(userId))
				return Unauthorized();

			return null;
		}

		/// <summary>A department-local date from a native date input (yyyy-MM-dd).</summary>
		public static DateTime? ParseLocalDate(string value)
		{
			return DateTime.TryParseExact((value ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
				? DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified)
				: (DateTime?)null;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> Extend(int trainingId, string userId, string newDueDate, string reason)
		{
			var refused = await AssigneeGateAsync(trainingId, userId);
			if (refused != null)
				return refused;

			var date = ParseLocalDate(newDueDate);
			var row = date.HasValue && !string.IsNullOrWhiteSpace(reason)
				? await _assignmentService.ExtendAsync(DepartmentId, trainingId, userId, date.Value, reason, UserId)
				: null;

			Flash(row != null ? L("ExtendedMessage") : null, row == null ? L("ExtendError") : null);
			return RedirectToAction("Report", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> Exempt(int trainingId, string userId, string reason, string until)
		{
			var refused = await AssigneeGateAsync(trainingId, userId);
			if (refused != null)
				return refused;

			var row = !string.IsNullOrWhiteSpace(reason)
				? await _assignmentService.ExemptAsync(DepartmentId, trainingId, userId, reason, ParseLocalDate(until), UserId)
				: null;

			Flash(row != null ? L("ExemptedMessage") : null, row == null ? L("ExemptError") : null);
			return RedirectToAction("Report", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> ClearExemption(int trainingId, string userId)
		{
			var refused = await AssigneeGateAsync(trainingId, userId);
			if (refused != null)
				return refused;

			var row = await _assignmentService.ClearExemptionAsync(DepartmentId, trainingId, userId, UserId);
			Flash(row != null ? L("ExemptionClearedMessage") : null, row == null ? L("ExemptError") : null);
			return RedirectToAction("Report", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> RemoveAssignee(int trainingId, string userId)
		{
			var refused = await AssigneeGateAsync(trainingId, userId);
			if (refused != null)
				return refused;

			var removed = await _assignmentService.RemoveAssigneeAsync(DepartmentId, trainingId, userId, UserId);
			Flash(removed ? L("AssigneeRemovedMessage") : null, removed ? null : L("AssigneeNotRemovedError"));
			return RedirectToAction("Report", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> ResetUserTraining(int trainingId, string userId)
		{
			var refused = await AssigneeGateAsync(trainingId, userId);
			if (refused != null)
				return refused;

			var row = await _assignmentService.ResetMemberAsync(DepartmentId, trainingId, userId, UserId);
			Flash(row != null ? L("MemberResetMessage") : null, row == null ? L("MemberNotResetError") : null);
			return RedirectToAction("Report", new { trainingId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> ResolveFlag(int trainingId, int trainingQuestionFlagId)
		{
			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();

			var scope = await ScopeAsync();
			if (training.DepartmentId != DepartmentId || !(scope.CanAuthor(training) || scope.CanAssign(training)))
				return Unauthorized();

			var resolved = await _trainingService.ResolveQuestionFlagAsync(DepartmentId, trainingId, trainingQuestionFlagId, UserId);
			Flash(resolved ? L("FlagResolvedMessage") : null, resolved ? null : L("FlagNotResolvedError"));
			return scope.CanAssign(training) ? RedirectToAction("Report", new { trainingId }) : RedirectToAction("Edit", new { trainingId });
		}

		#endregion Report and per-assignee actions

		#region Leave of absence

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> Leave()
		{
			var scope = await ScopeAsync();
			if (!scope.AssignRight.HoldsAny)
				return Unauthorized();

			var (message, error) = Flashed();
			var model = new TrainingLeaveView { Message = message, Error = error };
			await FillLeaveAsync(model, scope);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> SaveLeave(TrainingLeaveView model)
		{
			var scope = await ScopeAsync();
			if (!scope.AssignRight.HoldsAny)
				return Unauthorized();

			model ??= new TrainingLeaveView();
			var start = ParseLocalDate(model.StartDate);
			var end = ParseLocalDate(model.EndDate);

			if (string.IsNullOrWhiteSpace(model.UserId) || !scope.AssignRight.CoversMember(model.UserId))
				ModelState.AddModelError(nameof(model.UserId), L("LeaveMemberRequired"));
			if (!start.HasValue || !end.HasValue || end.Value < start.Value)
				ModelState.AddModelError(nameof(model.StartDate), L("LeaveDatesInvalid"));

			if (ModelState.IsValid)
			{
				var kind = Enum.IsDefined(typeof(MemberLeaveKinds), model.Kind) ? (MemberLeaveKinds)model.Kind : MemberLeaveKinds.Other;
				var saved = await _leaveService.SaveAsync(DepartmentId, model.UserId, start.Value, end.Value, kind, model.Note, UserId);
				if (saved == null)
					ModelState.AddModelError(nameof(model.UserId), L("LeaveNotSavedError"));
			}

			if (!ModelState.IsValid)
			{
				await FillLeaveAsync(model, scope);
				return View("Leave", model);
			}

			Flash(L("LeaveSavedMessage"));
			return RedirectToAction("Leave");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_Update)]
		public async Task<IActionResult> DeleteLeave(int memberLeavePeriodId)
		{
			var scope = await ScopeAsync();
			if (!scope.AssignRight.HoldsAny)
				return Unauthorized();

			var period = (await _leaveService.GetForDepartmentAsync(DepartmentId) ?? new List<MemberLeavePeriod>()).FirstOrDefault(p => p.MemberLeavePeriodId == memberLeavePeriodId);
			if (period == null)
				return NotFound();
			if (!scope.AssignRight.CoversMember(period.UserId))
				return Unauthorized();

			var deleted = await _leaveService.DeleteAsync(DepartmentId, memberLeavePeriodId, UserId);
			Flash(deleted ? L("LeaveDeletedMessage") : null, deleted ? null : L("LeaveNotSavedError"));
			return RedirectToAction("Leave");
		}

		private async Task FillLeaveAsync(TrainingLeaveView model, TrainingManagementScope scope)
		{
			model.Department = await DepartmentAsync();
			model.Names = await NamesAsync();
			model.Periods = (await _leaveService.GetForDepartmentAsync(DepartmentId) ?? new List<MemberLeavePeriod>())
				.Where(p => scope.AssignRight.CoversMember(p.UserId)).OrderByDescending(p => p.StartsOn).ToList();
			model.Members = (await SelectableMembersAsync()).Where(m => scope.AssignRight.CoversMember(m.Value)).ToList();
		}

		#endregion Leave of absence

		#region Form helpers

		/// <summary>The active members a picker offers, valued by their stored user id (PersonName upper-cases ids).</summary>
		private async Task<List<SelectListItem>> SelectableMembersAsync()
		{
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var items = new List<SelectListItem>();
			foreach (var person in await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId) && active.TryGetValue(person.UserId, out var storedId))
					items.Add(new SelectListItem(person.Name, storedId));
			return items;
		}

		private async Task FillOptionsAsync(TrainingEditorModel model)
		{
			var users = new HashSet<string>(model.UserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			model.Users = (await SelectableMembersAsync()).Select(i => { i.Selected = users.Contains(i.Value); return i; }).ToList();

			var groups = new HashSet<int>(model.GroupIds ?? new List<int>());
			model.Groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
				.OrderBy(g => g.Name).Select(g => new SelectListItem(g.Name, g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), groups.Contains(g.DepartmentGroupId))).ToList();

			var roles = new HashSet<int>(model.RoleIds ?? new List<int>());
			model.Roles = (await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
				.OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name, r.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), roles.Contains(r.PersonnelRoleId))).ToList();

			var certifications = new HashSet<int>(model.CertificationTypeIds ?? new List<int>());
			model.CertificationTypes = (await _certificationService.GetActiveCertificationTypesAsync(DepartmentId, CertificationAppliesTo.Person) ?? new List<DepartmentCertificationType>())
				.OrderBy(t => t.Type).Select(t => new SelectListItem(t.Type, t.DepartmentCertificationTypeId.ToString(CultureInfo.InvariantCulture), certifications.Contains(t.DepartmentCertificationTypeId))).ToList();

			var unitTypes = new HashSet<int>(model.UnitTypeIds ?? new List<int>());
			model.UnitTypes = (await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>())
				.OrderBy(t => t.Type).Select(t => new SelectListItem(t.Type, t.UnitTypeId.ToString(CultureInfo.InvariantCulture), unitTypes.Contains(t.UnitTypeId))).ToList();

			var shifts = new HashSet<int>(model.ShiftIds ?? new List<int>());
			model.Shifts = (await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>())
				.OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.ShiftId.ToString(CultureInfo.InvariantCulture), shifts.Contains(s.ShiftId))).ToList();

			// Training categories (T1): the department's live ones, and a removed one only while it is the stored value.
			var categories = await _learningRecordService.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>();
			model.Categories = categories.Where(c => c.DepartmentId == DepartmentId && (!c.IsDeleted || c.TrainingCategoryId == model.CategoryId))
				.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(c => new SelectListItem(c.IsDeleted ? string.Format(L("Rec.RemovedCategory"), c.Name) : c.Name, c.TrainingCategoryId.ToString(CultureInfo.InvariantCulture), c.TrainingCategoryId == model.CategoryId))
				.ToList();
		}

		/// <summary>The audience rules the form describes (ContentAudienceKinds); Everyone stands alone.</summary>
		public static List<ContentAudienceRule> BuildRules(TrainingEditorModel model)
		{
			var rules = new List<ContentAudienceRule>();
			if (model == null)
				return rules;

			if (model.AssignEveryone)
				return new List<ContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.Everyone) };

			string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
			rules.AddRange((model.UserIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new ContentAudienceRule(ContentAudienceKinds.User, x.Trim())));
			rules.AddRange((model.GroupIds ?? new List<int>()).Where(x => x > 0).Select(x => new ContentAudienceRule(ContentAudienceKinds.Group, Id(x))));
			rules.AddRange((model.RoleIds ?? new List<int>()).Where(x => x > 0).Select(x => new ContentAudienceRule(ContentAudienceKinds.Role, Id(x))));
			rules.AddRange((model.CertificationTypeIds ?? new List<int>()).Where(x => x > 0).Select(x => new ContentAudienceRule(ContentAudienceKinds.CertificationType, Id(x))));
			rules.AddRange((model.UnitTypeIds ?? new List<int>()).Where(x => x > 0).Select(x => new ContentAudienceRule(ContentAudienceKinds.UnitType, Id(x))));
			rules.AddRange((model.ShiftIds ?? new List<int>()).Where(x => x > 0).Select(x => new ContentAudienceRule(ContentAudienceKinds.Shift, Id(x))));

			return ContentAudienceMatcher.Normalize(rules);
		}

		/// <summary>
		/// The questions the editor posted: question_N, questionId_N, questionType_N, answerForQuestion_N_M, answerId_N_M and the
		/// correct option(s) in correct_N (a radio for single and true/false, check boxes for multiple). Capped as the scorer is.
		/// </summary>
		public static List<TrainingQuestionEditor> ParseQuestions(IFormCollection form)
		{
			var questions = new List<TrainingQuestionEditor>();
			if (form == null)
				return questions;

			var numbers = form.Keys.Select(k => QuestionKey.Match(k)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
				.Distinct().OrderBy(n => n).Take(TrainingQuizScorer.MaxQuestions).ToList();

			foreach (var n in numbers)
			{
				var key = n.ToString(CultureInfo.InvariantCulture);
				var question = new TrainingQuestionEditor
				{
					Text = form["question_" + key].ToString(),
					Id = int.TryParse(form["questionId_" + key].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0,
					Type = int.TryParse(form["questionType_" + key].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var type) && Enum.IsDefined(typeof(TrainingQuestionTypes), type)
						? type : (int)TrainingQuestionTypes.SingleChoice
				};

				var correct = new HashSet<string>(form["correct_" + key].Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()), StringComparer.Ordinal);
				var answerKey = new Regex("^answerForQuestion_" + key + "_([1-9][0-9]{0,3})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
				var answerNumbers = form.Keys.Select(k => answerKey.Match(k)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
					.Distinct().OrderBy(m => m).Take(TrainingQuizScorer.MaxOptionsPerQuestion);

				foreach (var m in answerNumbers)
				{
					var answer = m.ToString(CultureInfo.InvariantCulture);
					question.Answers.Add(new TrainingAnswerEditor
					{
						Text = form["answerForQuestion_" + key + "_" + answer].ToString(),
						Id = int.TryParse(form["answerId_" + key + "_" + answer].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var answerId) ? answerId : 0,
						Correct = correct.Contains(answer)
					});
				}

				questions.Add(question);
			}

			return questions;
		}

		private static List<TrainingQuestion> ToEntities(IEnumerable<TrainingQuestionEditor> questions, int trainingId)
		{
			return (questions ?? Enumerable.Empty<TrainingQuestionEditor>()).Select(q => new TrainingQuestion
			{
				TrainingQuestionId = q.Id,
				TrainingId = trainingId,
				Question = q.Text,
				Type = q.Type,
				Answers = q.Answers.Select(a => new TrainingQuestionAnswer
				{
					TrainingQuestionAnswerId = a.Id,
					TrainingQuestionId = q.Id,
					Answer = a.Text,
					Correct = a.Correct
				}).ToList()
			}).ToList();
		}

		private void ValidateQuiz(Training training)
		{
			foreach (var problem in TrainingQuizScorer.Validate(training))
				ModelState.AddModelError("Questions", L("QuizProblem" + problem));
		}

		/// <summary>Copies the editor's content and settings onto the training, adding a model error for each bad value.</summary>
		private void ApplyContent(Training training, TrainingEditorModel model, Department department)
		{
			var name = (model.Name ?? string.Empty).Trim();
			if (name.Length == 0)
				ModelState.AddModelError(nameof(model.Name), L("NameRequired"));
			training.Name = name.Length > 500 ? name.Substring(0, 500) : name;
			training.Description = model.Description ?? string.Empty;
			training.TrainingText = model.TrainingText ?? string.Empty;
			training.ExcludeFromAi = model.ExcludeFromAi;

			// Server-side bounds (T-D23): the browser's range check is not a guarantee.
			if (double.IsNaN(model.MinimumScore) || model.MinimumScore < 0 || model.MinimumScore > 100)
				ModelState.AddModelError(nameof(model.MinimumScore), L("MinimumScoreRange"));
			training.MinimumScore = Math.Max(0, Math.Min(100, double.IsNaN(model.MinimumScore) ? 0 : model.MinimumScore));

			switch ((model.DueMode ?? "none").ToLowerInvariant())
			{
				case "fixed":
					var due = ParseLocalDate(model.DueDate);
					if (!due.HasValue)
						ModelState.AddModelError(nameof(model.DueDate), L("DueDateRequired"));
					training.ToBeCompletedBy = due;
					training.DueDays = null;
					break;
				case "relative":
					if (!model.DueDays.HasValue || model.DueDays.Value < 1 || model.DueDays.Value > 3650)
						ModelState.AddModelError(nameof(model.DueDays), L("DueDaysRange"));
					training.DueDays = model.DueDays;
					training.ToBeCompletedBy = null;
					break;
				default:
					training.ToBeCompletedBy = null;
					training.DueDays = null;
					break;
			}

			training.StartsOn = null;
			if (!string.IsNullOrWhiteSpace(model.StartsOn))
			{
				if (DateTime.TryParseExact(model.StartsOn.Trim(), new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
					training.StartsOn = DateTime.SpecifyKind(DateTime.SpecifyKind(start, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);
				else
					ModelState.AddModelError(nameof(model.StartsOn), L("StartsOnInvalid"));
			}

			training.ClosedToNewAssignees = model.ClosedToNewAssignees;
			training.OpenToAllMembers = model.OpenToAllMembers;
			training.AllowSelfEnroll = model.OpenToAllMembers && model.AllowSelfEnroll;
			training.OneQuestionPerPage = model.OneQuestionPerPage;
			training.RequiresAcknowledgment = model.RequiresAcknowledgment;

			var previousRepeat = training.RepeatMonths;
			training.RepeatMonths = Bounded(model.RepeatMonths, 1, 120, nameof(model.RepeatMonths), "RepeatMonthsRange");
			if (training.RepeatMonths != previousRepeat)
				training.NextRepeatOn = null;
			training.QuizAttemptLimit = Bounded(model.QuizAttemptLimit, 1, 100, nameof(model.QuizAttemptLimit), "AttemptLimitRange");
			training.OverdueRepeatDays = Bounded(model.OverdueRepeatDays, 1, 365, nameof(model.OverdueRepeatDays), "OverdueRepeatRange");
			training.SendOverdueNotices = model.SendOverdueNotices;

			var leads = (model.ReminderLeadDaysCsv ?? string.Empty).Trim();
			if (leads.Length > 0 && leads.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Any(p => !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var d) || d < 1 || d > TrainingDueDates.MaxReminderLeadDays))
				ModelState.AddModelError(nameof(model.ReminderLeadDaysCsv), string.Format(L("LeadDaysInvalid"), TrainingDueDates.MaxReminderLeadDays));
			training.ReminderLeadDaysCsv = leads.Length == 0 ? null : leads;

			training.ReminderMessage = Trim(model.ReminderMessage, 500, nameof(model.ReminderMessage));
			training.CeProvider = Trim(model.CeProvider, 250, nameof(model.CeProvider));
			training.CeCourseNumber = Trim(model.CeCourseNumber, 100, nameof(model.CeCourseNumber));

			if ((model.Hours.HasValue && (model.Hours.Value < 0 || model.Hours.Value > 9999)) || (model.CeHours.HasValue && (model.CeHours.Value < 0 || model.CeHours.Value > 9999)))
				ModelState.AddModelError(nameof(model.Hours), L("HoursRange"));
			training.Hours = model.Hours.HasValue ? Math.Round(model.Hours.Value, 2) : (decimal?)null;
			training.CeHours = model.CeHours.HasValue ? Math.Round(model.CeHours.Value, 2) : (decimal?)null;
			training.CategoryId = model.CategoryId.HasValue && model.CategoryId.Value > 0 ? model.CategoryId : null;

			// Training T1 parity fields (Vector): delivery method, objectives, tags and the external course code.
			if (model.MethodOfTraining.HasValue && !Enum.IsDefined(typeof(MethodsOfTraining), model.MethodOfTraining.Value))
				ModelState.AddModelError(nameof(model.MethodOfTraining), L("Rec.MethodInvalid"));
			training.MethodOfTraining = model.MethodOfTraining.HasValue && Enum.IsDefined(typeof(MethodsOfTraining), model.MethodOfTraining.Value) ? model.MethodOfTraining : null;
			training.Objectives = Trim(model.Objectives, LearningRecord.MaxObjectivesLength, nameof(model.Objectives));
			training.Tags = NormalizeTags(Trim(model.Tags, 500, nameof(model.Tags)));
			training.ExternalCode = Trim(model.ExternalCode, 64, nameof(model.ExternalCode));
		}

		/// <summary>Comma separated tags, trimmed and without repeats.</summary>
		public static string NormalizeTags(string tags)
		{
			if (string.IsNullOrWhiteSpace(tags))
				return null;
			var parts = tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0)
				.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
			if (parts.Count == 0)
				return null;
			var joined = string.Join(", ", parts);
			return joined.Length > 500 ? joined.Substring(0, 500) : joined;
		}

		/// <summary>A training's category is one of the department's (a removed one only when it was already stored).</summary>
		private async Task ValidateCategoryAsync(Training training, int? storedCategoryId)
		{
			if (!training.CategoryId.HasValue || training.CategoryId == storedCategoryId)
				return;

			var categories = await _learningRecordService.GetCategoriesAsync(DepartmentId) ?? new List<TrainingCategory>();
			if (!categories.Any(c => c.TrainingCategoryId == training.CategoryId.Value && c.DepartmentId == DepartmentId && !c.IsDeleted))
			{
				ModelState.AddModelError("CategoryId", L("Rec.CategoryInvalid"));
				training.CategoryId = storedCategoryId;
			}
		}

		private int? Bounded(int? value, int min, int max, string field, string errorKey)
		{
			if (!value.HasValue)
				return null;
			if (value.Value < min || value.Value > max)
				ModelState.AddModelError(field, string.Format(L(errorKey), min, max));
			return Math.Max(min, Math.Min(max, value.Value));
		}

		private string Trim(string value, int max, string field)
		{
			var trimmed = value?.Trim();
			if (string.IsNullOrEmpty(trimmed))
				return null;
			if (trimmed.Length > max)
				ModelState.AddModelError(field, string.Format(L("TextTooLong"), max));
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		private async Task AddAttachmentsAsync(Training training, ICollection<IFormFile> attachments)
		{
			if (attachments == null || attachments.Count == 0)
				return;

			training.Attachments ??= new List<TrainingAttachment>();
			foreach (var file in attachments.Where(f => f != null && f.Length > 0))
			{
				var extension = System.IO.Path.GetExtension(file.FileName ?? string.Empty).TrimStart('.');
				if (!AllowedExtensions.Contains(extension))
				{
					ModelState.AddModelError("attachments", string.Format(L("AttachmentTypeError"), extension));
					continue;
				}

				if (file.Length > MaxAttachmentBytes)
				{
					ModelState.AddModelError("attachments", L("AttachmentSizeError"));
					continue;
				}

				using var stream = file.OpenReadStream();
				using var memory = new System.IO.MemoryStream();
				await stream.CopyToAsync(memory);

				training.Attachments.Add(new TrainingAttachment
				{
					TrainingId = training.TrainingId,
					FileName = System.IO.Path.GetFileName(file.FileName),
					FileType = file.ContentType,
					Data = memory.ToArray()
				});
			}
		}

		private static EditTrainingModel ToEditor(Training training, Department department, bool canAuthor, bool canAssign)
		{
			var model = new EditTrainingModel
			{
				TrainingId = training.TrainingId,
				CanAuthor = canAuthor,
				CanAssign = canAssign,
				Name = training.Name,
				Description = canAuthor ? training.Description : null,
				TrainingText = canAuthor ? training.TrainingText : null,
				MinimumScore = training.MinimumScore,
				DueMode = training.DueDays.HasValue ? "relative" : training.ToBeCompletedBy.HasValue ? "fixed" : "none",
				DueDate = training.ToBeCompletedBy?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				DueDays = training.DueDays,
				StartsOn = training.StartsOn.HasValue ? DateTime.SpecifyKind(training.StartsOn.Value, DateTimeKind.Utc).TimeConverter(department).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) : null,
				ClosedToNewAssignees = training.ClosedToNewAssignees,
				OpenToAllMembers = training.OpenToAllMembers,
				ExcludeFromAi = training.ExcludeFromAi,
				AllowSelfEnroll = training.AllowSelfEnroll,
				RepeatMonths = training.RepeatMonths,
				QuizAttemptLimit = training.QuizAttemptLimit,
				OneQuestionPerPage = training.OneQuestionPerPage,
				RequiresAcknowledgment = training.RequiresAcknowledgment,
				ReminderLeadDaysCsv = training.ReminderLeadDaysCsv,
				SendOverdueNotices = training.SendOverdueNotices,
				OverdueRepeatDays = training.OverdueRepeatDays,
				ReminderMessage = training.ReminderMessage,
				Hours = training.Hours,
				CeHours = training.CeHours,
				CeProvider = training.CeProvider,
				CeCourseNumber = training.CeCourseNumber,
				CategoryId = training.CategoryId,
				MethodOfTraining = training.MethodOfTraining,
				Objectives = training.Objectives,
				Tags = training.Tags,
				ExternalCode = training.ExternalCode,
				IsDynamic = training.Audiences?.Count > 0,
				AssignedCount = training.Users?.Count(u => u != null && !u.RemovedOn.HasValue) ?? 0,
				CompletedCount = training.Users?.Count(u => u != null && u.Complete && !u.RemovedOn.HasValue) ?? 0,
				Attachments = training.Attachments?.ToList() ?? new List<TrainingAttachment>()
			};

			// The answer key is shown only to whoever authors the training (T-D17).
			if (canAuthor)
				model.Questions = (training.Questions ?? new List<TrainingQuestion>()).OrderBy(q => q.TrainingQuestionId).Select(q => new TrainingQuestionEditor
				{
					Id = q.TrainingQuestionId,
					Text = q.Question,
					Type = q.Type,
					Answers = (q.Answers ?? new List<TrainingQuestionAnswer>()).OrderBy(a => a.TrainingQuestionAnswerId)
						.Select(a => new TrainingAnswerEditor { Id = a.TrainingQuestionAnswerId, Text = a.Answer, Correct = a.Correct }).ToList()
				}).ToList();

			foreach (var rule in training.Audiences ?? new List<TrainingAudience>())
			{
				ContentAudienceMatcher.TryId(rule, out var id);
				switch ((ContentAudienceKinds)rule.Kind)
				{
					case ContentAudienceKinds.Everyone: model.AssignEveryone = true; break;
					case ContentAudienceKinds.User: model.UserIds.Add(rule.RefId); break;
					case ContentAudienceKinds.Group: if (id > 0) model.GroupIds.Add(id); break;
					case ContentAudienceKinds.Role: if (id > 0) model.RoleIds.Add(id); break;
					case ContentAudienceKinds.CertificationType: if (id > 0) model.CertificationTypeIds.Add(id); break;
					case ContentAudienceKinds.UnitType: if (id > 0) model.UnitTypeIds.Add(id); break;
					case ContentAudienceKinds.Shift: if (id > 0) model.ShiftIds.Add(id); break;
				}
			}

			return model;
		}

		private async Task<TrainingAudiencePreview> PreviewAsync(TrainingAssignmentOutcome outcome)
		{
			var names = await NamesAsync();
			string Name(string id) => id != null && names.TryGetValue(id, out var name) ? name : id;

			return new TrainingAudiencePreview
			{
				AddedNames = (outcome?.Added ?? new List<string>()).Select(Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(),
				AlreadyAssigned = outcome?.AlreadyAssigned?.Count ?? 0,
				Closed = outcome?.Closed ?? false,
				Skipped = (outcome?.Skipped ?? new List<ContentAudienceSkip>()).Select(s => new TrainingSkippedMember { Name = Name(s.UserId), ReasonKey = TrainingStatePresentation.SkipReasonKey(s.Reason) })
					.Concat((outcome?.OutOfScope ?? new List<string>()).Select(id => new TrainingSkippedMember { Name = Name(id), ReasonKey = "SkipReasonOutOfScope" })).ToList()
			};
		}

		private string OutcomeMessage(TrainingAssignmentOutcome outcome)
		{
			if (outcome == null)
				return null;

			if (outcome.Closed)
				return L("ClosedToNewAssigneesMessage");

			return string.Format(L("AssignedCountMessage"), outcome.Added.Count, outcome.AlreadyAssigned.Count);
		}

		/// <summary>Every member who was picked or matched but not assigned, with the reason (T-D16).</summary>
		private async Task<string> SkippedMessageAsync(TrainingAssignmentOutcome outcome)
		{
			if (outcome == null || (outcome.Skipped.Count == 0 && outcome.OutOfScope.Count == 0))
				return null;

			var preview = await PreviewAsync(new TrainingAssignmentOutcome { Skipped = outcome.Skipped, OutOfScope = outcome.OutOfScope });
			return L("SkippedMembersIntro") + " " + string.Join("; ", preview.Skipped.Select(s => s.Name + " (" + L(s.ReasonKey) + ")"));
		}

		/// <summary>Members just added to a training that has started are told now; the notifier covers everyone else.</summary>
		private async Task NotifyAddedAsync(Training training, List<string> added, DateTime utcNow)
		{
			if (training == null || added == null || added.Count == 0 || !training.HasStarted(utcNow) || training.IsArchived)
				return;

			try
			{
				await _trainingService.SendTrainingNoticesAsync(training, utcNow, added);
			}
			catch (Exception ex)
			{
				// The training and its assignments are saved; a failed notice must not turn the save into an error page.
				Logging.LogException(ex);
			}
		}

		#endregion Form helpers
	}
}
