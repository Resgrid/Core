using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.AdminAssist;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	public partial class DepartmentController
	{
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> OperatingProfile()
		{
			if (!await CanEditOperatingProfileAsync()) return Forbid();
			ViewBag.DepartmentTimeZone = (await _departmentsService.GetDepartmentByIdAsync(DepartmentId, true))?.TimeZone;
			return View(await _departmentSettingsService.GetOperatingProfileAsync(DepartmentId));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(32768)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> OperatingProfile([Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever] DepartmentOperatingProfile profile, CancellationToken cancellationToken)
		{
			if (!await CanEditOperatingProfileAsync()) return Forbid();
			if (profile == null) return BadRequest();
			ViewBag.DepartmentTimeZone = (await _departmentsService.GetDepartmentByIdAsync(DepartmentId, true))?.TimeZone;
			// Empty optional rows are presentation affordances, not unknown reference identifiers.
			foreach (var list in new[] { profile.AuthoritativeSystemReferences, profile.SiteGroupReferences, profile.StaffingPolicyReferences,
				profile.QualificationPolicyReferences, profile.ContinuityProcedureReferences }) list?.RemoveAll(string.IsNullOrWhiteSpace);
			if (!ModelState.IsValid || !TryValidateModel(profile))
			{
				// Preserve binding errors, but render malformed null collections safely.
				profile.Archetypes ??= new(); profile.LanguageCodes ??= new(); profile.AccessibilityNeeds ??= new();
				profile.AuthoritativeSystemReferences ??= new(); profile.SiteGroupReferences ??= new();
				profile.StaffingPolicyReferences ??= new(); profile.QualificationPolicyReferences ??= new(); profile.ContinuityProcedureReferences ??= new();
				return View(profile);
			}
			try { await _departmentSettingsService.SetOperatingProfileAsync(DepartmentId, profile, UserId, cancellationToken); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (AdminAssistConcurrencyException)
			{
				Response.StatusCode = 409; ViewBag.ProfileErrorKey = "Profile.Conflict"; return View(profile);
			}
			catch (System.ComponentModel.DataAnnotations.ValidationException)
			{
				Response.StatusCode = 400; ViewBag.ProfileErrorKey = "Profile.InvalidReferences"; return View(profile);
			}
			return RedirectToAction(nameof(OperatingProfile));
		}

		private async Task<bool> CanEditOperatingProfileAsync()
		{
			if (!await Resgrid.Services.AdminAssist.AdminAssistFeatureAvailability.CanConfigureOperatingProfileAsync(
				_featureToggleService, DepartmentId, HttpContext.RequestAborted)) return false;
			var member = await _departmentsService.GetDepartmentMemberAsync(UserId, DepartmentId, true);
			if (!Resgrid.Model.Helpers.DepartmentMemberStateHelper.IsCurrentMember(member, DepartmentId)) return false;
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, true);
			return member.IsAdmin.GetValueOrDefault() || department?.ManagingUserId == UserId;
		}
	}
}
