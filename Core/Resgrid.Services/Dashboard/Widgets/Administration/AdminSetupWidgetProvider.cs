using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Services;
using Labels = Resgrid.Localization.Areas.User.AdminAssist.AdminAssist;

namespace Resgrid.Services.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.setup (plan §7.11): the service behind v4 AdminAssist/Overview (IAdminAssistService.GetOverviewAsync), in setup mode when
	/// the Setup Wizard is available to the admin, else the workspace — the same access check the overview enforces. The overview is
	/// the module's own aggregate (an evidence snapshot), hence the hour-long default refresh. Finding subject names are never read.
	/// </summary>
	[DashboardWidget("admin.setup")]
	public class AdminSetupWidgetProvider : IDashboardWidgetDataProvider
	{
		public const string WizardUrl = "/User/AdminAssist/SetupWizard";

		private static readonly HashSet<string> DoneStates = new HashSet<string>(StringComparer.Ordinal) { "Learned", "AccessAvailable", "ConfigurationPresent", "ChecksPassed" };

		private readonly IAdminAssistService _adminAssistService;
		private readonly IAdminAssistAccessService _adminAssistAccessService;
		private readonly IAdminAssistCatalog _catalog;
		private readonly IStringLocalizer<Labels> _labels;

		public AdminSetupWidgetProvider(IAdminAssistService adminAssistService, IAdminAssistAccessService adminAssistAccessService, IAdminAssistCatalog catalog,
			IStringLocalizer<Labels> labels)
		{
			_adminAssistService = adminAssistService;
			_adminAssistAccessService = adminAssistAccessService;
			_catalog = catalog;
			_labels = labels;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var actor = new AdminAssistActor(context.DepartmentId, context.UserId, CultureInfo.CurrentUICulture.Name);
			var setup = await _adminAssistAccessService.CanAccessAsync(actor, true, cancellationToken);
			if (!setup && !await _adminAssistAccessService.CanAccessAsync(actor, false, cancellationToken))
				throw new DashboardWidgetStateException(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

			var overview = await _adminAssistService.GetOverviewAsync(actor, setup, cancellationToken);
			var report = overview?.Report;
			var tasks = overview?.SetupPlan?.Tasks ?? Array.Empty<SetupTask>();
			var planned = tasks.Select(t => t.CapabilityId).ToHashSet(StringComparer.Ordinal);
			var next = tasks.FirstOrDefault(t => t.State == "Pending");

			var required = report?.Required ?? 0;
			var verified = report?.Verified ?? 0;
			return new AdminSetupWidgetData
			{
				SetupMode = setup,
				ChecksRequired = required,
				ChecksVerified = verified,
				ChecksFailed = report?.Failed ?? 0,
				ChecksUnknown = report?.Unknown ?? 0,
				PercentComplete = required == 0 ? 100 : (int)Math.Round(verified * 100.0 / required),
				TasksTotal = tasks.Count,
				TasksDone = tasks.Count(t => DoneStates.Contains(t.State ?? string.Empty)),
				NeedsAttention = (overview?.CapabilitySetup ?? Array.Empty<CapabilitySetupAssessment>())
					.Count(a => a.State == CapabilitySetupState.NeedsAttention && planned.Contains(a.CapabilityId)),
				NextLabel = next == null ? null : Label(next.CapabilityId),
				NextUrl = next != null && IsLocal(next.Destination) ? next.Destination : WizardUrl,
				RevisitDue = overview?.SetupPlan?.RevisitDue == true,
				RevisitOnUtc = overview?.SetupPlan?.RevisitOnUtc
			};
		}

		private string Label(string capabilityId)
		{
			var key = _catalog.Capabilities?.FirstOrDefault(c => c.Id == capabilityId)?.LabelKey;
			if (string.IsNullOrWhiteSpace(key))
				return null;

			var text = _labels[key];
			return text == null || text.ResourceNotFound ? null : text.Value;
		}

		/// <summary>A catalog destination is an app-relative path; anything else falls back to the wizard.</summary>
		private static bool IsLocal(string url) =>
			!string.IsNullOrWhiteSpace(url) && url.StartsWith("/", StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
	}
}
