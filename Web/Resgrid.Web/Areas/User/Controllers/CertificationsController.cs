using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Certifications;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Certification administration (Workforce &amp; Business Operations plan, Phase D9): the expiry dashboard, the typed
	/// catalog with its template gallery, role requirements, department settings, unit certification records and the
	/// per-record page (status, verification, renewal, credits). Phase D is free: no add-on or flag. A member always
	/// reaches their own record page; everything else follows the three certification permissions with department
	/// administrators as the fallback (plan D8). Protected values read REDACTED without a grant.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(12 * 1024 * 1024)]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public sealed class CertificationsController : SecureBaseController
	{
		private static readonly string[] AllowedExtensions = { "jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "txt", "xls", "xlsx" };
		private const int MaxFileBytes = 10 * 1024 * 1024;

		private readonly ICertificationService _certifications;
		private readonly IPersonnelRolesService _roles;
		private readonly IUnitsService _units;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUserProfileService _profiles;
		private readonly IProtectedReadService _protectedRead;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Certifications.Certifications> _strings;

		public CertificationsController(ICertificationService certifications, IPersonnelRolesService roles, IUnitsService units, IDepartmentsService departments,
			IDepartmentGroupsService groups, IUserProfileService profiles, IProtectedReadService protectedRead,
			IStringLocalizer<Resgrid.Localization.Areas.User.Certifications.Certifications> strings)
		{
			_certifications = certifications;
			_roles = roles;
			_units = units;
			_departments = departments;
			_groups = groups;
			_profiles = profiles;
			_protectedRead = protectedRead;
			_strings = strings;
		}

		#region Plumbing

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanView => IsAdmin || ClaimsAuthorizationHelper.CanViewCertifications();
		private static bool CanManage => IsAdmin || ClaimsAuthorizationHelper.CanManageCertifications();
		private static bool CanSetup => IsAdmin || ClaimsAuthorizationHelper.CanManageCertificationSetup();

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			ViewData["CertCanView"] = CanView;
			ViewData["CertCanManage"] = CanManage;
			ViewData["CertCanSetup"] = CanSetup;
			await next();
		}

		private T Page<T>(T view) where T : CertificationPageView
		{
			view.CanView = CanView;
			view.CanManage = CanManage;
			view.CanSetup = CanSetup;
			if (TempData["CertificationsMessage"] is string message) view.Message = message;
			if (TempData["CertificationsSaved"] is bool saved) view.SaveSuccess = saved;
			return view;
		}

		private bool IsAjax() => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

		/// <summary>Maps a certifications_* code to its message; unknown codes fall back to SaveFailed.</summary>
		private string ErrorText(string code)
		{
			var text = _strings[code];
			return text.ResourceNotFound ? _strings["SaveFailed"].Value : text.Value;
		}

		private IActionResult Refused(int statusCode, string code, string redirectAction, object routeValues = null)
		{
			if (IsAjax())
				return StatusCode(statusCode, new { message = ErrorText(code), code });
			TempData["CertificationsMessage"] = ErrorText(code);
			return RedirectToAction(redirectAction, routeValues);
		}

		private IActionResult Saved(string redirectAction, object routeValues = null)
		{
			if (IsAjax())
				return Json(new { success = true });
			TempData["CertificationsSaved"] = true;
			return RedirectToAction(redirectAction, routeValues);
		}

		private async Task<Dictionary<string, string>> PersonnelNamesAsync()
		{
			var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			return (names ?? new List<PersonName>()).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		/// ADP reveal endpoint (plan 7.2) for the record and unit pages. The grant rides the X-Resgrid-Protected-Grant
		/// header: catalog 6 resolves here with the explicit grant, catalog 27/28 through the certification service's
		/// request-bound grant context.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Reveal([FromForm] string kind, [FromForm] int id)
		{
			var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (string.Equals(kind, "unit", StringComparison.OrdinalIgnoreCase))
			{
				if (!CanView) return Unauthorized();
				var unit = await _units.GetUnitByIdAsync(id);
				if (unit == null || unit.DepartmentId != DepartmentId) return NotFound();
				foreach (var row in await _certifications.GetUnitCertificationsAsync(id))
					foreach (var accessor in CertificationProtectedFields.Unit) fields[accessor.Key + ":" + row.UnitCertificationId] = accessor.Value.Get(row);
			}
			else
			{
				var record = await AuthorizedRecordAsync(id, false);
				if (record == null) return Unauthorized();
				record.Data = null;
				var resolved = await _protectedRead.ResolveCertificationsForReadAsync(DepartmentId, new[] { record }, Request.Headers["X-Resgrid-Protected-Grant"].ToString(), UserId);
				if (resolved != null && resolved.IsProtected && resolved.ProtectedReason != null)
					return Json(new { success = false, error = resolved.ProtectedReason });
				foreach (var accessor in Resgrid.Services.ProtectedReadService.CertificationFieldAccessors) fields[accessor.Key] = accessor.Value.Get(record);
				foreach (var credit in await _certifications.GetCertificationCreditsAsync(id))
					foreach (var accessor in CertificationProtectedFields.Credit) fields[accessor.Key + ":" + credit.PersonnelCertificationCreditId] = accessor.Value.Get(credit);
			}
			return AdpRevealHelper.Answer(this, fields);
		}

		private async Task<(byte[] Data, string FileName, string FileType, string Error)> ReadUploadAsync(IFormFile file, CancellationToken cancellationToken)
		{
			if (file == null || file.Length == 0)
				return (null, null, null, null);
			var extension = FileHelper.GetFileExtensionWithoutDot(file.FileName);
			if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
				return (null, null, null, "certifications_file_type");
			if (file.Length > MaxFileBytes)
				return (null, null, null, "certifications_file_too_large");
			using var stream = file.OpenReadStream();
			var bytes = await FileHelper.ReadAllBytesAsync(stream, cancellationToken);
			return (bytes, FileHelper.GetSafeFileName(file.FileName), string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, null);
		}

		#endregion

		#region Dashboard

		[HttpGet]
		public async Task<IActionResult> Index(string scope = "person", int? category = null, int? groupId = null, int? roleId = null)
		{
			if (!CanView)
				return Unauthorized();

			var view = Page(new CertificationDashboardView
			{
				Dashboard = await _certifications.GetExpiryDashboardAsync(DepartmentId),
				Settings = await _certifications.GetCertificationSettingsAsync(DepartmentId),
				Scope = scope == "unit" ? "unit" : "person",
				Category = category,
				GroupId = groupId,
				RoleId = roleId,
				Groups = await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>(),
				Roles = await _roles.GetAllRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()
			});
			view.Horizon = view.Settings.GetNotifyLeadDays().DefaultIfEmpty(60).Max();
			view.SubjectFilter = await SubjectFilterAsync(groupId, roleId);
			return View(view);
		}

		private async Task<HashSet<string>> SubjectFilterAsync(int? groupId, int? roleId)
		{
			HashSet<string> filter = null;
			if (groupId > 0)
				filter = new HashSet<string>((await _groups.GetAllMembersForGroupAsync(groupId.Value) ?? new List<DepartmentGroupMember>()).Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
			if (roleId > 0)
			{
				var members = new HashSet<string>((await _roles.GetAllMembersOfRoleAsync(roleId.Value) ?? new List<PersonnelRoleUser>()).Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
				filter = filter == null ? members : new HashSet<string>(filter.Intersect(members, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
			}
			return filter;
		}

		/// <summary>CSV of the current matrix (the fleet-audit deliverable, plan D9). Never carries a protected value: names, types, statuses and dates only.</summary>
		[HttpGet]
		public async Task<IActionResult> DashboardCsv(string scope = "person", int? category = null, int? groupId = null, int? roleId = null)
		{
			if (!CanView)
				return Unauthorized();
			var dashboard = await _certifications.GetExpiryDashboardAsync(DepartmentId);
			var filter = await SubjectFilterAsync(groupId, roleId);
			var cells = (scope == "unit" ? dashboard.UnitCells : dashboard.PersonCells.Where(c => filter == null || filter.Contains(c.SubjectId)))
				.Where(c => !category.HasValue || c.Category == category.Value).OrderBy(c => c.SubjectName).ThenBy(c => c.TypeName).ToList();
			var sb = new StringBuilder();
			sb.AppendLine(string.Join(",", new[] { _strings[scope == "unit" ? "Unit" : "Member"].Value, _strings["TypeCode"].Value, _strings["TypeName"].Value, _strings["Category"].Value, _strings["Status"].Value, _strings["ExpiresOn"].Value, _strings["DaysUntilExpiry"].Value }.Select(Csv)));
			foreach (var c in cells)
				sb.AppendLine(string.Join(",", new[] { c.SubjectName, c.TypeCode, c.TypeName, ((CertificationCategories)c.Category).ToString(), StatusText(scope, c.Status), c.NeverExpires ? _strings["NeverExpires"].Value : c.ExpiresOn?.ToString("yyyy-MM-dd") ?? string.Empty, c.DaysUntilExpiry?.ToString(CultureInfo.InvariantCulture) ?? string.Empty }.Select(Csv)));
			return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"certifications-{scope}-{DateTime.UtcNow:yyyyMMdd}.csv");
		}

		private static string Csv(string value)
		{
			value ??= string.Empty;
			if (value.StartsWith("=") || value.StartsWith("+") || value.StartsWith("-") || value.StartsWith("@")) value = "'" + value;
			return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
		}

		private string StatusText(string scope, int status)
			=> scope == "unit" ? _strings["UnitStatus" + (UnitCertificationStatuses)status].Value : _strings["Status" + (PersonnelCertificationStatuses)status].Value;

		#endregion

		#region Types and templates

		[HttpGet]
		public async Task<IActionResult> Types(bool showInactive = false)
		{
			if (!CanSetup)
				return Unauthorized();
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).Where(t => !t.IsDeleted && (showInactive || t.IsActive)).OrderBy(t => t.AppliesTo).ThenBy(t => t.Category).ThenBy(t => t.Type).ToList();
			var records = await _certifications.GetCertificationsForDepartmentAsync(DepartmentId);
			var unitRecords = await _certifications.GetUnitCertificationsForDepartmentAsync(DepartmentId);
			var requirements = await _certifications.GetAllRoleRequirementsAsync(DepartmentId);
			var counts = records.Where(r => r.IsTyped).GroupBy(r => r.DepartmentCertificationTypeId.Value).ToDictionary(g => g.Key, g => g.Count());
			foreach (var group in unitRecords.GroupBy(u => u.DepartmentCertificationTypeId))
				counts[group.Key] = (counts.TryGetValue(group.Key, out var n) ? n : 0) + group.Count();
			var view = Page(new CertificationTypesView
			{
				Types = types,
				ShowInactive = showInactive,
				RecordCounts = counts,
				RequirementCounts = requirements.GroupBy(r => r.DepartmentCertificationTypeId).ToDictionary(g => g.Key, g => g.Count()),
				ExistingCodes = new HashSet<string>((await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).Where(t => !t.IsDeleted).Select(t => t.Code ?? string.Empty), StringComparer.OrdinalIgnoreCase)
			});
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> EditType(int id = 0)
		{
			if (!CanSetup)
				return Unauthorized();
			var view = Page(new CertificationTypeEditView());
			if (id > 0)
			{
				var type = await _certifications.GetCertificationTypeByIdAsync(id);
				if (type == null || type.IsDeleted || type.DepartmentId != DepartmentId)
					return NotFound();
				view.Type = CertificationTypeInput.From(type);
				view.CodeLocked = (await _certifications.GetAllRoleRequirementsAsync(DepartmentId)).Any(r => r.DepartmentCertificationTypeId == id);
				view.ScopeLocked = (await _certifications.GetCertificationsForDepartmentAsync(DepartmentId)).Any(r => r.DepartmentCertificationTypeId == id)
					|| (await _certifications.GetUnitCertificationsForDepartmentAsync(DepartmentId)).Any(u => u.DepartmentCertificationTypeId == id);
			}
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> EditType(CertificationTypeInput input, CancellationToken cancellationToken)
		{
			if (!CanSetup)
				return Unauthorized();
			if (input == null)
				return BadRequest();
			try
			{
				DepartmentCertificationType type;
				if (input.DepartmentCertificationTypeId > 0)
				{
					type = await _certifications.GetCertificationTypeByIdAsync(input.DepartmentCertificationTypeId);
					if (type == null || type.IsDeleted || type.DepartmentId != DepartmentId)
						return NotFound();
				}
				else
					type = new DepartmentCertificationType { DepartmentId = DepartmentId };
				input.ApplyTo(type);
				await _certifications.SaveCertificationTypeAsync(type, UserId, cancellationToken);
				return Saved(nameof(Types));
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				var view = Page(new CertificationTypeEditView { Type = input, Message = ErrorText(ex.Message) });
				return View(view);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteType(int id, CancellationToken cancellationToken)
		{
			if (!CanSetup)
				return Unauthorized();
			var type = await _certifications.GetCertificationTypeByIdAsync(id);
			if (type == null || type.DepartmentId != DepartmentId)
				return NotFound();
			try
			{
				await _certifications.DeleteCertificationTypeByIdAsync(id, cancellationToken);
				return Saved(nameof(Types));
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(Types));
			}
		}

		/// <summary>Adds one or more gallery templates to the department (plan D1.2). Duplicates by code are reported, not fatal.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddFromTemplate(string[] templateIds, CancellationToken cancellationToken)
		{
			if (!CanSetup)
				return Unauthorized();
			var added = 0; var skipped = new List<string>();
			foreach (var templateId in (templateIds ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct())
			{
				try { await _certifications.CreateCertificationTypeFromTemplateAsync(DepartmentId, templateId, UserId, cancellationToken); added++; }
				catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { skipped.Add(CertificationTypeTemplateCatalog.GetById(templateId)?.Code ?? templateId); }
			}
			if (skipped.Count > 0)
				TempData["CertificationsMessage"] = string.Format(_strings["TemplatesSkipped"].Value, added, string.Join(", ", skipped));
			else
				TempData["CertificationsSaved"] = true;
			return RedirectToAction(nameof(Types));
		}

		#endregion

		#region Settings

		[HttpGet]
		public async Task<IActionResult> Settings()
		{
			if (!CanSetup)
				return Unauthorized();
			var view = Page(new CertificationSettingsView
			{
				Settings = CertificationSettingsInput.From(await _certifications.GetCertificationSettingsAsync(DepartmentId)),
				MandatoryRequirementCount = (await _certifications.GetAllRoleRequirementsAsync(DepartmentId)).Count(r => r.IsMandatory)
			});
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Settings(CertificationSettingsInput input, CancellationToken cancellationToken)
		{
			if (!CanSetup)
				return Unauthorized();
			if (input == null)
				return BadRequest();
			// Enforce removes members from roles; the page asks for an explicit confirmation before it can be chosen.
			if (input.EnforcementMode == (int)CertificationEnforcementModes.Enforce && !input.ConfirmEnforce)
			{
				var current = await _certifications.GetCertificationSettingsAsync(DepartmentId);
				if (current.EnforcementMode != (int)CertificationEnforcementModes.Enforce)
					return View(Page(new CertificationSettingsView { Settings = input, Message = _strings["certifications_enforce_confirm"].Value }));
			}
			try
			{
				await _certifications.SaveCertificationSettingsAsync(new DepartmentCertificationSettings
				{
					DepartmentId = DepartmentId, EnforcementMode = input.EnforcementMode, RoleRemovalGraceDays = input.RoleRemovalGraceDays, NotifyLeadDaysCsv = input.NotifyLeadDaysCsv,
					NotifyCertificationHolder = input.NotifyCertificationHolder, TreatPendingVerificationAsValid = input.TreatPendingVerificationAsValid, SendAdminDigest = input.SendAdminDigest
				}, UserId, cancellationToken);
				return Saved(nameof(Settings));
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return View(Page(new CertificationSettingsView { Settings = input, Message = ErrorText(ex.Message) }));
			}
		}

		#endregion

		#region Role requirements

		[HttpGet]
		public async Task<IActionResult> RoleRequirements(int roleId)
		{
			if (!CanSetup && !CanView)
				return Unauthorized();
			var role = await _roles.GetRoleByIdAsync(roleId);
			if (role == null || role.DepartmentId != DepartmentId)
				return NotFound();
			var view = Page(new RoleRequirementsView
			{
				Role = role,
				Types = await _certifications.GetActiveCertificationTypesAsync(DepartmentId, CertificationAppliesTo.Person),
				Requirements = await _certifications.GetRoleRequirementsAsync(roleId),
				Evaluations = await _certifications.EvaluateRoleRequirementsAsync(DepartmentId, roleId),
				MemberNames = await PersonnelNamesAsync(),
				EnforcementMode = (await _certifications.GetCertificationSettingsAsync(DepartmentId)).EnforcementMode
			});
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RoleRequirements(int roleId, List<RoleRequirementRowInput> requirements, CancellationToken cancellationToken)
		{
			if (!CanSetup)
				return Unauthorized();
			try
			{
				var rows = (requirements ?? new List<RoleRequirementRowInput>()).Where(r => r != null && r.TypeId > 0)
					.Select(r => new PersonnelRoleCertificationRequirement { PersonnelRoleId = roleId, DepartmentId = DepartmentId, DepartmentCertificationTypeId = r.TypeId, IsMandatory = r.IsMandatory, AnyOfGroup = r.AnyOfGroup, AllowTrainee = r.AllowTrainee, GraceDaysOverride = r.GraceDaysOverride }).ToList();
				await _certifications.SaveRoleRequirementsAsync(DepartmentId, roleId, rows, UserId, cancellationToken);
				return Saved(nameof(RoleRequirements), new { roleId });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(RoleRequirements), new { roleId });
			}
		}

		#endregion

		#region Unit records

		[HttpGet]
		public async Task<IActionResult> Unit(int unitId)
		{
			if (!CanView)
				return Unauthorized();
			var unit = await _units.GetUnitByIdAsync(unitId);
			if (unit == null || unit.DepartmentId != DepartmentId)
				return NotFound();
			var view = Page(new UnitCertificationsView
			{
				Unit = unit,
				Records = await _certifications.GetUnitCertificationsAsync(unitId),
				Types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).Where(t => !t.IsDeleted && t.IsUnitScoped).OrderBy(t => t.Type).ToList()
			});
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveUnitCertification(UnitCertificationInput input, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			if (!CanManage)
				return Unauthorized();
			if (input == null || input.UnitId <= 0)
				return BadRequest();
			var upload = await ReadUploadAsync(fileToUpload, cancellationToken);
			if (upload.Error != null)
				return Refused(400, upload.Error, nameof(Unit), new { unitId = input.UnitId });
			try
			{
				await _certifications.SaveUnitCertificationAsync(new UnitCertification
				{
					UnitCertificationId = input.UnitCertificationId, UnitId = input.UnitId, DepartmentId = DepartmentId, DepartmentCertificationTypeId = input.DepartmentCertificationTypeId,
					Number = input.Number, IssuedBy = input.IssuedBy, IssuedOn = input.IssuedOn, ExpiresOn = input.ExpiresOn, Notes = input.Notes,
					Data = upload.Data, FileName = upload.FileName, FileType = upload.FileType
				}, UserId, cancellationToken);
				return Saved(nameof(Unit), new { unitId = input.UnitId });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(Unit), new { unitId = input.UnitId });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetUnitStatus(int id, int status, string reason, CancellationToken cancellationToken)
		{
			if (!CanManage)
				return Unauthorized();
			var row = await _certifications.GetUnitCertificationByIdAsync(id);
			if (row == null || row.DepartmentId != DepartmentId)
				return NotFound();
			if (!Enum.IsDefined(typeof(UnitCertificationStatuses), status))
				return Refused(400, "certifications_status_invalid", nameof(Unit), new { unitId = row.UnitId });
			try
			{
				await _certifications.SetUnitCertificationStatusAsync(id, DepartmentId, (UnitCertificationStatuses)status, reason, UserId, cancellationToken);
				return Saved(nameof(Unit), new { unitId = row.UnitId });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(Unit), new { unitId = row.UnitId });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteUnitCertification(int id, CancellationToken cancellationToken)
		{
			if (!CanManage)
				return Unauthorized();
			var row = await _certifications.GetUnitCertificationByIdAsync(id);
			if (row == null || row.DepartmentId != DepartmentId)
				return NotFound();
			await _certifications.DeleteUnitCertificationAsync(id, DepartmentId, UserId, cancellationToken);
			return Saved(nameof(Unit), new { unitId = row.UnitId });
		}

		[HttpGet]
		public async Task<IActionResult> UnitCertificationFile(int id)
		{
			if (!CanView)
				return Unauthorized();
			var row = await _certifications.GetUnitCertificationByIdAsync(id, includeData: true);
			if (row == null || row.DepartmentId != DepartmentId)
				return NotFound();
			if (row.Data == null || row.Data.Length == 0 || ProtectedDataEnvelope.HasEnvelopePrefix(row.FileName))
				return NotFound();
			return File(row.Data, string.IsNullOrWhiteSpace(row.FileType) ? "application/octet-stream" : row.FileType, string.IsNullOrWhiteSpace(row.FileName) ? "certificate" : row.FileName);
		}

		#endregion

		#region Personnel record page

		private async Task<PersonnelCertification> AuthorizedRecordAsync(int id, bool write)
		{
			var record = await _certifications.GetCertificationByIdAsync(id);
			if (record == null || record.IsDeleted || record.DepartmentId != DepartmentId)
				return null;
			var self = string.Equals(record.UserId, UserId, StringComparison.OrdinalIgnoreCase);
			if (!self && !(write ? CanManage : CanView))
				return null;
			return record;
		}

		[HttpGet]
		public async Task<IActionResult> Record(int id)
		{
			var record = await AuthorizedRecordAsync(id, false);
			if (record == null)
				return Unauthorized();
			record.Data = null;
			try { await _protectedRead.ResolveCertificationsForReadAsync(DepartmentId, new[] { record }, Request.Headers["X-Resgrid-Protected-Grant"].ToString(), UserId); }
			catch (Exception ex) { Framework.Logging.LogException(ex, "Protected certification could not be resolved for the record page."); }
			var type = record.IsTyped ? await _certifications.GetCertificationTypeByIdAsync(record.DepartmentCertificationTypeId.Value) : null;
			var names = await PersonnelNamesAsync();
			var view = Page(new CertificationRecordView
			{
				Record = record,
				Type = type,
				IsSelf = string.Equals(record.UserId, UserId, StringComparison.OrdinalIgnoreCase),
				HolderName = names.TryGetValue(record.UserId, out var holder) ? holder : record.UserId,
				VerifiedByName = record.VerifiedByUserId != null && names.TryGetValue(record.VerifiedByUserId, out var verifier) ? verifier : record.VerifiedByUserId,
				Credits = await _certifications.GetCertificationCreditsAsync(id),
				DaysUntilExpiry = CertificationRequirementEvaluator.DaysUntilExpiry(record.ExpiresOn, type?.NeverExpires == true, Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today)
			});
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetStatus(int id, int status, string reason, CancellationToken cancellationToken)
		{
			if (!CanManage)
				return Unauthorized();
			if (!Enum.IsDefined(typeof(PersonnelCertificationStatuses), status))
				return Refused(400, "certifications_status_invalid", nameof(Record), new { id });
			try
			{
				await _certifications.SetCertificationStatusAsync(id, DepartmentId, (PersonnelCertificationStatuses)status, reason, UserId, cancellationToken);
				return Saved(nameof(Record), new { id });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(Record), new { id });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify(int id, CancellationToken cancellationToken)
		{
			if (!CanManage)
				return Unauthorized();
			try
			{
				await _certifications.VerifyCertificationAsync(id, DepartmentId, UserId, cancellationToken);
				return Saved(nameof(Record), new { id });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(Record), new { id });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Renew(int id, DateTime? expiresOn, string number, CancellationToken cancellationToken)
		{
			if (await AuthorizedRecordAsync(id, true) == null)
				return Unauthorized();
			try
			{
				await _certifications.RenewCertificationAsync(id, DepartmentId, expiresOn, number, UserId, cancellationToken);
				return Saved(nameof(Record), new { id });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(Record), new { id });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddCredit(CertificationCreditInput input, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			if (input == null || await AuthorizedRecordAsync(input.PersonnelCertificationId, true) == null)
				return Unauthorized();
			var upload = await ReadUploadAsync(fileToUpload, cancellationToken);
			if (upload.Error != null)
				return Refused(400, upload.Error, nameof(Record), new { id = input.PersonnelCertificationId });
			try
			{
				await _certifications.AddCertificationCreditAsync(new PersonnelCertificationCredit
				{
					PersonnelCertificationId = input.PersonnelCertificationId, DepartmentId = DepartmentId, CreditDate = input.CreditDate ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, Hours = input.Hours,
					Category = input.Category, Description = input.Description, Data = upload.Data, FileName = upload.FileName, FileType = upload.FileType
				}, UserId, cancellationToken);
				return Saved(nameof(Record), new { id = input.PersonnelCertificationId });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal))
			{
				return Refused(400, ex.Message, nameof(Record), new { id = input.PersonnelCertificationId });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteCredit(int id, CancellationToken cancellationToken)
		{
			if (!CanManage)
				return Unauthorized();
			var credit = await _certifications.GetCertificationCreditByIdAsync(id);
			if (credit == null || credit.DepartmentId != DepartmentId)
				return NotFound();
			await _certifications.DeleteCertificationCreditAsync(id, DepartmentId, UserId, cancellationToken);
			return Saved(nameof(Record), new { id = credit.PersonnelCertificationId });
		}

		[HttpGet]
		public async Task<IActionResult> CreditFile(int id)
		{
			var credit = await _certifications.GetCertificationCreditByIdAsync(id, includeData: true);
			if (credit == null || credit.DepartmentId != DepartmentId || await AuthorizedRecordAsync(credit.PersonnelCertificationId, false) == null)
				return Unauthorized();
			if (credit.Data == null || credit.Data.Length == 0 || ProtectedDataEnvelope.HasEnvelopePrefix(credit.FileName))
				return NotFound();
			return File(credit.Data, string.IsNullOrWhiteSpace(credit.FileType) ? "application/octet-stream" : credit.FileType, string.IsNullOrWhiteSpace(credit.FileName) ? "credit" : credit.FileName);
		}

		#endregion
	}
}
