using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// The Incident Back Office entitlement gate (plan rev 4.1 §17, decision D6), cloned from BusinessOperationsAccessService:
	/// master flag → capability flag → module switch → billing configured → live add-on window, failing closed on any error.
	/// A lapsed add-on leaves existing data readable (ReadOnly) so nothing an incident recorded disappears with a payment.
	/// </summary>
	public class IncidentBackOfficeAccessService : IIncidentBackOfficeAccessService
	{
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentSettingsService _settings;
		private readonly ISubscriptionsService _subscriptions;
		private readonly IIncidentLedgerRepository<IncidentActivation> _activations;

		public IncidentBackOfficeAccessService(IFeatureToggleService flags, IDepartmentSettingsService settings, ISubscriptionsService subscriptions,
			IIncidentLedgerRepository<IncidentActivation> activations)
		{
			_flags = flags;
			_settings = settings;
			_subscriptions = subscriptions;
			_activations = activations;
		}

		public async Task<bool> IsEnabledAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;
			try
			{
				if ((await _flags.EvaluateFreshAsync(FeatureFlagKeys.IncidentBackOffice, departmentId))?.IsEnabled != true)
					return false;
				var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId, bypassCache: true);
				return modules != null && !modules.IncidentBackOfficeDisabled;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		public Task<bool> IsBusinessEnabledAsync(int departmentId) => IsCapabilityEnabledAsync(departmentId, FeatureFlagKeys.IncidentBusiness, m => m.IncidentBusinessDisabled);

		public Task<bool> IsCoordinationEnabledAsync(int departmentId) => IsCapabilityEnabledAsync(departmentId, FeatureFlagKeys.IncidentCoordination, m => m.IncidentCoordinationDisabled);

		private async Task<bool> IsCapabilityEnabledAsync(int departmentId, string flag, Func<DepartmentModuleSettings, bool> disabled)
		{
			if (!await IsEnabledAsync(departmentId))
				return false;
			try
			{
				// The capability flag carries a FeatureFlagPrerequisite on Incident.BackOffice (M0291); the explicit master
				// check above keeps the kill switch effective even where a prerequisite row was never seeded.
				if ((await _flags.EvaluateFreshAsync(flag, departmentId))?.IsEnabled != true)
					return false;
				var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId, bypassCache: true);
				return modules != null && !disabled(modules);
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
				// Generic billing helpers synthesize free forever PTT payments when billing is unconfigured. A paid add-on must not
				// grant access through that fallback (Readiness Pro and Business Ops precedent).
				if (string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) ||
					string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
					return false;

				var plans = await _subscriptions.GetAllAddonPlansByTypeAsync(PlanAddonTypes.IncidentBackOffice);
				var ids = plans?.Where(x => x != null && x.AddonType == (int)PlanAddonTypes.IncidentBackOffice &&
					!string.IsNullOrWhiteSpace(x.PlanAddonId)).Select(x => x.PlanAddonId).Distinct().ToList();
				if (ids == null || ids.Count == 0)
					return false;

				// No entitlement cache: cancellation and renewal take effect on the next request.
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

		public async Task<IncidentBackOfficeAccess> GetAccessAsync(int departmentId)
		{
			if (!await IsEnabledAsync(departmentId))
				return IncidentBackOfficeAccess.Denied;

			var access = new IncidentBackOfficeAccess
			{
				Enabled = true,
				BusinessEnabled = await IsBusinessEnabledAsync(departmentId),
				CoordinationEnabled = await IsCoordinationEnabledAsync(departmentId),
				Licensed = await HasActiveAddonAsync(departmentId)
			};

			if (access.Licensed)
				access.Level = IncidentBackOfficeAccessLevels.Full;
			else
			{
				try
				{
					// Lapsed: a department that once ran the back office keeps reading what it recorded.
					var any = await _activations.CountAsync(departmentId, new IncidentLedgerQuery { IncludeDeleted = false });
					access.Level = any > 0 ? IncidentBackOfficeAccessLevels.ReadOnly : IncidentBackOfficeAccessLevels.None;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex);
					access.Level = IncidentBackOfficeAccessLevels.None;
				}
			}

			return access;
		}
	}
}
