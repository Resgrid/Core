using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac.Features.Indexed;
using Resgrid.Framework;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard
{
	/// <summary>
	/// Runs one widget provider (home-dashboard-redesign-plan.md §4.2, D3): looks up the definition, re-checks its gates for
	/// this call, validates settings, invokes the keyed provider and turns failures into a widget-level error so one widget
	/// never breaks another. Only the requested provider is constructed (IIndex).
	/// </summary>
	public class DashboardWidgetDataService : IDashboardWidgetDataService
	{
		private readonly IIndex<string, IDashboardWidgetDataProvider> _providers;
		private readonly IDashboardEntitlementService _entitlementService;

		public DashboardWidgetDataService(IIndex<string, IDashboardWidgetDataProvider> providers, IDashboardEntitlementService entitlementService)
		{
			_providers = providers;
			_entitlementService = entitlementService;
		}

		public Task<DashboardWidgetDataResult> GetDataAsync(DashboardViewer viewer, DashboardLayoutWidget instance, string protectedGrantToken,
			CancellationToken cancellationToken)
		{
			if (instance == null)
				return Task.FromResult(Error(DashboardWidgetErrorCodes.NotFound, "WidgetErrorNotFound"));

			return RunAsync(viewer, instance.Key, instance.Settings, instance.Size, protectedGrantToken, cancellationToken);
		}

		public Task<DashboardWidgetDataResult> GetPreviewAsync(DashboardViewer viewer, string key, IDictionary<string, object> settings, string size,
			string protectedGrantToken, CancellationToken cancellationToken)
		{
			return RunAsync(viewer, key, settings, size, protectedGrantToken, cancellationToken);
		}

		private async Task<DashboardWidgetDataResult> RunAsync(DashboardViewer viewer, string key, IDictionary<string, object> settings, string size,
			string protectedGrantToken, CancellationToken cancellationToken)
		{
			var definition = DashboardWidgetCatalog.Find(key);
			if (definition == null || viewer == null)
				return Error(DashboardWidgetErrorCodes.NotFound, "WidgetErrorNotFound");

			await _entitlementService.CompleteViewerForWidgetAsync(viewer, definition);
			if (!_entitlementService.IsVisible(definition, viewer))
				return Error(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

			if (!_providers.TryGetValue(definition.Key, out var provider))
				return Error(DashboardWidgetErrorCodes.NotFound, "WidgetErrorNotFound");

			var context = new DashboardWidgetContext
			{
				DepartmentId = viewer.DepartmentId,
				UserId = viewer.UserId,
				Viewer = viewer,
				Definition = definition,
				Settings = DashboardLayoutValidator.WithDefaults(definition, settings),
				Size = definition.AllowedSizes.Contains(size) ? size : definition.DefaultSize,
				NowUtc = DateTime.UtcNow,
				ProtectedGrantToken = viewer.AdpEnforced ? protectedGrantToken : null
			};

			try
			{
				var data = await provider.GetDataAsync(context, cancellationToken);
				return new DashboardWidgetDataResult { Data = data, GeneratedUtc = context.NowUtc };
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (DashboardWidgetStateException state)
			{
				return Error(state.Code, state.MessageKey);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dashboard widget {definition.Key} failed for department {viewer.DepartmentId}");
				return Error(DashboardWidgetErrorCodes.Failed, "WidgetErrorFailed");
			}
		}

		private static DashboardWidgetDataResult Error(string code, string messageKey) =>
			new DashboardWidgetDataResult { GeneratedUtc = DateTime.UtcNow, Error = new DashboardWidgetError { Code = code, MessageKey = messageKey } };
	}
}
