using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.LmsSkills;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Skills (Training T4 part 2, LMS add-on, dark behind Training.Lms): skill sheets and the template library, evaluations
	/// (the web evaluator mode: grade, Pass All, timer, annotations, evidence, crew split, submit to the S3 evaluator request), position
	/// task books and their definitions, and the notice settings. NotFound unless the Trainings module and the operator flag are on. Every
	/// member reads their own results and task books; LMS managers (Manage Courses or department admins) author and see everything;
	/// evaluators run the sheets their rule allows. While the add-on is lapsed everything stays readable and open evaluations and task books
	/// can be finished; nothing new is made (lapse rule 3).
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsSkillsController : SecureBaseController
	{
		private const long MaxEvidenceRequestBytes = 32 * 1024 * 1024;
		private static readonly Regex ServiceCode = new Regex("^lms_(skill|task_book)_[a-z0-9_]{2,80}", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsSkillSheetService _sheets;
		private readonly ILmsSkillEvaluationService _evaluations;
		private readonly ILmsTaskBookService _taskBooks;
		private readonly ILmsSkillSettingsService _settings;
		private readonly ISignatureRequestService _requests;
		private readonly ISignerEligibilityService _eligibility;
		private readonly ILearningRecordService _learningRecords;
		private readonly ICertificationService _certifications;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentsService _departments;
		private readonly IAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsSkills.LmsSkills> _localizer;

		private LmsActor _actor;
		private Department _department;
		private Dictionary<string, string> _names;

		public LmsSkillsController(ILmsGateService gate, ILmsAccessService access, ILmsSkillSheetService sheets, ILmsSkillEvaluationService evaluations, ILmsTaskBookService taskBooks,
			ILmsSkillSettingsService settings, ISignatureRequestService requests, ISignerEligibilityService eligibility, ILearningRecordService learningRecords, ICertificationService certifications,
			IPersonnelRolesService roles, IDepartmentsService departments, IAuthorizationService authorization,
			IStringLocalizer<Resgrid.Localization.Areas.User.LmsSkills.LmsSkills> localizer)
		{
			_gate = gate;
			_access = access;
			_sheets = sheets;
			_evaluations = evaluations;
			_taskBooks = taskBooks;
			_settings = settings;
			_requests = requests;
			_eligibility = eligibility;
			_learningRecords = learningRecords;
			_certifications = certifications;
			_roles = roles;
			_departments = departments;
			_authorization = authorization;
			_localizer = localizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// Dark until the operator flag is on (and closed with the Trainings module): one gate read per request (R-L9).
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId) || !(await ActorAsync()).Gate.FlagOn)
			{
				context.Result = NotFound();
				return;
			}

			_department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			ViewData["Scope"] = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
			ViewData["LmsPrograms"] = true;
			ViewData["LmsSkills"] = true;
			await next();
		}

		private async Task<LmsActor> ActorAsync() => _actor ??= await _gate.GetActorAsync(DepartmentId, UserId) ?? LmsActor.Nobody(DepartmentId);

		private string L(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		/// <summary>A service refusal's localized text ("lms_skill_step_required:key" shows the step's own message).</summary>
		private string ErrorText(Exception ex)
		{
			if (ex is InvalidOperationException && ex.Message != null && ServiceCode.IsMatch(ex.Message))
			{
				var code = ex.Message.Split(':')[0];
				var text = _localizer?[code];
				if (text != null && !text.ResourceNotFound && !string.IsNullOrEmpty(text.Value))
					return text.Value;
			}
			else if (ex != null && !(ex is InvalidOperationException))
				Logging.LogException(ex);
			return L("Skl.ActionFailed");
		}

		private void Flash(string messageKey, string error = null)
		{
			if (TempData == null)
				return;
			if (messageKey != null)
				TempData["LmsSkillsMessage"] = L(messageKey);
			if (error != null)
				TempData["LmsSkillsError"] = error;
		}

		private async Task<bool> TryAsync(Func<Task> work, string successKey)
		{
			try
			{
				await work();
				Flash(successKey);
				return true;
			}
			catch (InvalidOperationException ex)
			{
				Flash(null, ErrorText(ex));
				return false;
			}
		}

		private async Task<Dictionary<string, string>> NamesAsync() =>
			_names ??= (await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private async Task<List<KeyValuePair<string, string>>> SelectableMembersAsync() =>
			(await _departments.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(n => new KeyValuePair<string, string>(n.UserId, n.Name)).ToList();

		private async Task<T> PageAsync<T>(T view, string tab) where T : LmsSkillPageView
		{
			var actor = await ActorAsync();
			view.CanAuthor = actor.Gate.CanAuthor;
			view.IsLapsed = actor.Gate.IsLapsed;
			view.IsManager = actor.MayManage;
			view.UserId = UserId;
			view.Names = await NamesAsync();
			view.Message ??= TempData?["LmsSkillsMessage"] as string;
			view.Error ??= TempData?["LmsSkillsError"] as string;
			var department = _department;
			view.Date = d => !d.HasValue ? null : department == null ? d.Value.ToShortDateString() : System.DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).TimeConverter(department).ToShortDateString();
			view.When = d => !d.HasValue ? null : department == null ? d.Value.ToString("g") : System.DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).TimeConverter(department).ToString("g");

			ViewData["Section"] = "Skills";
			ViewData["SubTabs"] = Tabs(tab, actor.MayManage);
			return view;
		}

		private ModuleTabsView Tabs(string active, bool manager)
		{
			var tabs = new ModuleTabsView { Label = L("Skl.StripLabel") };
			void Add(string key, string icon, string action) => tabs.Tabs.Add(new ModuleTab(L(key), icon, "LmsSkills", action, action == active));
			Add("Skl.Tab.Home", "fa-th-large", "Index");
			Add("Skl.Tab.Sheets", "fa-list-ol", "Sheets");
			if (manager)
				Add("Skl.Tab.Evaluations", "fa-check-square-o", "Evaluations");
			Add("Skl.Tab.TaskBooks", "fa-book", "TaskBooks");
			if (manager)
			{
				Add("Skl.Tab.Definitions", "fa-sitemap", "Definitions");
				Add("Skl.Tab.Settings", "fa-cog", "Settings");
			}
			return tabs;
		}

		private async Task<(List<LmsSkillChoice> Types, List<LmsSkillChoice> Roles)> ChoicesAsync()
		{
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => !t.IsDeleted && t.AppliesTo == (int)CertificationAppliesTo.Person).OrderBy(t => t.Type, StringComparer.CurrentCultureIgnoreCase)
				.Select(t => new LmsSkillChoice { Id = t.DepartmentCertificationTypeId, Name = t.Type }).ToList();
			var roles = (await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
				.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Select(r => new LmsSkillChoice { Id = r.PersonnelRoleId, Name = r.Name }).ToList();
			return (types, roles);
		}

		private static SignerEligibility Rule(int[] certificationTypeIds, int[] roleIds)
		{
			var rule = new SignerEligibility
			{
				CertificationTypeIds = (certificationTypeIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList(),
				PersonnelRoleIds = (roleIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToList()
			};
			return rule.IsEmpty ? null : rule;
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);

		private string Agent => Request?.Headers["User-Agent"].ToString();

		/// <summary>Who may open an evaluation's page: its evaluator, requester, evaluated members, next signer, and LMS managers.</summary>
		private async Task<bool> MaySeeAsync(LmsSkillEvaluationView view)
		{
			if (view?.Evaluation == null)
				return false;
			var e = view.Evaluation;
			return (await ActorAsync()).MayManage || LmsSkillRules.SameUser(e.EvaluatorUserId, UserId) || LmsSkillRules.SameUser(e.CreatedByUserId, UserId) ||
				LmsSkillRules.SameUser(e.NextSignerUserId, UserId) || view.Members.Any(m => LmsSkillRules.SameUser(m.UserId, UserId)) ||
				(view.TaskBook != null && LmsSkillRules.SameUser(view.TaskBook.InitiatedByUserId, UserId));
		}

		private async Task<bool> MaySeeBookAsync(LmsTaskBook book)
		{
			if (book == null)
				return false;
			var actor = await ActorAsync();
			if (actor.MayManage || LmsSkillRules.SameUser(book.UserId, UserId) || LmsSkillRules.SameUser(book.InitiatedByUserId, UserId) ||
				LmsSkillRules.SameUser(book.FinalEvaluatorUserId, UserId) || LmsSkillRules.SameUser(book.CertifyingOfficialUserId, UserId))
				return true;
			return await CanEvaluateBookAsync(book);
		}

		private async Task<bool> CanEvaluateBookAsync(LmsTaskBook book)
		{
			var actor = await ActorAsync();
			if (book == null || LmsSkillRules.SameUser(book.UserId, UserId))
				return false;
			var rule = SignerEligibility.Parse(book.EvaluatorEligibilityJson);
			return rule == null ? actor.MayManage : await _requests.IsEligibleAsync(DepartmentId, UserId, rule);
		}

		#endregion Gates and helpers

		#region Home and settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Index()
		{
			var view = await PageAsync(new LmsSkillsHomeView(), "Index");
			view.MyOpenEvaluations = await _evaluations.GetOpenForEvaluatorAsync(DepartmentId, UserId);
			view.MySignatures = (await _requests.GetPendingForUserAsync(DepartmentId, UserId) ?? new List<PendingSignatureItem>())
				.Where(p => p.SubjectKind == (int)SignatureSubjectKinds.SkillEvaluation).ToList();
			view.MyResults = (await _evaluations.GetForMemberAsync(DepartmentId, UserId)).Where(e => e.Status != (int)LmsSkillEvaluationStatuses.Voided).Take(25).ToList();
			view.MyTaskBooks = await _taskBooks.GetForMemberAsync(DepartmentId, UserId);
			view.Sheets = (await _sheets.GetSheetsAsync(DepartmentId)).Count(s => s.CurrentVersionId.HasValue);
			if (view.IsManager)
				view.OpenTaskBooks = (await _taskBooks.GetTaskBooksAsync(DepartmentId, true)).Count;
			return View(view);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Settings()
		{
			if (!(await ActorAsync()).MayManage)
				return Unauthorized();
			return View(await PageAsync(new LmsSkillSettingsView { Settings = await _settings.GetAsync(DepartmentId) }, "Settings"));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Settings(int digestMode, int digestHour, int digestWeekday, bool notifyMembers, bool notifyOfficers, CancellationToken cancellationToken)
		{
			if (!(await ActorAsync()).MayManage)
				return Unauthorized();
			await TryAsync(() => _settings.SaveAsync(DepartmentId, new LmsSkillSettings
			{
				DigestMode = digestMode, DigestHour = digestHour, DigestWeekday = digestWeekday, NotifyMembers = notifyMembers, NotifyOfficers = notifyOfficers
			}, UserId, cancellationToken), "Skl.Saved");
			return RedirectToAction(nameof(Settings));
		}

		#endregion Home and settings

		#region Skill sheets

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Sheets()
		{
			var actor = await ActorAsync();
			var view = await PageAsync(new LmsSkillSheetsView { Templates = LmsSkillTemplateCatalog.Sheets }, "Sheets");
			view.Sheets = await _sheets.GetSheetsAsync(DepartmentId);
			foreach (var sheet in view.Sheets)
			{
				if (sheet.CurrentVersionId.HasValue)
				{
					var version = await _sheets.GetVersionAsync(DepartmentId, sheet.CurrentVersionId.Value);
					if (version != null)
						view.Published[sheet.LmsSkillSheetId] = version;
				}
				if (sheet.CurrentVersionId.HasValue && await _evaluations.CanEvaluateAsync(actor, sheet))
					view.CanEvaluate.Add(sheet.LmsSkillSheetId);
			}
			// Members see the sheets in force; managers see drafts too.
			if (!actor.MayManage)
				view.Sheets = view.Sheets.Where(s => s.CurrentVersionId.HasValue).ToList();
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CreateSheet(string title, string code, int kind, CancellationToken cancellationToken)
		{
			LmsSkillSheet created = null;
			await TryAsync(async () => created = await _sheets.CreateAsync(await ActorAsync(), new LmsSkillSheetInput
			{
				Title = title, Code = code, Kind = Enum.IsDefined(typeof(LmsSkillSheetKinds), kind) ? (LmsSkillSheetKinds)kind : LmsSkillSheetKinds.SkillSheet
			}, cancellationToken), "Skl.SheetCreated");
			return created == null ? RedirectToAction(nameof(Sheets)) : RedirectToAction(nameof(Sheet), new { id = created.LmsSkillSheetId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> UseTemplate(string key, CancellationToken cancellationToken)
		{
			LmsSkillSheet created = null;
			await TryAsync(async () => created = await _sheets.CreateFromTemplateAsync(await ActorAsync(), key, cancellationToken), "Skl.SheetCreated");
			return created == null ? RedirectToAction(nameof(Sheets)) : RedirectToAction(nameof(Sheet), new { id = created.LmsSkillSheetId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Sheet(int id)
		{
			var actor = await ActorAsync();
			var sheet = await _sheets.GetSheetAsync(DepartmentId, id);
			if (sheet == null || (sheet.IsDeleted && !actor.MayManage) || (!sheet.CurrentVersionId.HasValue && !actor.MayManage))
				return NotFound();
			var view = await PageAsync(new LmsSkillSheetView { Sheet = sheet }, "Sheets");
			view.Versions = await _sheets.GetVersionsAsync(DepartmentId, id);
			view.Published = await _sheets.GetPublishedVersionAsync(DepartmentId, id);
			view.Draft = actor.MayManage ? await _sheets.GetDraftAsync(DepartmentId, id) : null;
			view.DraftContent = LmsSkillSheetContent.Parse((view.Draft ?? view.Published)?.ContentJson);
			if (view.Draft != null)
				view.DraftErrors = LmsSkillSheetEngine.Validate(view.DraftContent);
			view.Categories = await _learningRecords.GetCategoriesAsync(DepartmentId) ?? new List<TrainingCategory>();
			(view.CertificationTypes, view.Roles) = await ChoicesAsync();
			view.EvaluatorRule = SignerEligibility.Parse(sheet.EvaluatorEligibilityJson);
			view.NextSignerRule = SignerEligibility.Parse(sheet.NextSignerEligibilityJson);
			view.CanEvaluate = view.Published != null && await _evaluations.CanEvaluateAsync(actor, sheet);
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveSheet(int id, long rowVersion, string title, string code, string description, int kind, int? categoryId, int[] evaluatorTypes, int[] evaluatorRoles,
			int[] nextTypes, int[] nextRoles, bool allowSelfEvaluation, bool allowCrewEvaluation, CancellationToken cancellationToken)
		{
			await TryAsync(async () => await _sheets.SaveSheetAsync(await ActorAsync(), id, new LmsSkillSheetInput
			{
				Title = title, Code = code, Description = description, Kind = Enum.IsDefined(typeof(LmsSkillSheetKinds), kind) ? (LmsSkillSheetKinds)kind : LmsSkillSheetKinds.SkillSheet,
				CategoryId = categoryId, EvaluatorEligibility = Rule(evaluatorTypes, evaluatorRoles), NextSignerEligibility = Rule(nextTypes, nextRoles),
				AllowSelfEvaluation = allowSelfEvaluation, AllowCrewEvaluation = allowCrewEvaluation, ExpectedRowVersion = rowVersion
			}, cancellationToken), "Skl.Saved");
			return RedirectToAction(nameof(Sheet), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveDraft(int id, string contentJson, decimal passThreshold, int maxAttempts, int attemptWindowDays, int timeLimitSeconds, int defaultMinutes,
			CancellationToken cancellationToken)
		{
			LmsSkillSheetContent content;
			try
			{
				content = JsonConvert.DeserializeObject<LmsSkillSheetContent>(contentJson ?? "{}") ?? new LmsSkillSheetContent();
			}
			catch (JsonException)
			{
				Flash(null, L("lms_skill_sheet_step_key"));
				return RedirectToAction(nameof(Sheet), new { id });
			}
			await TryAsync(async () => await _sheets.SaveDraftAsync(await ActorAsync(), id, new LmsSkillSheetDraftInput
			{
				Content = content, PassThreshold = passThreshold, MaxAttempts = maxAttempts, AttemptWindowDays = attemptWindowDays, TimeLimitSeconds = timeLimitSeconds, DefaultMinutes = defaultMinutes
			}, cancellationToken), "Skl.DraftSaved");
			return RedirectToAction(nameof(Sheet), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Publish(int id, bool major, string changeSummary, CancellationToken cancellationToken)
		{
			await TryAsync(async () => await _sheets.PublishAsync(await ActorAsync(), id, major, changeSummary, cancellationToken), "Skl.Published");
			return RedirectToAction(nameof(Sheet), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteSheet(int id, CancellationToken cancellationToken)
		{
			await TryAsync(async () => await _sheets.DeleteAsync(await ActorAsync(), id, cancellationToken), "Skl.SheetDeleted");
			return RedirectToAction(nameof(Sheets));
		}

		#endregion Skill sheets

		#region Evaluations

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Start(int? sheetId, int? taskBookId, string taskKey)
		{
			var actor = await ActorAsync();
			var view = await PageAsync(new LmsSkillStartView { SheetId = sheetId }, "Sheets");
			if (taskBookId.HasValue)
			{
				var book = (await _taskBooks.GetViewAsync(DepartmentId, taskBookId.Value))?.Book;
				if (book == null || !await CanEvaluateBookAsync(book))
					return NotFound();
				view.TaskBook = book;
				view.Task = LmsSkillRules.ParseTasks(book.TasksJson).FirstOrDefault(t => string.Equals(t.Key, taskKey, StringComparison.OrdinalIgnoreCase));
				if (view.Task == null)
					return NotFound();
			}
			else
			{
				foreach (var sheet in (await _sheets.GetSheetsAsync(DepartmentId)).Where(s => s.CurrentVersionId.HasValue))
					if (await _evaluations.CanEvaluateAsync(actor, sheet))
						view.Sheets.Add(sheet);
				view.Members = await SelectableMembersAsync();
			}
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Start(int? sheetId, string[] memberUserIds, int? taskBookId, string taskKey, int? callId, int? deploymentId, string location,
			CancellationToken cancellationToken)
		{
			LmsSkillEvaluation started = null;
			var members = (memberUserIds ?? Array.Empty<string>()).ToList();
			if (taskBookId.HasValue)
			{
				var book = (await _taskBooks.GetViewAsync(DepartmentId, taskBookId.Value))?.Book;
				if (book != null)
					members = new List<string> { book.UserId };
			}
			await TryAsync(async () => started = await _evaluations.StartAsync(await ActorAsync(), new LmsSkillEvaluationStart
			{
				LmsSkillSheetId = sheetId, MemberUserIds = members, LmsTaskBookId = taskBookId, TaskKey = taskKey, CallId = callId, DeploymentId = deploymentId, Location = location
			}, cancellationToken), "Skl.EvaluationStarted");
			if (started != null)
				return RedirectToAction(nameof(Run), new { id = started.LmsSkillEvaluationId });
			return taskBookId.HasValue ? RedirectToAction(nameof(TaskBook), new { id = taskBookId.Value }) : RedirectToAction(nameof(Start), new { sheetId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Run(int id)
		{
			var evaluation = await _evaluations.GetViewAsync(DepartmentId, id);
			if (evaluation == null || !LmsSkillRules.SameUser(evaluation.Evaluation.EvaluatorUserId, UserId))
				return NotFound();
			if (evaluation.Evaluation.Status != (int)LmsSkillEvaluationStatuses.InProgress)
				return RedirectToAction(nameof(Evaluation), new { id });
			var actor = await ActorAsync();
			var view = await PageAsync(new LmsSkillRunView { Evaluation = evaluation }, "Sheets");
			view.NextSignerRequired = !evaluation.Evaluation.LmsTaskBookId.HasValue && SignerEligibility.Parse(evaluation.Sheet?.NextSignerEligibilityJson) != null;
			if (view.NextSignerRequired)
				view.NextSignerCandidates = (await _evaluations.GetNextSignerCandidatesAsync(DepartmentId, evaluation.Sheet))
					.Where(u => !LmsSkillRules.SameUser(u, UserId) && !evaluation.Members.Any(m => m.IsIncluded && LmsSkillRules.SameUser(m.UserId, u))).ToList();
			view.AllowExternalEvaluator = actor.Gate.CanAuthor;
			view.SubmitErrors = LmsSkillSheetEngine.Evaluate(evaluation.Content, evaluation.Answers, evaluation.Version?.Grading, 0,
				new HashSet<string>(evaluation.Files.Where(f => f.StepKey != null).Select(f => f.StepKey), StringComparer.OrdinalIgnoreCase), true).Errors;
			return View(view);
		}

		/// <summary>The answers the run form posted ("s.{key}.value", ".values", ".note", ".na", ".naReason", ".addNote"), merged onto the stored ones.</summary>
		private LmsSkillAnswers ReadAnswers(LmsSkillEvaluationView evaluation)
		{
			var answers = evaluation.Answers?.Clone() ?? new LmsSkillAnswers();
			var form = Request.HasFormContentType ? Request.Form : null;
			if (form == null)
				return answers;
			var now = System.DateTime.UtcNow;
			foreach (var step in evaluation.Content.Steps.Where(s => s != null && !LmsSkillSheetEngine.IsDisplayOnly(s.StepType)))
			{
				var prefix = "s." + step.Key + ".";
				var existing = answers.Get(step.Key) ?? new LmsSkillAnswer();
				var answer = new LmsSkillAnswer { Notes = existing.Notes, AnsweredOn = existing.AnsweredOn, Note = form[prefix + "note"].ToString() };
				if (string.IsNullOrWhiteSpace(answer.Note))
					answer.Note = null;
				if (form[prefix + "na"].ToString() == "true")
				{
					answer.Status = (int)LmsSkillAnswerStatuses.NotApplicable;
					answer.NotApplicableReason = form[prefix + "naReason"].ToString();
				}
				else if (step.StepType == LmsSkillStepTypes.ChooseAny)
				{
					answer.Values = form[prefix + "values"].Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
					answer.Status = answer.Values.Count > 0 ? (int)LmsSkillAnswerStatuses.Answered : (int)LmsSkillAnswerStatuses.Unanswered;
				}
				else if (step.StepType == LmsSkillStepTypes.TimestampedNotes)
				{
					answer.Notes = (existing.Notes ?? new List<LmsSkillTimedNote>()).ToList();
					var added = form[prefix + "addNote"].ToString();
					if (!string.IsNullOrWhiteSpace(added))
						answer.Notes.Add(new LmsSkillTimedNote { At = now, Text = added.Trim() });
					answer.Status = answer.Notes.Count > 0 ? (int)LmsSkillAnswerStatuses.Answered : (int)LmsSkillAnswerStatuses.Unanswered;
				}
				else if (step.StepType == LmsSkillStepTypes.Evidence)
					answer.Status = (int)LmsSkillAnswerStatuses.Answered;
				else
				{
					var value = form[prefix + "value"].ToString();
					answer.Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
					answer.Status = answer.Value == null ? (int)LmsSkillAnswerStatuses.Unanswered : (int)LmsSkillAnswerStatuses.Answered;
				}
				if (answer.Status == (int)LmsSkillAnswerStatuses.Answered && existing.Status != (int)LmsSkillAnswerStatuses.Answered)
					answer.AnsweredOn = now;
				if (answer.Status == (int)LmsSkillAnswerStatuses.Unanswered && answer.Note == null && (answer.Notes == null || answer.Notes.Count == 0))
					answers.Steps.Remove(step.Key);
				else
					answers.Steps[step.Key] = answer;
			}
			return answers;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Run(int id, long rowVersion, string notes, bool passAll, string submitAction, string nextSignerUserId, string externalName,
			string externalEmail, bool externalRemote, CancellationToken cancellationToken)
		{
			var evaluation = await _evaluations.GetViewAsync(DepartmentId, id);
			if (evaluation == null || !LmsSkillRules.SameUser(evaluation.Evaluation.EvaluatorUserId, UserId))
				return NotFound();
			var actor = await ActorAsync();
			var answers = ReadAnswers(evaluation);
			LmsSkillEvaluation saved = null;
			if (!await TryAsync(async () => saved = await _evaluations.SaveAnswersAsync(actor, id, rowVersion, answers, notes, passAll, cancellationToken), submitAction == "submit" ? null : "Skl.Saved"))
				return RedirectToAction(nameof(Run), new { id });

			if (submitAction != "submit")
				return RedirectToAction(nameof(Run), new { id });

			LmsSkillEvaluation submitted = null;
			if (!await TryAsync(async () => submitted = await _evaluations.SubmitAsync(actor, id, new LmsSkillEvaluationSubmit
			{
				ExpectedRowVersion = saved.RowVersion, NextSignerUserId = nextSignerUserId, ExternalEvaluatorName = externalName, ExternalEvaluatorEmail = externalEmail,
				ExternalRemote = externalRemote, Culture = CultureInfo.CurrentUICulture.Name, IpAddress = Ip, UserAgent = Agent
			}, cancellationToken), "Skl.Submitted"))
				return RedirectToAction(nameof(Run), new { id });

			// The evaluator signs at L2 on the shared signing page, then comes back to the evaluation.
			if (submitted?.SignatureRequestId != null && string.IsNullOrWhiteSpace(submitted.ExternalEvaluatorName))
				return Redirect(Url.Action("Step", "Signatures", new { area = "User", id = submitted.SignatureRequestId.Value, returnUrl = Url.Action(nameof(Evaluation), new { id }) }));
			return RedirectToAction(nameof(Evaluation), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxEvidenceRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxEvidenceRequestBytes)]
		public async Task<IActionResult> Evidence(int id, string stepKey, IFormFile file, CancellationToken cancellationToken)
		{
			if (file == null || file.Length <= 0)
			{
				Flash(null, L("lms_skill_evidence_size"));
				return RedirectToAction(nameof(Run), new { id });
			}
			if (file.Length > LmsSkillEvaluationFile.MaxBytes)
			{
				Flash(null, L("lms_skill_evidence_size"));
				return RedirectToAction(nameof(Run), new { id });
			}
			using var stream = new MemoryStream();
			await file.CopyToAsync(stream, cancellationToken);
			await TryAsync(async () => await _evaluations.AddEvidenceAsync(await ActorAsync(), id, stepKey, file.FileName, file.ContentType, stream.ToArray(), cancellationToken), "Skl.EvidenceAdded");
			return RedirectToAction(nameof(Run), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Split(int id, string userId, CancellationToken cancellationToken)
		{
			LmsSkillEvaluation split = null;
			await TryAsync(async () => split = await _evaluations.SplitMemberAsync(await ActorAsync(), id, userId, cancellationToken), "Skl.MemberSplit");
			return RedirectToAction(nameof(Run), new { id = split?.LmsSkillEvaluationId ?? id });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Evaluation(int id)
		{
			var evaluation = await _evaluations.GetViewAsync(DepartmentId, id);
			if (evaluation == null || !await MaySeeAsync(evaluation))
				return NotFound();
			var actor = await ActorAsync();
			var view = await PageAsync(new LmsSkillEvaluationDetailView { Evaluation = evaluation }, actor.MayManage ? "Evaluations" : "Index");
			view.IsEvaluator = LmsSkillRules.SameUser(evaluation.Evaluation.EvaluatorUserId, UserId);
			view.CanVoid = actor.MayManage && evaluation.Evaluation.Status != (int)LmsSkillEvaluationStatuses.Voided;
			if (evaluation.Evaluation.SignatureRequestId.HasValue)
			{
				view.Request = await _requests.GetAsync(DepartmentId, evaluation.Evaluation.SignatureRequestId.Value);
				view.Signers = await _requests.GetSignersAsync(DepartmentId, evaluation.Evaluation.SignatureRequestId.Value) ?? new List<SignatureRequestSigner>();
			}
			return View(view);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> EvidenceFile(int id, int fileId)
		{
			var evaluation = await _evaluations.GetViewAsync(DepartmentId, id);
			if (evaluation == null || !await MaySeeAsync(evaluation))
				return NotFound();
			var file = await _evaluations.GetEvidenceAsync(DepartmentId, id, fileId);
			if (file?.Data == null)
				return NotFound();
			return File(file.Data, string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, file.FileName ?? "evidence");
		}

		/// <summary>The sealed evidence PDF only: while sealing is pending nothing is handed out as the record (fails closed).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Pdf(int id, CancellationToken cancellationToken)
		{
			var evaluation = await _evaluations.GetViewAsync(DepartmentId, id);
			if (evaluation == null || !await MaySeeAsync(evaluation))
				return NotFound();
			var bytes = await _evaluations.GetSealedPdfAsync(DepartmentId, id, cancellationToken);
			if (bytes == null)
			{
				Flash(null, L("Skl.SealPending"));
				return RedirectToAction(nameof(Evaluation), new { id });
			}
			return File(bytes, "application/pdf", "skill-evaluation-" + id.ToString(CultureInfo.InvariantCulture) + ".pdf");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Void(int id, string reason, CancellationToken cancellationToken)
		{
			await TryAsync(async () => await _evaluations.VoidAsync(await ActorAsync(), id, reason, cancellationToken), "Skl.Voided");
			return RedirectToAction(nameof(Evaluation), new { id });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Evaluations(int? sheetId, int? status)
		{
			if (!(await ActorAsync()).MayManage)
				return Unauthorized();
			var view = await PageAsync(new LmsSkillEvaluationsView { SheetId = sheetId, Status = status }, "Evaluations");
			view.Sheets = await _sheets.GetSheetsAsync(DepartmentId, true);
			view.Evaluations = await _evaluations.GetForDepartmentAsync(DepartmentId, null, null, sheetId, status, 500);
			view.Members = (await _evaluations.GetMembersAsync(DepartmentId, view.Evaluations.Select(e => e.LmsSkillEvaluationId)))
				.GroupBy(m => m.LmsSkillEvaluationId).ToDictionary(g => g.Key, g => g.ToList());
			return View(view);
		}

		#endregion Evaluations

		#region Task books

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> TaskBooks()
		{
			var actor = await ActorAsync();
			var view = await PageAsync(new LmsTaskBooksView(), "TaskBooks");
			view.Mine = await _taskBooks.GetForMemberAsync(DepartmentId, UserId);
			if (actor.MayManage)
			{
				view.All = await _taskBooks.GetTaskBooksAsync(DepartmentId, false);
				view.Definitions = await _taskBooks.GetDefinitionsAsync(DepartmentId);
				view.Members = await SelectableMembersAsync();
			}
			else
			{
				// Evaluators see the open books whose tasks they may sign.
				foreach (var book in await _taskBooks.GetTaskBooksAsync(DepartmentId, true))
					if (await CanEvaluateBookAsync(book))
						view.All.Add(book);
			}
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Initiate(int definitionId, string userId, DateTime? initiatedOn, CancellationToken cancellationToken)
		{
			LmsTaskBook book = null;
			var department = _department;
			var initiated = initiatedOn.HasValue && department != null
				? System.DateTime.SpecifyKind(System.DateTime.SpecifyKind(initiatedOn.Value.Date, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc)
				: System.DateTime.UtcNow;
			await TryAsync(async () => book = await _taskBooks.InitiateAsync(await ActorAsync(), definitionId, userId, initiated, cancellationToken), "Skl.TaskBookInitiated");
			return book == null ? RedirectToAction(nameof(TaskBooks)) : RedirectToAction(nameof(TaskBook), new { id = book.LmsTaskBookId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> TaskBook(int id)
		{
			var book = await _taskBooks.GetViewAsync(DepartmentId, id);
			if (book == null || !await MaySeeBookAsync(book.Book))
				return NotFound();
			var actor = await ActorAsync();
			var view = await PageAsync(new LmsTaskBookDetailView { Book = book }, "TaskBooks");
			view.CanEvaluateTasks = book.Book.Status == (int)LmsTaskBookStatuses.Open && actor.Gate.CanAuthor && await CanEvaluateBookAsync(book.Book);
			if (actor.MayManage && book.Book.Status == (int)LmsTaskBookStatuses.Open && book.AllTasksComplete)
			{
				var finalRule = SignerEligibility.Parse(book.Book.FinalEvaluatorEligibilityJson) ?? SignerEligibility.Parse(book.Book.EvaluatorEligibilityJson);
				var certifyingRule = SignerEligibility.Parse(book.Book.CertifyingEligibilityJson);
				var admins = (await _departments.GetActiveAdminsForDepartmentAsync(DepartmentId) ?? new List<Resgrid.Model.Identity.IdentityUser>()).Select(a => a.UserId).ToList();
				view.FinalEvaluatorCandidates = finalRule == null ? admins : await _eligibility.GetEligibleUserIdsAsync(DepartmentId, finalRule) ?? new List<string>();
				view.CertifyingCandidates = certifyingRule == null ? admins : await _eligibility.GetEligibleUserIdsAsync(DepartmentId, certifyingRule) ?? new List<string>();
				view.FinalEvaluatorCandidates = view.FinalEvaluatorCandidates.Where(u => !LmsSkillRules.SameUser(u, book.Book.UserId)).ToList();
				view.CertifyingCandidates = view.CertifyingCandidates.Where(u => !LmsSkillRules.SameUser(u, book.Book.UserId)).ToList();
			}
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RequestCertification(int id, string finalEvaluatorUserId, string certifyingOfficialUserId, CancellationToken cancellationToken)
		{
			await TryAsync(async () => await _taskBooks.RequestCertificationAsync(await ActorAsync(), id, finalEvaluatorUserId, certifyingOfficialUserId, CultureInfo.CurrentUICulture.Name,
				cancellationToken), "Skl.CertificationRequested");
			return RedirectToAction(nameof(TaskBook), new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CancelTaskBook(int id, string reason, CancellationToken cancellationToken)
		{
			await TryAsync(async () => await _taskBooks.CancelAsync(await ActorAsync(), id, reason, cancellationToken), "Skl.TaskBookCancelled");
			return RedirectToAction(nameof(TaskBook), new { id });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Definitions()
		{
			if (!(await ActorAsync()).MayManage)
				return Unauthorized();
			var view = await PageAsync(new LmsTaskBookDefinitionsView { Templates = LmsSkillTemplateCatalog.TaskBooks }, "Definitions");
			view.Definitions = await _taskBooks.GetDefinitionsAsync(DepartmentId);
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> UseTaskBookTemplate(string key, CancellationToken cancellationToken)
		{
			LmsTaskBookDefinition created = null;
			await TryAsync(async () => created = await _taskBooks.CreateFromTemplateAsync(await ActorAsync(), key, cancellationToken), "Skl.DefinitionCreated");
			return created == null ? RedirectToAction(nameof(Definitions)) : RedirectToAction(nameof(Definition), new { id = created.LmsTaskBookDefinitionId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Definition(int? id)
		{
			if (!(await ActorAsync()).MayManage)
				return Unauthorized();
			var definition = id.HasValue ? await _taskBooks.GetDefinitionAsync(DepartmentId, id.Value) : new LmsTaskBookDefinition { ValidityMonths = LmsTaskBookDefinition.DefaultValidityMonths };
			if (definition == null)
				return NotFound();
			var view = await PageAsync(new LmsTaskBookDefinitionView { Definition = definition, Tasks = LmsSkillRules.ParseTasks(definition.TasksJson) }, "Definitions");
			view.Sheets = (await _sheets.GetSheetsAsync(DepartmentId)).Where(s => s.CurrentVersionId.HasValue).ToList();
			(view.CertificationTypes, view.Roles) = await ChoicesAsync();
			view.EvaluatorRule = SignerEligibility.Parse(definition.EvaluatorEligibilityJson);
			view.FinalEvaluatorRule = SignerEligibility.Parse(definition.FinalEvaluatorEligibilityJson);
			view.CertifyingRule = SignerEligibility.Parse(definition.CertifyingEligibilityJson);
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveDefinition(int id, string positionCode, string name, string description, int family, int? certificationTypeId, int validityMonths,
			int recertifyMonths, int[] evaluatorTypes, int[] evaluatorRoles, int[] finalTypes, int[] finalRoles, int[] certifyingTypes, int[] certifyingRoles,
			string[] taskKey, string[] taskNumber, string[] taskTitle, int[] taskRequired, string[] taskIncident, int[] taskSheet, CancellationToken cancellationToken)
		{
			var tasks = new List<LmsTaskBookTask>();
			var incident = new HashSet<string>(taskIncident ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			for (var i = 0; i < (taskTitle?.Length ?? 0); i++)
			{
				if (string.IsNullOrWhiteSpace(taskTitle[i]))
					continue;
				var key = taskKey != null && i < taskKey.Length && !string.IsNullOrWhiteSpace(taskKey[i]) ? taskKey[i].Trim() : "t" + Guid.NewGuid().ToString("N").Substring(0, 8);
				tasks.Add(new LmsTaskBookTask
				{
					Key = key,
					Number = taskNumber != null && i < taskNumber.Length ? taskNumber[i] : null,
					Title = taskTitle[i],
					RequiredEvaluations = taskRequired != null && i < taskRequired.Length ? Math.Max(1, taskRequired[i]) : 1,
					RequiresIncident = incident.Contains(key),
					LmsSkillSheetId = taskSheet != null && i < taskSheet.Length && taskSheet[i] > 0 ? taskSheet[i] : null
				});
			}

			LmsTaskBookDefinition saved = null;
			await TryAsync(async () => saved = await _taskBooks.SaveDefinitionAsync(await ActorAsync(), new LmsTaskBookDefinition
			{
				LmsTaskBookDefinitionId = id,
				PositionCode = positionCode,
				Name = name,
				Description = description,
				Family = family,
				CertificationTypeId = certificationTypeId,
				ValidityMonths = validityMonths,
				RecertifyMonths = recertifyMonths,
				EvaluatorEligibilityJson = Rule(evaluatorTypes, evaluatorRoles)?.ToJson(),
				FinalEvaluatorEligibilityJson = Rule(finalTypes, finalRoles)?.ToJson(),
				CertifyingEligibilityJson = Rule(certifyingTypes, certifyingRoles)?.ToJson()
			}, tasks, cancellationToken), "Skl.Saved");
			return saved == null ? (id > 0 ? RedirectToAction(nameof(Definition), new { id }) : RedirectToAction(nameof(Definitions)))
				: RedirectToAction(nameof(Definition), new { id = saved.LmsTaskBookDefinitionId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteDefinition(int id, CancellationToken cancellationToken)
		{
			await TryAsync(async () => await _taskBooks.DeleteDefinitionAsync(await ActorAsync(), id, cancellationToken), "Skl.DefinitionDeleted");
			return RedirectToAction(nameof(Definitions));
		}

		#endregion Task books
	}
}
