using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Plan decision 42 entitlement gate, cloned from ReadinessAccessService. Fails closed on any error.</summary>
	public class BusinessOperationsAccessService : IBusinessOperationsAccessService
	{
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentSettingsService _settings;
		private readonly ISubscriptionsService _subscriptions;

		public BusinessOperationsAccessService(IFeatureToggleService flags, IDepartmentSettingsService settings, ISubscriptionsService subscriptions)
		{
			_flags = flags;
			_settings = settings;
			_subscriptions = subscriptions;
		}

		public Task<bool> CanUseInvoicingAsync(int departmentId) => CanUseAsync(departmentId, FeatureFlagKeys.CustomerInvoicing);

		public Task<bool> CanUseContractorBillingAsync(int departmentId) => CanUseAsync(departmentId, FeatureFlagKeys.ContractorBilling);
		public Task<bool> CanUseCostRecoveryAsync(int departmentId) => CanUseAsync(departmentId, FeatureFlagKeys.CalOesMars);
		// The Phase E flag key is declared when that phase is authored; until then the capability is off.
		public Task<bool> CanUseWorkforceAsync(int departmentId) => CanUseAsync(departmentId, "Workforce.InternalCosting");

		private async Task<bool> CanUseAsync(int departmentId, string capabilityFlag)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				// The capability flag carries a FeatureFlagPrerequisite on Business.Operations (plan decision 11), so a
				// fresh evaluation of the child already covers the master; the explicit master check keeps the kill
				// switch effective even where a prerequisite row was never seeded.
				if ((await _flags.EvaluateFreshAsync(FeatureFlagKeys.BusinessOperations, departmentId))?.IsEnabled != true)
					return false;
				if ((await _flags.EvaluateFreshAsync(capabilityFlag, departmentId))?.IsEnabled != true)
					return false;

				var settings = await _settings.GetDepartmentModuleSettingsAsync(departmentId, bypassCache: true);
				if (settings == null || settings.BusinessOperationsDisabled)
					return false;

				return await HasActiveAddonAsync(departmentId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> HasActiveAddonAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				// Generic billing helpers synthesize free forever PTT payments when billing is unconfigured.
				// A paid add-on must not grant access through that fallback (Readiness Pro precedent).
				if (string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) ||
					string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
					return false;

				var plans = await _subscriptions.GetAllAddonPlansByTypeAsync(PlanAddonTypes.BusinessOperations);
				var ids = plans?.Where(x => x != null && x.AddonType == (int)PlanAddonTypes.BusinessOperations &&
					!string.IsNullOrWhiteSpace(x.PlanAddonId)).Select(x => x.PlanAddonId).Distinct().ToList();
				if (ids == null || ids.Count == 0)
					return false;

				// No entitlement cache: cancellation and renewal take effect on the next write (plan decision 42).
				var payments = await _subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, ids);
				var now = DateTime.UtcNow;
				return payments != null && payments.Any(x => x != null && x.DepartmentId == departmentId &&
					ids.Contains(x.PlanAddonId) && x.EffectiveOn != default && x.EffectiveOn <= now && x.EndingOn > now &&
					!string.Equals(x.TransactionId, "SYSTEM", StringComparison.OrdinalIgnoreCase) &&
					x.EndingOn != DateTime.MaxValue);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}
	}
}
