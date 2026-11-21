using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Who may buy and who holds the Dedicated Number add-on (call-intake-and-ai-destinations-plan.md D1; PlanAddonTypes.DedicatedNumber).
	/// The add-on is sold only to departments with a billing relationship (an active paid entity-pack plan) while
	/// <see cref="FeatureFlagKeys.IntakeDedicatedNumber"/> is on; the entitlement itself is the live PaymentAddons window, read from the
	/// Billing API with no cache (Enhanced AI precedent), so a purchase or lapse takes effect on the next request. Fails closed: with
	/// billing unconfigured (an open-source install) nothing is offered and no add-on is held.
	/// </summary>
	public sealed class DedicatedNumberAccessService : IDedicatedNumberAccessService
	{
		private readonly IFeatureToggleService _flags;
		private readonly ISubscriptionsService _subscriptions;
		private readonly IDepartmentsService _departments;
		private readonly IAddressService _addresses;

		public DedicatedNumberAccessService(IFeatureToggleService flags, ISubscriptionsService subscriptions, IDepartmentsService departments,
			IAddressService addresses)
		{
			_flags = flags;
			_subscriptions = subscriptions;
			_departments = departments;
			_addresses = addresses;
		}

		public async Task<DedicatedNumberAccess> GetAccessAsync(int departmentId)
		{
			var access = new DedicatedNumberAccess { AllowedCountries = Array.Empty<string>() };
			if (departmentId <= 0 || !IsBillingConfigured())
				return access;

			access.IsOffered = await FlagOnAsync(departmentId);
			access.HasBillingRelationship = await HasEntityPlanAsync(departmentId);
			access.HasActiveAddon = await GetActiveAddonStateAsync(departmentId) == true;

			// A department that still holds the add-on keeps managing its number even after its base plan changed.
			if (!access.HasBillingRelationship && !access.HasActiveAddon)
				return access;

			access.BillingProvider = PaymentProviderConfig.IsPaddleActive() ? "Paddle" : "Stripe";
			access.AllowedCountries = await AllowedCountriesAsync(departmentId, access.BillingProvider);
			return access;
		}

		public async Task<bool> HasActiveAddonAsync(int departmentId) => await GetActiveAddonStateAsync(departmentId) == true;

		public async Task<bool?> GetActiveAddonStateAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			// Generic billing helpers synthesize free forever payments when billing is unconfigured; a paid add-on must never be granted
			// through that fallback, and the number lifecycle must never act on it either, so the state is unknown.
			if (!IsBillingConfigured())
				return null;

			try
			{
				var plans = await _subscriptions.GetAllAddonPlansByTypeAsync(PlanAddonTypes.DedicatedNumber);
				var ids = plans?.Where(x => x != null && x.AddonType == (int)PlanAddonTypes.DedicatedNumber && !string.IsNullOrWhiteSpace(x.PlanAddonId))
					.Select(x => x.PlanAddonId).Distinct().ToList();
				if (ids == null || ids.Count == 0)
					return null;

				// No entitlement cache: cancellation and renewal take effect on the next request.
				var payments = await _subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, ids);
				if (payments == null)
					return null;

				var now = DateTime.UtcNow;
				return payments.Any(x => x != null && x.DepartmentId == departmentId && ids.Contains(x.PlanAddonId) &&
					x.EffectiveOn != default && x.EffectiveOn <= now && x.EndingOn > now &&
					!string.Equals(x.TransactionId, "SYSTEM", StringComparison.OrdinalIgnoreCase) && x.EndingOn != DateTime.MaxValue);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated Number add-on state failed for DepartmentId: {departmentId}");
				return null;
			}
		}

		public async Task<DedicatedNumberPaymentState> GetPaymentStateAsync(int departmentId)
		{
			// The add-on first: no paid interval is "stopped" without asking about the plan, and an unknown add-on state is unknown
			// whatever the plan read says.
			var addon = await GetActiveAddonStateAsync(departmentId);
			if (addon != true)
				return new DedicatedNumberPaymentState { AddonActive = addon };

			return new DedicatedNumberPaymentState { AddonActive = true, PaidEntityPlan = await GetPaidEntityPlanStateAsync(departmentId) };
		}

		/// <summary>
		/// True: an active paid entity plan. False: a plan that is not one. Null: no plan came back or the read failed. Read with
		/// GetCurrentPlanForDepartmentIfKnownAsync: the ordinary read answers a timeout or fault with the free plan, which here would be
		/// "known not paying" and start a paying department's release clock during a Billing API outage.
		/// </summary>
		private async Task<bool?> GetPaidEntityPlanStateAsync(int departmentId)
		{
			if (!IsBillingConfigured())
				return null;

			try
			{
				var plan = await _subscriptions.GetCurrentPlanForDepartmentIfKnownAsync(departmentId);
				if (plan == null)
					return null;

				return DedicatedNumberAccess.IsEntityPlan(plan.PlanId) && !_subscriptions.IsPlanRestrictedOrFree(plan.PlanId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated Number plan state failed for DepartmentId: {departmentId}");
				return null;
			}
		}

		private async Task<bool> FlagOnAsync(int departmentId)
		{
			try
			{
				return (await _flags.EvaluateFreshAsync(FeatureFlagKeys.IntakeDedicatedNumber, departmentId))?.IsEnabled == true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Unable to evaluate {FeatureFlagKeys.IntakeDedicatedNumber} for DepartmentId: {departmentId}");
				return false;
			}
		}

		/// <summary>An active paid entity-pack plan. GetCurrentPlanForDepartmentAsync falls back to the free plan, and may return null.</summary>
		private async Task<bool> HasEntityPlanAsync(int departmentId)
		{
			try
			{
				var plan = await _subscriptions.GetCurrentPlanForDepartmentAsync(departmentId);
				return plan != null && DedicatedNumberAccess.IsEntityPlan(plan.PlanId) && !_subscriptions.IsPlanRestrictedOrFree(plan.PlanId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated Number plan check failed for DepartmentId: {departmentId}");
				return false;
			}
		}

		/// <summary>
		/// Stripe (US cluster): <see cref="DedicatedNumberAddonConfig.StripeCountries"/>. Paddle (EU cluster): the department's own country,
		/// only when the operator configured it in <see cref="DedicatedNumberAddonConfig.PaddleCountries"/> (it needs a Twilio regulatory bundle).
		/// </summary>
		private async Task<IReadOnlyList<string>> AllowedCountriesAsync(int departmentId, string provider)
		{
			if (provider == "Stripe")
				return ParseCountries(DedicatedNumberAddonConfig.StripeCountries);

			var configured = ParseCountries(DedicatedNumberAddonConfig.PaddleCountries);
			if (configured.Count == 0)
				return configured;

			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				if (department?.DepartmentId != departmentId)
					return Array.Empty<string>();

				var country = department.Address?.Country;
				if (string.IsNullOrWhiteSpace(country) && department.AddressId.HasValue)
					country = (await _addresses.GetAddressByIdAsync(department.AddressId.Value))?.Country;

				var code = ToCountryCode(country);
				return code != null && configured.Contains(code) ? new[] { code } : Array.Empty<string>();
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Dedicated Number country lookup failed for DepartmentId: {departmentId}");
				return Array.Empty<string>();
			}
		}

		/// <summary>"US,CA" → ["US", "CA"]: trimmed, upper-cased ISO 3166-1 alpha-2 codes, duplicates and malformed entries dropped.</summary>
		public static IReadOnlyList<string> ParseCountries(string value) =>
			(value ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(c => c.Trim().ToUpperInvariant())
				.Where(c => c.Length == 2 && c.All(char.IsLetter))
				.Distinct().ToList();

		/// <summary>
		/// An ISO 3166-1 alpha-2 code for a department address country, which the profile stores as an English name ("Germany") from
		/// <see cref="Countries.CountryNames"/> or, from imports, as a code. Null when it cannot be resolved.
		/// </summary>
		public static string ToCountryCode(string country)
		{
			if (string.IsNullOrWhiteSpace(country))
				return null;

			var value = country.Trim();
			if (value.Length == 2 && value.All(char.IsLetter))
				return value.ToUpperInvariant();

			switch (value.ToUpperInvariant())
			{
				case "UNITED STATES":
				case "UNITED STATES OF AMERICA":
				case "USA":
					return "US";
				case "UNITED KINGDOM":
				case "GREAT BRITAIN":
					return "GB";
			}

			try
			{
				foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
				{
					RegionInfo region;
					try { region = new RegionInfo(culture.Name); }
					catch (ArgumentException) { continue; }

					if (string.Equals(region.EnglishName, value, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(region.ThreeLetterISORegionName, value, StringComparison.OrdinalIgnoreCase))
						return region.TwoLetterISORegionName.ToUpperInvariant();
				}
			}
			catch (Exception)
			{
				// Invariant globalization (no ICU data): only codes and the names above resolve.
			}

			return null;
		}

		private static bool IsBillingConfigured() =>
			!string.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) && !string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey);
	}
}
