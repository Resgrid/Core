using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model.AdminAssist;
using Labels = Resgrid.Localization.Areas.User.AdminAssist.AdminAssist;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>Deterministic department setup and configuration guidance. No configuration mutation or model calls.</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class AdminAssistController(IAdminAssistService service, IAdminAssistAccessService access,
		IAdminAssistCatalog catalog, IStringLocalizer<Labels> labels, IAdminAssistReferenceSearch referenceSearch,
		IAdminAssistWorklistService worklist, IAdminAssistMaintenanceStore maintenance, IConfigurationImpactService impacts,
		IDispatchImpactService dispatchImpacts, IPermissionImpactService permissionImpacts, IModuleImpactService moduleImpacts, ITextImportImpactService textImportImpacts, IRetentionImpactService retentionImpacts, INotificationImpactService notificationImpacts, ISecurityImpactService securityImpacts) : V4AuthenticatedApiControllerbase
	{
		private AdminAssistActor Actor => new(DepartmentId, UserId, CultureInfo.CurrentUICulture.Name);
		private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
		{
			Converters = { new JsonStringEnumConverter() }
		};

		/// <summary>Read fresh, authorized configuration evidence and setup progress.</summary>
		[HttpGet("Overview")]
		public Task<IActionResult> Overview(bool setup, CancellationToken cancellationToken) => ExecuteAsync(async () =>
			await service.GetOverviewAsync(Actor, setup, cancellationToken));

		/// <summary>Read the release-pinned public setup and feature catalog.</summary>
		[HttpGet("Catalog")]
		public Task<IActionResult> Catalog(bool setup, CancellationToken cancellationToken) => ExecuteAsync(async () =>
		{
			if (!await access.CanAccessAsync(Actor, setup, cancellationToken)) throw new UnauthorizedAccessException();
			return new
			{
				CanSetup = await access.CanAccessAsync(Actor, true, cancellationToken),
				ImpactSettings = catalog.Settings.Where(s => Resgrid.AdminAssist.ConfigurationImpactEvaluator.Supports(s.Id)).Select(s => s.Id).ToArray(),
				ModuleImpactTypes = ModuleImpactSelection.Supported,
				PermissionImpactTypes = Resgrid.Services.AdminAssist.PermissionImpactService.Supported,
				catalog.Version, catalog.Areas, catalog.Capabilities, catalog.Settings, catalog.Packs, catalog.Articles,
				Strings = labels.GetAllStrings(true).GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First().Value),
				RightToLeft = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
			};
		});

		/// <summary>Update setup choices and personal learning metadata.</summary>
		[HttpPost("Setup")]
		[RequestSizeLimit(8192)]
		public Task<IActionResult> Setup([FromBody] SetupProgressCommand command, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await service.UpdateSetupAsync(Actor, command, cancellationToken));

		/// <summary>Search the allowlisted public reference corpus.</summary>
		[HttpGet("Search")]
		public Task<IActionResult> Search(string query, bool setup, CancellationToken cancellationToken) => ExecuteAsync(async () =>
		{
			if (!await access.CanAccessAsync(Actor, setup, cancellationToken)) throw new UnauthorizedAccessException();
			return referenceSearch.Search(query, Actor.Locale);
		});

		/// <summary>Read tenant history with personal learning choices restricted to the current administrator.</summary>
		[HttpGet("History")]
		public Task<IActionResult> History(int skip, int take = 30, CancellationToken cancellationToken = default) =>
			ExecuteAsync(async () => await service.GetHistoryAsync(Actor, skip, take, cancellationToken));

		/// <summary>Read current findings through the department protection policy.</summary>
		[HttpGet("Worklist")]
		public Task<IActionResult> Worklist(CancellationToken cancellationToken) => ExecuteAsync(async () => await worklist.GetAsync(Actor, cancellationToken));

		/// <summary>Evaluate fresh evidence and persist meaningful finding transitions.</summary>
		[HttpPost("Verify")]
		[RequestSizeLimit(1024)]
		public Task<IActionResult> Verify(CancellationToken cancellationToken) => ExecuteAsync(async () =>
		{
			await worklist.VerifyAsync(Actor, cancellationToken);
			return new { verified = true };
		});

		/// <summary>Update finding ownership or review metadata without changing the rule result.</summary>
		[HttpPost("Review")]
		[RequestSizeLimit(16384)]
		public Task<IActionResult> Review([FromBody] FindingReviewCommand command, CancellationToken cancellationToken) => ExecuteAsync(async () =>
		{
			await worklist.ReviewAsync(Actor, command, cancellationToken);
			return new { saved = true };
		});

		/// <summary>Preview a scalar proposal against fresh evidence without saving configuration.</summary>
		[HttpPost("Impact")]
		[RequestSizeLimit(2048)]
		public Task<IActionResult> Impact([FromBody] ConfigurationImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await impacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>Preview base-plan headroom for proposed total personnel and unit counts without provisioning.</summary>
		[HttpPost("CapacityImpact")]
		[RequestSizeLimit(1024)]
		public Task<IActionResult> CapacityImpact([FromBody] CapacityImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await impacts.PreviewCapacityAsync(Actor, request, cancellationToken));

		/// <summary>Simulate a saved call's routes using current authorized membership and an explicit roster time; never sends.</summary>
		[HttpPost("DispatchImpact")]
		[RequestSizeLimit(2048)]
		public Task<IActionResult> DispatchImpact([FromBody] DispatchImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await dispatchImpacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>Compare the proposed permission gate for current members and resource targets; never changes access.</summary>
		[HttpPost("PermissionImpact")]
		[RequestSizeLimit(4096)]
		public Task<IActionResult> PermissionImpact([FromBody] PermissionImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await permissionImpacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>List current department roles for a permission proposal, without exposing personnel details.</summary>
		[HttpGet("PermissionRoles")]
		public Task<IActionResult> PermissionRoles(string expectedRevision, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await permissionImpacts.GetRoleOptionsAsync(Actor, expectedRevision, cancellationToken));

		/// <summary>Preview module navigation and bounded content counts without changing module availability.</summary>
		[HttpPost("ModuleImpact")]
		[RequestSizeLimit(1024)]
		public Task<IActionResult> ModuleImpact([FromBody] ModuleImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await moduleImpacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>Compare a masked sender scenario against provider-specific routing; never receives or sends a message.</summary>
		[HttpPost("TextImportImpact")]
		[RequestSizeLimit(2048)]
		public Task<IActionResult> TextImportImpact([FromBody] TextImportImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await textImportImpacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>Compare supported department security-policy gates; never changes credentials or sessions.</summary>
		[HttpPost("SecurityImpact")]
		[RequestSizeLimit(1024)]
		public Task<IActionResult> SecurityImpact([FromBody] ConfigurationImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await securityImpacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>Estimate ordinary notification channel gates for a declared future cohort scenario; never sends.</summary>
		[HttpPost("NotificationImpact")]
		[RequestSizeLimit(1024)]
		public Task<IActionResult> NotificationImpact([FromBody] NotificationImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await notificationImpacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>Preview prospective retention windows and known hold exclusions using metadata only; never purges.</summary>
		[HttpPost("RetentionImpact")]
		[RequestSizeLimit(1024)]
		public Task<IActionResult> RetentionImpact([FromBody] RetentionImpactRequest request, CancellationToken cancellationToken) =>
			ExecuteAsync(async () => await retentionImpacts.PreviewAsync(Actor, request, cancellationToken));

		/// <summary>Read personal digest preferences and the last completed worker evaluation.</summary>
		[HttpGet("Preferences")]
		public Task<IActionResult> Preferences(CancellationToken cancellationToken) => ExecuteAsync(async () =>
		{
			if (!await access.CanAccessAsync(Actor, false, cancellationToken)) throw new UnauthorizedAccessException();
			return new { DigestsAvailable = Config.AdminAssistConfig.SendAdminDigests, Preferences = await maintenance.GetPreferencesAsync(DepartmentId, UserId, cancellationToken),
				Worker = await maintenance.GetWorkerStatusAsync(DepartmentId, cancellationToken) };
		});

		/// <summary>Save the current administrator's explicit digest opt-in and quiet hours.</summary>
		[HttpPost("Preferences")]
		[RequestSizeLimit(2048)]
		public Task<IActionResult> Preferences([FromBody] AdminAssistPreferencesCommand command, CancellationToken cancellationToken) => ExecuteAsync(async () =>
		{
			if (!await access.CanAccessAsync(Actor, false, cancellationToken)) throw new UnauthorizedAccessException();
			await maintenance.SavePreferencesAsync(Actor, command, cancellationToken);
			return new { saved = true };
		});

		private async Task<IActionResult> ExecuteAsync(Func<Task<object>> action)
		{
			Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			try { return Content(JsonSerializer.Serialize(await action(), JsonOptions), "application/json"); }
			catch (UnauthorizedAccessException) { return StatusCode(403); }
			catch (AdminAssistConcurrencyException) { return Conflict(new { code = "WorkspaceChanged" }); }
			catch (ArgumentException) { return BadRequest(new { code = "InvalidSetupChoice" }); }
		}
	}
}
