using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ReadinessAccessService : IReadinessAccessService
	{
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentSettingsService _settings;
		private readonly ISubscriptionsService _subscriptions;

		public ReadinessAccessService(IFeatureToggleService flags, IDepartmentSettingsService settings,
			ISubscriptionsService subscriptions)
		{
			_flags = flags;
			_settings = settings;
			_subscriptions = subscriptions;
		}

		public async Task<bool> CanUseChecklistsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				if ((await _flags.EvaluateFreshAsync(FeatureFlagKeys.ChecklistsSystem, departmentId))?.IsEnabled != true)
					return false;

				var settings = await _settings.GetDepartmentModuleSettingsAsync(departmentId, true);
				return settings != null && !settings.ChecklistsDisabled;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> CanUseMaintenanceAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				if (!await _flags.IsEnabledAsync(FeatureFlagKeys.MaintenanceWorkOrders, departmentId))
					return false;

				var settings = await _settings.GetDepartmentModuleSettingsAsync(departmentId);
				if (settings == null || settings.MaintenanceDisabled)
					return false;

				// Generic billing helpers synthesize free forever PTT payments when billing is
				// unconfigured. Readiness Pro must not grant paid access through that fallback.
				if (string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) ||
					string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
					return false;

				var plans = await _subscriptions.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ReadinessPro);
				var ids = plans?.Where(x => x != null && x.AddonType == (int)PlanAddonTypes.ReadinessPro &&
					!string.IsNullOrWhiteSpace(x.PlanAddonId)).Select(x => x.PlanAddonId).Distinct().ToList();
				if (ids == null || ids.Count == 0)
					return false;

				// No entitlement cache: cancellation and renewal take effect on the next write.
				var payments = await _subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, ids);
				var now = DateTime.UtcNow;
				return payments != null && payments.Any(x => x != null && x.DepartmentId == departmentId &&
					ids.Contains(x.PlanAddonId) && x.EffectiveOn != default && x.EffectiveOn <= now && x.EndingOn > now &&
					!string.Equals(x.TransactionId, "SYSTEM", StringComparison.OrdinalIgnoreCase) &&
					x.EndingOn != DateTime.MaxValue);
				// IsCancelled represents renewal cancellation in the shared model; access lasts
				// through EndingOn. Immediate revocation must shorten EndingOn at reconciliation.
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}
	}
}
