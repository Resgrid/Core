using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Enhanced AI add-on entitlement gate (enhanced-ai-addon-plan.md §5), cloned from BusinessOperationsAccessService. Fails closed on any error.</summary>
	public class EnhancedAiAccessService : IEnhancedAiAccessService
	{
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentSettingsService _settings;
		private readonly ISubscriptionsService _subscriptions;
		private readonly IDepartmentDataProtectionService _protection;

		public EnhancedAiAccessService(IFeatureToggleService flags, IDepartmentSettingsService settings, ISubscriptionsService subscriptions,
			IDepartmentDataProtectionService protection)
		{
			_flags = flags;
			_settings = settings;
			_subscriptions = subscriptions;
			_protection = protection;
		}

		public async Task<bool> IsEnabledAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;
			try
			{
				if ((await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiEnhanced, departmentId))?.IsEnabled != true)
					return false;
				var settings = await _settings.GetDepartmentModuleSettingsAsync(departmentId, bypassCache: true);
				return settings != null && !settings.AiDisabled;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> CanUseAsync(int departmentId, string capabilityFlag)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(capabilityFlag))
				return false;

			try
			{
				// Each capability flag carries a FeatureFlagPrerequisite on Ai.Enhanced (M0238), so a fresh evaluation of
				// the child already covers the master; the explicit master check keeps the kill switch effective even
				// where a prerequisite row was never seeded.
				if ((await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiEnhanced, departmentId))?.IsEnabled != true)
					return false;
				if ((await _flags.EvaluateFreshAsync(capabilityFlag, departmentId))?.IsEnabled != true)
					return false;

				var settings = await _settings.GetDepartmentModuleSettingsAsync(departmentId, bypassCache: true);
				if (settings == null || settings.AiDisabled)
					return false;

				return await HasActiveAddonAsync(departmentId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> HasActiveAddonAsync(int departmentId) => await GetActiveAddonStateAsync(departmentId) == true;

		public async Task<OwnLlmProviderStatus> GetOwnLlmProviderStatusAsync(int departmentId)
		{
			if (departmentId <= 0)
				return OwnLlmProviderStatus.Unknown;

			// Advanced Data Protection keeps protected fields inside Resgrid, and a department's own provider is outside it.
			// Any state but Disabled blocks: a queued enrollment is a decision to protect, and the offboarding decrypt still
			// holds protected data. This applies on open-source installs too.
			try
			{
				if (await _protection.GetStateAsync(departmentId, bypassCache: true) != DepartmentDataProtectionState.Disabled)
					return OwnLlmProviderStatus.DataProtectionEnabled;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return OwnLlmProviderStatus.Unknown;
			}

			// SystemBehaviorConfig.BillingApiBaseUrl is documented as never set on an open-source install.
			if (!IsBillingConfigured())
				return OwnLlmProviderStatus.Allowed;

			return await GetActiveAddonStateAsync(departmentId) switch
			{
				true => OwnLlmProviderStatus.Allowed,
				false => OwnLlmProviderStatus.AddonRequired,
				null => OwnLlmProviderStatus.Unknown
			};
		}

		private static bool IsBillingConfigured() =>
			!string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) &&
			!string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey);

		public async Task<bool?> GetActiveAddonStateAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			// Generic billing helpers synthesize free forever PTT payments when billing is unconfigured.
			// A paid add-on must not grant access through that fallback (Readiness Pro precedent).
			if (!IsBillingConfigured())
				return false;

			try
			{
				var plans = await _subscriptions.GetAllAddonPlansByTypeAsync(PlanAddonTypes.EnhancedAi);
				var ids = plans?.Where(x => x != null && x.AddonType == (int)PlanAddonTypes.EnhancedAi &&
					!string.IsNullOrWhiteSpace(x.PlanAddonId)).Select(x => x.PlanAddonId).Distinct().ToList();
				if (ids == null || ids.Count == 0)
					return null;

				// No entitlement cache: cancellation and renewal take effect on the next request.
				var payments = await _subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, ids);
				if (payments == null)
					return null;
				var now = DateTime.UtcNow;
				return payments.Any(x => x != null && x.DepartmentId == departmentId &&
					ids.Contains(x.PlanAddonId) && x.EffectiveOn != default && x.EffectiveOn <= now && x.EndingOn > now &&
					!string.Equals(x.TransactionId, "SYSTEM", StringComparison.OrdinalIgnoreCase) &&
					x.EndingOn != DateTime.MaxValue);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return null;
			}
		}
	}
}
