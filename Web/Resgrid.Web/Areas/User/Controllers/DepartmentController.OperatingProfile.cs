using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model.AdminAssist;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	public partial class DepartmentController
	{
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> OperatingProfile(CancellationToken cancellationToken)
		{
			if (!await CanEditOperatingProfileAsync()) return Forbid();
			await LoadOperatingProfileOptionsAsync(cancellationToken);
			return View(await _departmentSettingsService.GetOperatingProfileAsync(DepartmentId));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(32768)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> OperatingProfile([Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever] DepartmentOperatingProfile profile,
			string seasonStartMonth, string seasonStartDay, string seasonEndMonth, string seasonEndDay, CancellationToken cancellationToken)
		{
			if (!await CanEditOperatingProfileAsync()) return Forbid();
			if (profile == null) return BadRequest();
			async Task<IActionResult> RedisplayAsync()
			{
				await LoadOperatingProfileOptionsAsync(cancellationToken);
				return View(profile);
			}
			// Seasons are picked as a month and a day; the profile stores them as MM-DD.
			profile.SeasonStartMonthDay = SeasonMonthDay.Compose(seasonStartMonth, seasonStartDay);
			profile.SeasonEndMonthDay = SeasonMonthDay.Compose(seasonEndMonth, seasonEndDay);
			// Empty optional rows are presentation affordances, not unknown reference identifiers.
			foreach (var list in new[] { profile.AuthoritativeSystemReferences, profile.SiteGroupReferences, profile.StaffingPolicyReferences,
				profile.QualificationPolicyReferences, profile.ContinuityProcedureReferences }) list?.RemoveAll(string.IsNullOrWhiteSpace);
			if (!ModelState.IsValid || !TryValidateModel(profile))
			{
				// Preserve binding errors, but render malformed null collections safely.
				profile.Archetypes ??= new(); profile.LanguageCodes ??= new(); profile.AccessibilityNeeds ??= new();
				profile.AuthoritativeSystemReferences ??= new(); profile.SiteGroupReferences ??= new();
				profile.StaffingPolicyReferences ??= new(); profile.QualificationPolicyReferences ??= new(); profile.ContinuityProcedureReferences ??= new();
				return await RedisplayAsync();
			}
			try { await _departmentSettingsService.SetOperatingProfileAsync(DepartmentId, profile, UserId, cancellationToken); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (AdminAssistConcurrencyException)
			{
				Response.StatusCode = 409; ViewBag.ProfileErrorKey = "Profile.Conflict"; return await RedisplayAsync();
			}
			catch (System.ComponentModel.DataAnnotations.ValidationException)
			{
				Response.StatusCode = 400; ViewBag.ProfileErrorKey = "Profile.InvalidReferences"; return await RedisplayAsync();
			}
			TempData["OperatingProfileSaved"] = true;
			return RedirectToAction(nameof(OperatingProfile));
		}

		/// <summary>
		/// Reference pickers offer this department's groups and unexpired documents, so an administrator chooses a reference
		/// instead of typing an identifier. Protected document names resolve the same way the Documents screen resolves them.
		/// </summary>
		private async Task LoadOperatingProfileOptionsAsync(CancellationToken cancellationToken)
		{
			ViewBag.DepartmentTimeZone = (await _departmentsService.GetDepartmentByIdAsync(DepartmentId, true))?.TimeZone;
			ViewBag.AdminAssistAvailable =
				await Resgrid.Services.AdminAssist.AdminAssistFeatureAvailability.IsEnabledAsync(_featureToggleService, DepartmentId, true, cancellationToken) ||
				await Resgrid.Services.AdminAssist.AdminAssistFeatureAvailability.IsWorkspaceEnabledAsync(_featureToggleService, DepartmentId, cancellationToken);

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedThinAsync(DepartmentId) ?? new();
			ViewBag.ProfileGroups = groups
				.Select(g => new SelectListItem(string.IsNullOrWhiteSpace(g.Name) ? "#" + g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture) : g.Name,
					g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture)))
				.OrderBy(g => g.Text, StringComparer.CurrentCultureIgnoreCase).ToList();

			var documents = (await _departmentSettingsService.GetOperatingProfileDocumentOptionsAsync(DepartmentId, cancellationToken)).ToList();
			await _protectedReadService.ResolveDocumentsForReadAsync(DepartmentId, documents, null, UserId, false, cancellationToken);
			var categories = documents.Select(d => string.IsNullOrWhiteSpace(d.Category) ? string.Empty : d.Category.Trim())
				.Distinct(StringComparer.CurrentCultureIgnoreCase).ToDictionary(c => c, c => c.Length == 0 ? null : new SelectListGroup { Name = c }, StringComparer.CurrentCultureIgnoreCase);
			ViewBag.ProfileDocuments = documents
				.Select(d => new SelectListItem(string.IsNullOrWhiteSpace(d.Name) ? "#" + d.DocumentId.ToString(CultureInfo.InvariantCulture) : d.Name,
					d.DocumentId.ToString(CultureInfo.InvariantCulture)) { Group = categories[string.IsNullOrWhiteSpace(d.Category) ? string.Empty : d.Category.Trim()] })
				.OrderBy(d => d.Group == null ? 0 : 1).ThenBy(d => d.Group?.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Text, StringComparer.CurrentCultureIgnoreCase).ToList();
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
