using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Courses;
using Resgrid.Web.Areas.User.Models.Lms;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Programs (Training T3, LMS, protocols-training-knowledge-plan §13 T3, §18.3): learning requirements, the compliance
	/// matrix (person × requirement, group-admin scope, CSV and PDF), learning paths with prerequisites, onboarding, and training-backed
	/// enforcement with its preview ("who would lose which role today", R-L11) and history. Dark while the Training.Lms flag is off;
	/// authoring, exemptions and new assignments need the add-on (lapse rule 3), and a lapsed department sees the pages paused (rule 5).
	/// LMS managers (Manage Courses 132, department admins) author; holders of Assign Training (126) department-wide, and group admins for
	/// their group, read the matrix and exempt or extend their members.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsRequirementsController : SecureBaseController
	{
		private const string MessageKey = "LmsProgramsMessage";
		private const string ErrorKey = "LmsProgramsError";

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsRequirementService _requirements;
		private readonly ILmsPathService _paths;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsAcademyService _academy;
		private readonly ITrainingService _trainings;
		private readonly ILearningRecordService _learningRecords;
		private readonly ICertificationService _certifications;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IShiftsService _shifts;
		private readonly IAuthorizationService _authorization;
		private readonly IPdfProvider _pdf;
		private readonly ILmsRecommendationService _recommendations;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsPrograms.LmsPrograms> _localizer;

		private LmsActor _actor;
		private TrainingManagementScope _scope;
		private Department _department;

		public LmsRequirementsController(ILmsGateService gate, ILmsAccessService access, ILmsRequirementService requirements, ILmsPathService paths, ILmsCoursesRepository courses,
			ILmsAcademyService academy, ITrainingService trainings, ILearningRecordService learningRecords, ICertificationService certifications,
			IDepartmentsService departments, IDepartmentGroupsService groups, IPersonnelRolesService roles, IShiftsService shifts, IAuthorizationService authorization,
			IPdfProvider pdf, ILmsRecommendationService recommendations, IStringLocalizer<Resgrid.Localization.Areas.User.LmsPrograms.LmsPrograms> localizer)
		{
			_gate = gate;
			_access = access;
			_requirements = requirements;
			_paths = paths;
			_courses = courses;
			_academy = academy;
			_trainings = trainings;
			_learningRecords = learningRecords;
			_certifications = certifications;
			_departments = departments;
			_groups = groups;
			_roles = roles;
			_shifts = shifts;
			_authorization = authorization;
			_pdf = pdf;
			_recommendations = recommendations;
			_localizer = localizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
			{
				context.Result = NotFound();
				return;
			}

			_actor = await _gate.GetActorAsync(DepartmentId, UserId);
			// The whole section is LMS: dark while the operator flag is off.
			if (_actor == null || !_actor.IsCurrentMember || _actor.Gate == null || !_actor.Gate.FlagOn)
			{
				context.Result = NotFound();
				return;
			}

			_department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			_scope = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
			ViewData["Scope"] = _scope;
			ViewData["LmsRequirements"] = true;
			ViewData["LmsLibrary"] = _actor.MayManage || _actor.MayGrade;
			ViewData["CanRecommend"] = _actor.Gate.FlagOn && _actor.Gate.AddonActive == true;
			ViewData["AcademyLesson"] = "09-training";
			await next();
		}

		/// <summary>Reads the matrix: LMS managers, Assign Training department-wide, and group admins (their group).</summary>
		private bool MayReadCompliance => _actor.MayManage || _scope.AssignRight.ManagesAll || (_scope.AssignRight.AdministeredMemberUserIds?.Count ?? 0) > 0;

		private string Text(string key) => _localizer[key].Value;

		private T Page<T>(T model) where T : LmsProgramsPage
		{
			model.Message ??= TempData[MessageKey] as string;
			model.Error ??= TempData[ErrorKey] as string;
			model.Actor = _actor;
			model.Scope = _scope;
			model.Department = _department;
			return model;
		}

		private void Flash(LmsResult result, string successKey)
		{
			if (result != null && result.Succeeded)
				TempData[MessageKey] = Text(successKey);
			else
				TempData[ErrorKey] = ErrorText(result);
		}

		private string ErrorText(LmsResult result)
		{
			if (result?.Value is string key && key.StartsWith("Err.", StringComparison.Ordinal))
				return Text(key);
			switch (result?.Error ?? LmsErrors.InvalidInput)
			{
				case LmsErrors.NotAuthorized: return Text("Err.NotAuthorized");
				case LmsErrors.AddonRequired: return Text("Err.AddonRequired");
				case LmsErrors.NotFound: return Text("Err.NotFound");
				default: return Text("Err.Invalid");
			}
		}

		private DateTime? LocalDateToUtcEnd(string localDate)
		{
			if (string.IsNullOrWhiteSpace(localDate) || !DateTime.TryParseExact(localDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
				return null;
			return TrainingDueDates.EndOfLocalDayUtc(date, _department);
		}

		private string Locale => CultureInfo.CurrentUICulture.Name;

		private async Task<List<LmsOption>> CourseOptionsAsync()
		{
			var options = new List<LmsOption>();
			foreach (var course in (await _courses.GetForDepartmentAsync(DepartmentId) ?? new List<LmsCourse>()).Where(c => !c.IsDeleted).OrderBy(c => c.Title))
				options.Add(new LmsOption(course.LmsCourseId.ToString(CultureInfo.InvariantCulture), course.Title));
			foreach (var course in (await _courses.GetSystemCoursesAsync() ?? new List<LmsCourse>()).Where(c => !c.IsDeleted && c.SystemKey != null))
				options.Add(new LmsOption(course.LmsCourseId.ToString(CultureInfo.InvariantCulture),
					Text("AcademyPrefix") + " " + _academy.Text(AcademyCourseManifest.LessonIdFromSystemKey(course.SystemKey) + ".Title", Locale)));
			return options;
		}

		private async Task<List<LmsOption>> TrainingOptionsAsync() =>
			(await _trainings.GetAllTrainingsForDepartmentAsync(DepartmentId) ?? new List<Training>()).Where(t => !t.IsArchived).OrderBy(t => t.Name)
				.Select(t => new LmsOption(t.TrainingId.ToString(CultureInfo.InvariantCulture), t.Name)).ToList();

		private async Task<List<LmsOption>> CertificationOptionsAsync(bool expiringOnly) =>
			(await _certifications.GetActiveCertificationTypesAsync(DepartmentId, CertificationAppliesTo.Person) ?? new List<DepartmentCertificationType>())
				.Where(t => !expiringOnly || !t.NeverExpires).OrderBy(t => t.Type)
				.Select(t => new LmsOption(t.DepartmentCertificationTypeId.ToString(CultureInfo.InvariantCulture), t.Type)).ToList();

		private async Task<List<LmsOption>> RoleOptionsAsync() =>
			(await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).OrderBy(r => r.Name)
				.Select(r => new LmsOption(r.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), r.Name)).ToList();

		private async Task<List<LmsOption>> GroupOptionsAsync() =>
			(await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>()).OrderBy(g => g.Name)
				.Select(g => new LmsOption(g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), g.Name)).ToList();

		private async Task<List<LmsOption>> MemberOptionsAsync() =>
			(await _departments.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>()).OrderBy(p => p.Name)
				.Select(p => new LmsOption(p.UserId, p.Name)).ToList();

		private async Task<Dictionary<string, string>> NamesAsync() =>
			(await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		#endregion Gates and helpers

		#region Requirements and onboarding

		[HttpGet]
		public async Task<IActionResult> Index(bool onboarding = false)
		{
			if (!_actor.MayManage)
				return MayReadCompliance ? RedirectToAction(nameof(Compliance)) : Unauthorized();

			var rows = await _requirements.GetRequirementsAsync(_actor, DateTime.UtcNow);
			var model = Page(new LmsRequirementsIndexView { Rows = onboarding ? rows.Where(r => r.Requirement.IsOnboarding).ToList() : rows, OnboardingOnly = onboarding });
			foreach (var role in await RoleOptionsAsync()) model.AudienceNames["2:" + role.Value] = role.Text;
			foreach (var group in await GroupOptionsAsync()) model.AudienceNames["1:" + group.Value] = group.Text;
			foreach (var type in await CertificationOptionsAsync(false)) model.AudienceNames["3:" + type.Value] = type.Text;
			foreach (var shift in await _shifts.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>()) model.AudienceNames["5:" + shift.ShiftId.ToString(CultureInfo.InvariantCulture)] = shift.Name;
			return View(model);
		}

		[HttpGet]
		public Task<IActionResult> Onboarding() => Index(onboarding: true);

		[HttpGet]
		public async Task<IActionResult> Requirement(int? id = null, bool onboarding = false)
		{
			if (!_actor.MayManage)
				return Unauthorized();

			var form = new LmsRequirementForm { IsOnboarding = onboarding, NewMembersOnly = onboarding, AutoAssign = onboarding, Kind = onboarding ? (int)LmsRequirementKinds.Path : 0 };
			if (id.HasValue)
			{
				var requirement = await _requirements.GetRequirementAsync(DepartmentId, id.Value);
				if (requirement == null || requirement.DeletedOn.HasValue)
					return NotFound();
				form = LmsRequirementForm.From(requirement, await _requirements.GetAudienceAsync(DepartmentId, id.Value));
			}
			else
			{
				form.AtRiskDays = (await _requirements.GetSettingsAsync(DepartmentId)).DefaultAtRiskDays;
			}

			return View(await EditViewAsync(form));
		}

		private async Task<LmsRequirementEditView> EditViewAsync(LmsRequirementForm form)
		{
			var model = Page(new LmsRequirementEditView { Form = form });
			model.Courses = await CourseOptionsAsync();
			model.Trainings = await TrainingOptionsAsync();
			model.Categories = (await _learningRecords.GetCategoriesAsync(DepartmentId) ?? new List<TrainingCategory>()).Where(c => !c.IsDeleted).OrderBy(c => c.Name)
				.Select(c => new LmsOption(c.TrainingCategoryId.ToString(CultureInfo.InvariantCulture), c.Name)).ToList();
			model.Paths = (await _paths.GetPathsAsync(_actor) ?? new List<LmsPathRow>())
				.Select(p => new LmsOption(p.Path.LmsPathId.ToString(CultureInfo.InvariantCulture), (p.Path.IsSystem ? Text("AcademyPrefix") + " " : string.Empty) + p.Title)).ToList();
			model.CertificationTypes = await CertificationOptionsAsync(false);
			model.ExpiringCertificationTypes = await CertificationOptionsAsync(true);
			model.Roles = await RoleOptionsAsync();
			model.Groups = await GroupOptionsAsync();
			model.Shifts = (await _shifts.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>()).OrderBy(s => s.Name)
				.Select(s => new LmsOption(s.ShiftId.ToString(CultureInfo.InvariantCulture), s.Name)).ToList();
			model.Members = await MemberOptionsAsync();
			return model;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveRequirement(LmsRequirementForm form, CancellationToken cancellationToken = default)
		{
			var result = await _requirements.SaveRequirementAsync(_actor, form?.ToInput(), cancellationToken);
			if (!result.Succeeded)
			{
				var model = await EditViewAsync(form ?? new LmsRequirementForm());
				model.Error = ErrorText(result);
				return View(nameof(Requirement), model);
			}

			Flash(result, "Saved");
			return RedirectToAction(form != null && form.IsOnboarding ? nameof(Onboarding) : nameof(Index));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteRequirement(int id, CancellationToken cancellationToken = default)
		{
			Flash(await _requirements.DeleteRequirementAsync(_actor, id, cancellationToken), "Deleted");
			return RedirectToAction(nameof(Index));
		}

		#endregion Requirements and onboarding

		#region Recommendations (K3)

		/// <summary>The member's learning recommendations (their own; officers pick a member in their scope), deterministic, with reasons.</summary>
		[HttpGet]
		public async Task<IActionResult> Recommendations(string userId = null, CancellationToken cancellationToken = default)
		{
			var set = await _recommendations.GetForMemberAsync(_actor, _scope, userId, DateTime.UtcNow, cancellationToken);
			if (set.Error == LmsErrors.NotAuthorized || set.Error == LmsErrors.NotFound)
				return Unauthorized();

			var members = await _recommendations.GetMembersInScopeAsync(_actor, _scope);
			var memberId = set.UserId ?? UserId;
			var model = Page(new LmsRecommendationsView
			{
				Members = members,
				MemberUserId = memberId,
				MemberName = string.Equals(memberId, UserId, StringComparison.OrdinalIgnoreCase) ? null
					: members.FirstOrDefault(m => string.Equals(m.UserId, memberId, StringComparison.OrdinalIgnoreCase))?.Name
			});
			model.List = Page(new LmsRecommendationListModel
			{
				Set = set,
				IsSelf = string.Equals(memberId, UserId, StringComparison.OrdinalIgnoreCase),
				CanOpenPaths = _actor.MayManage || _actor.MayAssignAcademy,
				PanelId = "lms-recommendations"
			});
			if (set.Error == LmsErrors.AddonRequired)
				model.Error ??= Text("Err.AddonRequired");
			return View(model);
		}

		/// <summary>The optional AI explanation of a member's recommendations (JSON; Enhanced AI, nothing saved).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ExplainRecommendations(string userId, CancellationToken cancellationToken = default)
		{
			var explanation = await _recommendations.ExplainAsync(_actor, _scope, userId, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, DateTime.UtcNow, cancellationToken);
			if (explanation == null || explanation.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();
			var answered = explanation.Outcome == KnowledgeOutcomes.Answered;
			return Json(new
			{
				outcome = explanation.Outcome,
				message = answered ? null : explanation.Outcome == KnowledgeOutcomes.BudgetExhausted || explanation.Outcome == KnowledgeOutcomes.Busy
					? Text("ExplainBusy") : Text("ExplainFailed"),
				texts = explanation.Texts
			});
		}

		#endregion Recommendations (K3)

		#region Compliance matrix

		[HttpGet]
		public async Task<IActionResult> Compliance(int? requirementId = null, int? groupId = null, int? state = null)
		{
			if (!MayReadCompliance)
				return Unauthorized();

			var matrix = await _requirements.GetMatrixAsync(_actor, _scope, Filter(requirementId, groupId, state), DateTime.UtcNow);
			if (matrix.Error == LmsErrors.NotAuthorized)
				return Unauthorized();

			var model = Page(new LmsComplianceView { Matrix = matrix, RequirementId = requirementId, GroupId = groupId, State = state });
			if (matrix.Error == LmsErrors.AddonRequired)
				model.Error = Text("Err.AddonRequired");
			model.Groups = await GroupOptionsAsync();
			model.CanChangeMembers = _actor.Gate.CanAuthor;
			return View(model);
		}

		private static LmsMatrixFilter Filter(int? requirementId, int? groupId, int? state) => new LmsMatrixFilter
		{
			LmsRequirementId = requirementId,
			DepartmentGroupId = groupId,
			State = state.HasValue && Enum.IsDefined(typeof(LmsRequirementStates), state.Value) ? (LmsRequirementStates?)state.Value : null
		};

		private string StateText(LmsRequirementStates state) => Text("State." + state);

		private string CellText(LmsComplianceView view, LmsMatrixCell cell)
		{
			var text = StateText(cell.State);
			if (cell.Counted && cell.State != LmsRequirementStates.Compliant)
				text += " " + string.Format(CultureInfo.InvariantCulture, "{0:0.##}/{1:0.##}", cell.Progress, cell.Required);
			return text;
		}

		[HttpGet]
		public async Task<IActionResult> ComplianceCsv(int? requirementId = null, int? groupId = null, int? state = null)
		{
			if (!MayReadCompliance)
				return Unauthorized();
			var matrix = await _requirements.GetMatrixAsync(_actor, _scope, Filter(requirementId, groupId, state), DateTime.UtcNow);
			if (matrix.Error != LmsErrors.None)
				return Unauthorized();

			var view = Page(new LmsComplianceView { Matrix = matrix });
			var sb = new StringBuilder();
			sb.AppendLine(string.Join(",", new[] { Text("Member"), Text("Requirement"), Text("Status"), Text("DueOn"), Text("LastCompleted") }.Select(Csv)));
			foreach (var row in matrix.Rows)
				foreach (var requirement in matrix.Requirements)
					if (row.Cells.TryGetValue(requirement.LmsRequirementId, out var cell))
						sb.AppendLine(string.Join(",", new[] { row.Name, requirement.Name, CellText(view, cell), view.Date(cell.DueUtc) ?? string.Empty, view.Date(cell.LastCompletedUtc) ?? string.Empty }.Select(Csv)));
			return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", "compliance.csv");
		}

		/// <summary>CSV cell: quoted, with a leading formula character neutralised (CSV injection).</summary>
		private static string Csv(string value)
		{
			var text = value ?? string.Empty;
			if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0)
				text = "'" + text;
			return "\"" + text.Replace("\"", "\"\"") + "\"";
		}

		[HttpGet]
		public async Task<IActionResult> CompliancePdf(int? requirementId = null, int? groupId = null, int? state = null)
		{
			if (!MayReadCompliance)
				return Unauthorized();
			var matrix = await _requirements.GetMatrixAsync(_actor, _scope, Filter(requirementId, groupId, state), DateTime.UtcNow);
			if (matrix.Error != LmsErrors.None)
				return Unauthorized();

			var view = Page(new LmsComplianceView { Matrix = matrix });
			string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			var html = new StringBuilder();
			html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,sans-serif;font-size:10px}table{border-collapse:collapse;width:100%}" +
				"th,td{border:1px solid #999;padding:3px;text-align:center}td.n{text-align:left}.Compliant{background:#dff0d8}.AtRisk{background:#fcf8e3}.Overdue{background:#f2dede}" +
				".Exempt,.NotLicensed,.OnLeave{background:#eee}</style></head><body>");
			html.Append("<h2>").Append(E(Text("ComplianceTitle"))).Append(" - ").Append(E(_department?.Name)).Append("</h2><p>").Append(E(view.Date(DateTime.UtcNow))).Append("</p><table><thead><tr><th>")
				.Append(E(Text("Member"))).Append("</th>");
			foreach (var requirement in matrix.Requirements)
				html.Append("<th>").Append(E(requirement.Name)).Append("</th>");
			html.Append("</tr></thead><tbody>");
			foreach (var row in matrix.Rows)
			{
				html.Append("<tr><td class=\"n\">").Append(E(row.Name)).Append("</td>");
				foreach (var requirement in matrix.Requirements)
				{
					if (row.Cells.TryGetValue(requirement.LmsRequirementId, out var cell))
						html.Append("<td class=\"").Append(cell.State).Append("\">").Append(E(CellText(view, cell))).Append("<br/>").Append(E(view.Date(cell.DueUtc))).Append("</td>");
					else
						html.Append("<td>-</td>");
				}
				html.Append("</tr>");
			}
			html.Append("</tbody></table></body></html>");
			return File(_pdf.ConvertHtmlToPdf(html.ToString()), "application/pdf", "compliance.pdf");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Exempt(int memberRowId, bool exempt, string until, string reason, int? requirementId = null, CancellationToken cancellationToken = default)
		{
			var result = await _requirements.SetExemptionAsync(_actor, _scope, memberRowId, exempt, LocalDateToUtcEnd(until), reason, cancellationToken);
			Flash(result, "Saved");
			return RedirectToAction(nameof(Compliance), new { requirementId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Extend(int memberRowId, string dueOn, string reason, int? requirementId = null, CancellationToken cancellationToken = default)
		{
			var result = await _requirements.ExtendAsync(_actor, _scope, memberRowId, LocalDateToUtcEnd(dueOn), reason, cancellationToken);
			Flash(result, "Saved");
			return RedirectToAction(nameof(Compliance), new { requirementId });
		}

		#endregion Compliance matrix

		#region Learning paths

		[HttpGet]
		public async Task<IActionResult> Paths()
		{
			if (!_actor.MayManage && !_actor.MayAssignAcademy)
				return Unauthorized();
			return View(Page(new LmsPathsIndexView { Rows = await _paths.GetPathsAsync(_actor) }));
		}

		[HttpGet]
		public async Task<IActionResult> Path(int? id = null, bool edit = false)
		{
			if (!_actor.MayManage && !_actor.MayAssignAcademy)
				return Unauthorized();

			LmsPathDetail detail = null;
			var form = new LmsPathForm();
			if (id.HasValue)
			{
				detail = await _paths.GetPathAsync(_actor, id.Value);
				if (detail == null)
					return NotFound();
				form = new LmsPathForm
				{
					LmsPathId = detail.Path.LmsPathId,
					Name = detail.Path.Name,
					Description = detail.Path.Description,
					IssuesCertificationTypeId = detail.Path.IssuesCertificationTypeId,
					Steps = detail.Steps.Select(s => new LmsPathStepForm
					{
						Kind = s.Step.Kind,
						CourseId = s.Step.Kind == (int)LmsPathStepKinds.Course ? s.Step.TargetRefId : (int?)null,
						TrainingId = s.Step.Kind == (int)LmsPathStepKinds.Training ? s.Step.TargetRefId : (int?)null,
						Prerequisites = s.Step.PrerequisiteOrders,
						ElectiveGroup = s.Step.ElectiveGroup,
						ElectiveRequired = s.Step.ElectiveRequired
					}).ToList()
				};
			}
			else if (!_actor.MayManage)
				return Unauthorized();

			return View(await PathViewAsync(detail, form, editing: !id.HasValue || (edit && detail?.CanEdit == true)));
		}

		private async Task<LmsPathEditView> PathViewAsync(LmsPathDetail detail, LmsPathForm form, bool editing)
		{
			var model = Page(new LmsPathEditView { Detail = detail, Form = form, Editing = editing });
			if (editing)
			{
				// Blank rows so the editor works without script (blank rows are ignored on save).
				form.Steps ??= new List<LmsPathStepForm>();
				var blanks = form.Steps.Count == 0 ? 3 : 1;
				for (var i = 0; i < blanks; i++)
					form.Steps.Add(new LmsPathStepForm());
				model.Courses = await CourseOptionsAsync();
				model.Trainings = await TrainingOptionsAsync();
				model.CertificationTypes = await CertificationOptionsAsync(false);
			}
			if (detail != null)
			{
				model.Progress = await _paths.GetProgressAsync(_actor, detail.Path.LmsPathId);
				model.Names = await NamesAsync();
				if (detail.CanAssign)
				{
					model.Roles = await RoleOptionsAsync();
					model.Groups = await GroupOptionsAsync();
					model.Members = await MemberOptionsAsync();
				}
			}
			return model;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SavePath(LmsPathForm form, CancellationToken cancellationToken = default)
		{
			var result = await _paths.SavePathAsync(_actor, form?.ToInput(), cancellationToken);
			if (!result.Succeeded)
			{
				var detail = form != null && form.LmsPathId > 0 ? await _paths.GetPathAsync(_actor, form.LmsPathId) : null;
				var model = await PathViewAsync(detail, form ?? new LmsPathForm(), editing: true);
				model.Error = ErrorText(result);
				return View(nameof(Path), model);
			}

			Flash(result, "Saved");
			return RedirectToAction(nameof(Path), new { id = (result.Value as LmsPath)?.LmsPathId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeletePath(int id, CancellationToken cancellationToken = default)
		{
			Flash(await _paths.DeletePathAsync(_actor, id, cancellationToken), "Deleted");
			return RedirectToAction(nameof(Paths));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AssignPath(int id, CourseWhoInput who, CancellationToken cancellationToken = default)
		{
			var outcome = await _paths.AssignAsync(_actor, id, who?.Rules() ?? new List<ContentAudienceRule>(), LocalDateToUtcEnd(who?.DueOn), null, cancellationToken);
			if (outcome.Error == LmsErrors.None)
				TempData[MessageKey] = string.Format(Text("AssignedFormat"), outcome.Created, outcome.AlreadyEnrolled);
			else
				TempData[ErrorKey] = ErrorText(new LmsResult { Error = outcome.Error });
			return RedirectToAction(nameof(Path), new { id });
		}

		#endregion Learning paths

		#region Enforcement and settings

		[HttpGet]
		public async Task<IActionResult> Enforcement()
		{
			if (!_actor.MayManage)
				return Unauthorized();
			return View(await EnforcementViewAsync());
		}

		private async Task<LmsEnforcementView> EnforcementViewAsync()
		{
			var model = Page(new LmsEnforcementView
			{
				Settings = await _requirements.GetSettingsAsync(DepartmentId),
				Preview = await _requirements.PreviewEnforcementAsync(_actor, DateTime.UtcNow),
				History = await _requirements.GetEnforcementHistoryAsync(_actor, 100),
				Names = await NamesAsync()
			});
			foreach (var requirement in await _requirements.GetRequirementsAsync(_actor, DateTime.UtcNow))
				model.RequirementNames[requirement.Requirement.LmsRequirementId] = requirement.Requirement.Name;
			return model;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveSettings(LmsSettingsForm form, CancellationToken cancellationToken = default)
		{
			if (form == null)
				return BadRequest();

			// Turning enforcement on shows who would lose which role first (R-L11): the officer confirms having read the preview.
			var current = await _requirements.GetSettingsAsync(DepartmentId);
			if (form.EnforcementEnabled && !current.EnforcementEnabled && !form.ConfirmPreview)
			{
				var model = await EnforcementViewAsync();
				model.Error = Text("ConfirmRequired");
				return View(nameof(Enforcement), model);
			}

			var result = await _requirements.SaveSettingsAsync(_actor, new LearningRequirementSettings
			{
				EnforcementEnabled = form.EnforcementEnabled,
				MaxRoleRemovalSharePercent = form.MaxRoleRemovalSharePercent,
				DefaultAtRiskDays = form.DefaultAtRiskDays
			}, cancellationToken);
			Flash(result, "Saved");
			return RedirectToAction(nameof(Enforcement));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UndoEnforcement(int id, CancellationToken cancellationToken = default)
		{
			Flash(await _requirements.UndoEnforcementAsync(_actor, id, cancellationToken), "Undone");
			return RedirectToAction(nameof(Enforcement));
		}

		#endregion Enforcement and settings
	}
}
