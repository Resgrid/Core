using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Departments.CallSettings;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Department → Call Settings → Updates from your CAD (cad-email-call-updates-plan.md): how an inbound dispatch email carrying the
	/// CAD call ID of a call the department already has updates, closes or is noted on that call, the labels of the Labeled Fields
	/// email format, and a test box that reads a pasted email without creating anything. Department admins only, like Call Settings.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class CadUpdatesController : SecureBaseController
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEmailCallUpdateService _emailCallUpdateService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Department.CadUpdates> _localizer;

		public CadUpdatesController(IDepartmentSettingsService departmentSettingsService, IDepartmentsService departmentsService,
			IEmailCallUpdateService emailCallUpdateService, IStringLocalizer<Resgrid.Localization.Areas.User.Department.CadUpdates> localizer)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_emailCallUpdateService = emailCallUpdateService;
			_localizer = localizer;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Index()
		{
			var format = await FormatTypeAsync();
			var stored = await _departmentSettingsService.GetStoredEmailCallUpdateConfigAsync(DepartmentId, true);
			var effective = stored ?? EmailCallUpdatePresets.ForFormat(format) ?? new EmailCallUpdateConfig();
			var labels = await _departmentSettingsService.GetEmailLabeledFieldsConfigAsync(DepartmentId, true);

			var model = CadUpdatesView.From(effective, labels, format);
			model.Message = TempData["CadUpdatesMessage"] as string;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Index(CadUpdatesView model, CancellationToken cancellationToken)
		{
			if (model == null)
				return RedirectToAction("Index");

			var format = await FormatTypeAsync();
			var config = model.ToConfig();
			var labels = model.ToLabels();

			var error = config.Validate();
			if (error != null || !ModelState.IsValid)
			{
				var view = CadUpdatesView.From(config, labels, format);
				view.ErrorMessage = error != null ? _localizer[error].Value : _localizer["SettingsInvalid"].Value;
				return View(view);
			}

			await _departmentSettingsService.SetEmailCallUpdateConfigAsync(DepartmentId, config, cancellationToken);
			await _departmentSettingsService.SetEmailLabeledFieldsConfigAsync(DepartmentId, labels, cancellationToken);

			TempData["CadUpdatesMessage"] = _localizer["Saved"].Value;
			return RedirectToAction("Index");
		}

		/// <summary>Removes the saved settings, so a format with a built-in behaviour goes back to it (and any other format to off).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> UseBuiltIn(CancellationToken cancellationToken)
		{
			await _departmentSettingsService.DeleteSettingAsync(DepartmentId, DepartmentSettingTypes.EmailCallUpdates, cancellationToken);
			TempData["CadUpdatesMessage"] = _localizer["Saved"].Value;
			return RedirectToAction("Index");
		}

		/// <summary>The test box: what the department's email format reads from a pasted email under the page's (unsaved) settings.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Preview(CadUpdatesPreviewInput input, CancellationToken cancellationToken)
		{
			if (input == null || (string.IsNullOrWhiteSpace(input.TestSubject) && string.IsNullOrWhiteSpace(input.TestBody)))
				return BadRequest();

			var subject = Cap(input.TestSubject, 1000);
			var body = Cap(input.TestBody, 20000);
			var format = await FormatTypeAsync();
			var preview = await _emailCallUpdateService.PreviewAsync(DepartmentId, format, subject, body, input.ToConfig(), input.ToLabels(), cancellationToken);
			return Json(preview);
		}

		private async Task<int> FormatTypeAsync()
		{
			var emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(DepartmentId);
			return emailSettings?.FormatType > 0 ? emailSettings.FormatType : (int)CallEmailTypes.Generic;
		}

		private static string Cap(string value, int length)
		{
			if (string.IsNullOrEmpty(value))
				return value;
			return value.Length > length ? value.Substring(0, length) : value;
		}
	}
}
