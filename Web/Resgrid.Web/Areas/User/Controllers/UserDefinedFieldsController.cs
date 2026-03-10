using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.UserDefinedFields;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class UserDefinedFieldsController : SecureBaseController
	{
		private static readonly UdfEntityType[] _entityTypes =
		{
			UdfEntityType.Call,
			UdfEntityType.Personnel,
			UdfEntityType.Unit,
			UdfEntityType.Contact,
		};

		private readonly IUserDefinedFieldsService _udfService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields> _localizer;

		public UserDefinedFieldsController(
			IUserDefinedFieldsService udfService,
			IEventAggregator eventAggregator,
			IStringLocalizer<Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields> localizer)
		{
			_udfService        = udfService;
			_eventAggregator   = eventAggregator;
			_localizer         = localizer;
		}

		// ── Index ─────────────────────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.Udf_View)]
		public async Task<IActionResult> Index(CancellationToken ct)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Dashboard", "Home");

			var summaries = new List<UdfEntitySummaryModel>();

			foreach (var entityType in _entityTypes)
			{
				var definition = await _udfService.GetActiveDefinitionAsync(DepartmentId, (int)entityType);
				int fieldCount = 0;

				if (definition != null)
				{
					var fields = await _udfService.GetFieldsForActiveDefinitionAsync(DepartmentId, (int)entityType);
					fieldCount = fields?.Count ?? 0;
				}

				summaries.Add(new UdfEntitySummaryModel
				{
					EntityType       = entityType,
					EntityTypeName   = entityType.ToString(),
					ActiveDefinition = definition,
					FieldCount       = fieldCount,
				});
			}

			return View(new UdfIndexModel { Entities = summaries });
		}

		// ── Preview ───────────────────────────────────────────────────────────────

		/// <summary>
		/// Returns a partial view that renders the active UDF fields for the given entity type as
		/// disabled form controls so users can see how they will look. Nothing is saved.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Udf_View)]
		public async Task<IActionResult> Preview(int entityType, CancellationToken ct)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Forbid();

			// Preview is the admin-facing design preview; show all fields regardless of visibility.
			var fields = await _udfService.GetFieldsForActiveDefinitionAsync(DepartmentId, entityType) ?? new List<UdfField>();
			var parsedEntityType = (UdfEntityType)entityType;

			var model = new UdfPreviewModel
			{
				EntityType          = parsedEntityType,
				EntityTypeName      = parsedEntityType.ToString(),
				Fields              = fields,
				PreviewWarningTitle = _localizer["PreviewWarningTitle"].Value,
				PreviewWarningBody  = _localizer["PreviewWarningBody"].Value,
				NoFieldsMessage     = _localizer["PreviewNoFields"].Value,
			};

			return PartialView("_UdfPreview", model);
		}

		// ── Edit / New Definition ─────────────────────────────────────────────────

		[HttpGet]
		[Authorize(Policy = ResgridResources.Udf_Update)]
		public async Task<IActionResult> Edit(int entityType, CancellationToken ct)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Dashboard", "Home");

			var parsedEntityType = (UdfEntityType)entityType;
			var definition = await _udfService.GetActiveDefinitionAsync(DepartmentId, entityType);
			List<UdfField> fields = new();

			if (definition != null)
				fields = await _udfService.GetFieldsForActiveDefinitionAsync(DepartmentId, entityType) ?? new();

			var model = new UdfDefinitionEditModel
			{
				EntityType           = parsedEntityType,
				EntityTypeName       = parsedEntityType.ToString(),
				ExistingDefinitionId = definition?.UdfDefinitionId,
				Fields               = fields.Select(MapFieldToForm).ToList(),
			};

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Udf_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(UdfDefinitionEditModel model, CancellationToken ct)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Dashboard", "Home");

			model.EntityTypeName = model.EntityType.ToString();

			if (!ModelState.IsValid)
				return View(model);

			// ── Machine-name validation (client-side duplicates these; service enforces them too) ──

			var fieldForms = model.Fields ?? new List<UdfFieldFormModel>();

			// 1. Every field must have a non-empty label
			if (fieldForms.Any(f => string.IsNullOrWhiteSpace(f.Label)))
			{
				ModelState.AddModelError(string.Empty, "All fields must have a label.");
				return View(model);
			}

			// 2. Every machine name must be non-empty and match the allowed pattern
			var invalidNames = fieldForms
				.Select(f => f.Name?.Trim() ?? string.Empty)
				.Where(n => !UdfValidationHelper.IsValidMachineName(n))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (invalidNames.Any())
			{
				ModelState.AddModelError(string.Empty,
					$"Invalid machine name(s): {string.Join(", ", invalidNames.Select(n => $"'{n}'"))}. " +
					"Machine names must start with a letter or underscore and contain only letters, digits, and underscores.");
				return View(model);
			}

			// 3. Machine names must be unique within this entity type (case-insensitive)
			var names = fieldForms.Select(f => f.Name!.Trim()).ToList();
			var duplicates = names
				.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
				.Where(g => g.Count() > 1)
				.Select(g => g.Key)
				.ToList();

			if (duplicates.Any())
			{
				ModelState.AddModelError(string.Empty,
					$"Duplicate machine name(s): {string.Join(", ", duplicates.Select(d => $"'{d}'"))}. " +
					"Each field machine name must be unique within the same entity type.");
				return View(model);
			}

			var domainFields = fieldForms.Select((f, i) => MapFormToField(f, i)).ToList();
			var isNew = string.IsNullOrWhiteSpace(model.ExistingDefinitionId);

			try
			{
				var saved = await _udfService.SaveDefinitionAsync(DepartmentId, (int)model.EntityType, domainFields, UserId, ct);

				_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = DepartmentId,
					UserId       = UserId,
					Type         = isNew ? AuditLogTypes.UdfDefinitionCreated : AuditLogTypes.UdfDefinitionUpdated,
					After        = JsonSerializer.Serialize(new
					{
						saved.UdfDefinitionId,
						EntityType = model.EntityType.ToString(),
						FieldCount = domainFields.Count
					}),
					Successful   = true,
					IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
					ServerName   = Environment.MachineName,
					UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}",
				});
			}
			catch (InvalidOperationException ex)
			{
				// Service-layer domain rule violations (e.g. duplicate names from a non-web caller)
				ModelState.AddModelError(string.Empty, ex.Message);
				return View(model);
			}

			TempData["SuccessMessage"] = "DefinitionSaved";
			return RedirectToAction("Index");
		}

		// ── Delete ────────────────────────────────────────────────────────────────

		[HttpPost]
		[Authorize(Policy = ResgridResources.Udf_Delete)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(int entityType, CancellationToken ct)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Dashboard", "Home");

			// Save an empty field set, which creates a new version with no fields and marks
			// the previous definition inactive — effectively "deleting" the configuration.
			await _udfService.SaveDefinitionAsync(DepartmentId, entityType, new List<UdfField>(), UserId, ct);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId       = UserId,
				Type         = AuditLogTypes.UdfDefinitionDeleted,
				Before       = JsonSerializer.Serialize(new { EntityType = ((UdfEntityType)entityType).ToString() }),
				Successful   = true,
				IpAddress    = IpAddressHelper.GetRequestIP(Request, true),
				ServerName   = Environment.MachineName,
				UserAgent    = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}",
			});

			TempData["SuccessMessage"] = "DefinitionDeleted";
			return RedirectToAction("Index");
		}

		// ── Mapping helpers ───────────────────────────────────────────────────────

		private static UdfFieldFormModel MapFieldToForm(UdfField field)
		{
			UdfValidationRules? rules = null;
			if (!string.IsNullOrWhiteSpace(field.ValidationRules))
			{
				try { rules = JsonSerializer.Deserialize<UdfValidationRules>(field.ValidationRules); }
				catch { /* ignore */ }
			}

			string? optionsRaw = null;
			if (rules?.Options != null && rules.Options.Count > 0)
				optionsRaw = string.Join("\n", rules.Options.Select(o => $"{o.Key}={o.Label}"));

			return new UdfFieldFormModel
			{
				UdfFieldId          = field.UdfFieldId,
				Name                = field.Name,
				Label               = field.Label,
				Description         = field.Description,
				Placeholder         = field.Placeholder,
				FieldDataType       = field.FieldDataType,
				IsRequired          = field.IsRequired,
				IsReadOnly          = field.IsReadOnly,
				IsEnabled           = field.IsEnabled,
				IsVisibleOnMobile   = field.IsVisibleOnMobile,
				IsVisibleOnReports  = field.IsVisibleOnReports,
				Visibility          = field.Visibility,
				DefaultValue        = field.DefaultValue,
				GroupName           = field.GroupName,
				SortOrder           = field.SortOrder,
				MinLength           = rules?.MinLength,
				MaxLength           = rules?.MaxLength,
				MinValue            = rules?.MinValue,
				MaxValue            = rules?.MaxValue,
				Regex               = rules?.Regex,
				RegexErrorMessage   = rules?.RegexErrorMessage,
				DropdownOptionsRaw  = optionsRaw,
			};
		}

		private static UdfField MapFormToField(UdfFieldFormModel form, int sortOrder)
		{
			var rules = new UdfValidationRules
			{
				MinLength        = form.MinLength,
				MaxLength        = form.MaxLength,
				MinValue         = form.MinValue,
				MaxValue         = form.MaxValue,
				Regex            = form.Regex,
				RegexErrorMessage = form.RegexErrorMessage,
			};

			if (!string.IsNullOrWhiteSpace(form.DropdownOptionsRaw))
			{
				rules.Options = form.DropdownOptionsRaw
					.Split('\n', StringSplitOptions.RemoveEmptyEntries)
					.Select(line =>
					{
						var idx = line.IndexOf('=');
						if (idx <= 0) return new UdfDropdownOption { Key = line.Trim(), Label = line.Trim() };
						return new UdfDropdownOption
						{
							Key   = line[..idx].Trim(),
							Label = line[(idx + 1)..].Trim(),
						};
					})
					.ToList();
			}

			bool hasValidation = rules.MinLength.HasValue || rules.MaxLength.HasValue ||
				rules.MinValue.HasValue || rules.MaxValue.HasValue ||
				!string.IsNullOrWhiteSpace(rules.Regex) || rules.Options.Count > 0;

			return new UdfField
			{
				UdfFieldId         = form.UdfFieldId,
				Name               = form.Name?.Trim() ?? string.Empty,
				Label              = form.Label?.Trim() ?? string.Empty,
				Description        = form.Description,
				Placeholder        = form.Placeholder,
				FieldDataType      = form.FieldDataType,
				IsRequired         = form.IsRequired,
				IsReadOnly         = form.IsReadOnly,
				IsEnabled          = form.IsEnabled,
				IsVisibleOnMobile  = form.IsVisibleOnMobile,
				IsVisibleOnReports = form.IsVisibleOnReports,
				Visibility         = form.Visibility,
				DefaultValue       = form.DefaultValue,
				GroupName          = form.GroupName,
				SortOrder          = sortOrder,
				ValidationRules    = hasValidation ? JsonSerializer.Serialize(rules) : null,
			};
		}
	}
}




