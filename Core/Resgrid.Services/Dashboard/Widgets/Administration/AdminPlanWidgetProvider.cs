using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.plan (plan §7.11). The Subscription page's plan block, limits and add-on cards with fewer Billing API reads than the page:
	/// the cached plan (GetCurrentPlanForDepartmentAsync can return null — CLAUDE.md gotcha 2 — which shows the page's free-plan
	/// fallback), cached limits, one add-on plan list and one held-add-on read covering every add-on type (Push-To-Talk included, as
	/// IVoiceService reads it, instead of the page's per-pack Stripe read). Billing figures only for a member who may manage the
	/// subscription (IAuthorizationService.CanUserManageSubscriptionAsync, the page's own rule).
	/// The add-on availability logic stays in SubscriptionController: its types (AddonCatalog, AddonAvailability) live in the web
	/// project and moving it into ISubscriptionsService would need IFeatureToggleService, which already depends on that service.
	/// </summary>
	[DashboardWidget("admin.plan")]
	public class AdminPlanWidgetProvider : IDashboardWidgetDataProvider
	{
		/// <summary>The Subscription page's add-on cards, in its order, with the flag that offers each (AddonCatalog); PTT is always offered.</summary>
		internal static readonly IReadOnlyList<(PlanAddonTypes Type, string Flag)> Addons = new (PlanAddonTypes, string)[]
		{
			(PlanAddonTypes.PTT, null),
			(PlanAddonTypes.ReadinessPro, FeatureFlagKeys.MaintenanceWorkOrders),
			(PlanAddonTypes.BusinessOperations, FeatureFlagKeys.BusinessOperations),
			(PlanAddonTypes.IncidentBackOffice, FeatureFlagKeys.IncidentBackOffice),
			(PlanAddonTypes.EnhancedAi, FeatureFlagKeys.AiEnhanced),
			(PlanAddonTypes.ADP, FeatureFlagKeys.DepartmentProtectedDataEnrollment)
		};

		private readonly ISubscriptionsService _subscriptionsService;
		private readonly ILimitsService _limitsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IFeatureToggleService _featureToggleService;

		public AdminPlanWidgetProvider(ISubscriptionsService subscriptionsService, ILimitsService limitsService, IAuthorizationService authorizationService,
			IFeatureToggleService featureToggleService)
		{
			_subscriptionsService = subscriptionsService;
			_limitsService = limitsService;
			_authorizationService = authorizationService;
			_featureToggleService = featureToggleService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId, false);
			var data = new AdminPlanWidgetData
			{
				PlanUnavailable = plan == null,
				PlanId = plan?.PlanId ?? 1,
				PlanName = plan?.Name,
				IsFree = plan == null || plan.IsFree
			};

			var limits = await _limitsService.GetLimitsForEntityPlanWithFallbackAsync(departmentId) ?? new DepartmentLimits();
			data.IsEntityPlan = limits.IsEntityPlan;
			data.PersonnelCount = limits.PersonnelCount;
			data.PersonnelLimit = limits.PersonnelLimit;
			data.UnitsCount = limits.UnitsCount;
			data.UnitsLimit = limits.UnitsLimit;
			data.EntityCount = limits.GetCurrentEntityTotal();
			data.EntityLimit = limits.EntityTotal;
			var (used, limit) = limits.IsEntityPlan ? (data.EntityCount, data.EntityLimit) : (data.PersonnelCount, data.PersonnelLimit);
			data.UsedPercent = limit > 0 && limit < int.MaxValue ? (int)Math.Round(used * 100.0 / limit) : null;

			data.CanManageBilling = await _authorizationService.CanUserManageSubscriptionAsync(context.UserId, departmentId);
			if (data.CanManageBilling && plan != null)
			{
				var payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(departmentId, false);
				// The page shows the payment's amount for a paid plan whose catalog cost is zero (custom pricing).
				data.Cost = (decimal)(plan.PlanId != 1 && plan.Cost == 0 && payment != null ? payment.Amount : plan.Cost);
				data.Frequency = Enum.GetName(typeof(PlanFrequency), plan.Frequency);
				if (payment != null)
				{
					// DateTime.MaxValue loses precision across the Billing API: any value on its date never expires.
					data.RenewsOn = payment.EndingOn.Date == DateTime.MaxValue.Date ? null : payment.EndingOn;
					data.Cancelled = payment.Cancelled;
				}
			}

			data.Addons = await AddonsAsync(departmentId);
			return data;
		}

		private async Task<List<AdminPlanAddon>> AddonsAsync(int departmentId)
		{
			var plans = new List<PlanAddon>();
			var held = new List<PaymentAddon>();
			try
			{
				plans = await _subscriptionsService.GetAllAddonPlansAsync() ?? new List<PlanAddon>();
				var planIds = plans.Where(p => p != null && !string.IsNullOrWhiteSpace(p.PlanAddonId)).Select(p => p.PlanAddonId).Distinct().ToList();
				if (planIds.Count > 0)
					held = await _subscriptionsService.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, planIds) ?? new List<PaymentAddon>();
			}
			catch (Exception ex)
			{
				// As on the Subscription page: the plan block still renders; held add-ons are then simply not listed.
				Logging.LogException(ex, $"Dashboard admin.plan could not load add-on payments for DepartmentId: {departmentId}");
			}

			var result = new List<AdminPlanAddon>();
			foreach (var (type, flag) in Addons)
			{
				var typePlanIds = plans.Where(p => p != null && p.AddonType == (int)type).Select(p => p.PlanAddonId).ToHashSet(StringComparer.Ordinal);
				var rows = held.Where(h => h != null && typePlanIds.Contains(h.PlanAddonId)).ToList();
				var cancelled = rows.Count > 0 && rows.All(h => h.IsCancelled);
				var endsOn = rows.Count > 0 ? rows.Max(h => h.EndingOn) : (DateTime?)null;
				var offered = flag == null || await _featureToggleService.IsEnabledAsync(flag, departmentId);
				result.Add(new AdminPlanAddon
				{
					Key = type.ToString(),
					Offered = offered,
					Held = rows.Count > 0,
					Cancelled = cancelled,
					// A manual or invoiced grant runs on the DateTime.MaxValue sentinel; it has no end date to show.
					EndsOn = cancelled && endsOn.HasValue && endsOn.Value.Date != DateTime.MaxValue.Date ? endsOn : null
				});
			}

			return result;
		}
	}
}
