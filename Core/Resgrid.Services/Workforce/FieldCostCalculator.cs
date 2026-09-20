using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Model.Workforce;

namespace Resgrid.Services.Workforce
{
	/// <summary>
	/// The one pure line calculator behind every field-cost run (Workforce &amp; Business Operations plan, E3).
	/// Labor: base rate for the pay basis × pay-code multiplier, plus eligible pay components (per hour /
	/// percent of base / fixed spread over the standard year) and employer-cost components (percent of eligible
	/// pay / per hour / per shift / per day / fixed annual ÷ standard hours). Resources: each applicable variable
	/// component × approved usage, fuel as a direct rate or consumption × unit price (actual fuel cost overrides
	/// both), straight-line depreciation = (acquisition − salvage) ÷ useful life (or annual depreciation ÷ expected
	/// utilization for a time life), fixed annual components allocated by expected utilization. Anything that
	/// cannot be priced becomes a blocked detail with its reason — never a silent zero.
	/// </summary>
	public static class FieldCostCalculator
	{
		public const decimal MilesPerKilometer = 0.621371m;

		#region Labor

		public static LaborCostResult CalculateLabor(LaborCostInput input)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var work = input.Work ?? throw new ArgumentException("Work quantity is required.", nameof(input));
			var result = new LaborCostResult { IsFallback = input.IsFallback };
			if (input.IsFallback) result.ReviewReasons.Add(input.Profile?.Scope == (int)CompensationScopes.DepartmentDefault ? LaborReviewReasons.DepartmentFallback : LaborReviewReasons.RoleFallback);

			// An approved payroll cost from the payroll system is the actual; nothing is estimated on top of it.
			if (work.ApprovedPayrollCost.HasValue)
			{
				result.PayAmount = Round(work.ApprovedPayrollCost.Value);
				result.IsEstimated = false;
				result.ReviewReasons.Add(LaborReviewReasons.ApprovedPayrollCostUsed);
				result.Details.Add(new LaborCostDetail { Kind = "Payroll", Name = "Approved payroll cost", Basis = "actual", Rate = work.ApprovedPayrollCost.Value, Amount = result.PayAmount });
				result.NeedsReview = input.IsFallback;
				return result;
			}

			var profile = input.Profile;
			if (profile == null)
			{
				result.NeedsReview = true;
				result.ReviewReasons.Add(LaborReviewReasons.NoProfile);
				return result;
			}
			if (!profile.IsApproved) { result.NeedsReview = true; result.ReviewReasons.Add(LaborReviewReasons.UnapprovedProfile); }
			if (!string.IsNullOrWhiteSpace(input.Currency) && !string.IsNullOrWhiteSpace(profile.Currency) && !string.Equals(input.Currency, profile.Currency, StringComparison.OrdinalIgnoreCase))
			{
				result.NeedsReview = true;
				result.ReviewReasons.Add(LaborReviewReasons.CurrencyMismatch);
			}

			var baseRate = HourlyRate(profile);
			if (!baseRate.HasValue)
			{
				result.NeedsReview = true;
				result.ReviewReasons.Add(LaborReviewReasons.NoRate);
				return result;
			}
			var multipliers = Multipliers(profile);
			var code = (PayCodes)work.PayCode;
			var multiplier = code == PayCodes.Regular || code == PayCodes.PaidLeave ? 1m : multipliers.TryGetValue(code.ToString(), out var m) ? m : DefaultMultiplier(code);
			if (code != PayCodes.Regular && code != PayCodes.PaidLeave && !multipliers.ContainsKey(code.ToString())) result.ReviewReasons.Add(LaborReviewReasons.PayCodeMultiplierMissing);

			result.BaseRate = baseRate.Value;
			result.Multiplier = multiplier;
			result.PayAmount = Round(work.Hours * baseRate.Value * multiplier);
			result.Details.Add(new LaborCostDetail { Kind = "Pay", Name = code.ToString(), Basis = "hour", Rate = Round4(baseRate.Value * multiplier), Amount = result.PayAmount, ComponentId = profile.EmployeeCompensationProfileId, Version = profile.RowVersion });

			var standardYearHours = profile.StandardHoursPerYear ?? (profile.StandardHoursPerWeek.HasValue ? profile.StandardHoursPerWeek.Value * 52m : 2080m);
			var standardDayHours = profile.StandardHoursPerDay ?? 8m;
			var codeName = code.ToString();
			foreach (var component in (profile.PayComponents ?? new List<EmployeePayComponent>()).Where(c => !c.IsDeleted && c.Covers(input.AsOf) && Eligible(c.EligiblePayCodesCsv, codeName)))
			{
				var amount = component.AmountValue;
				if (!amount.HasValue) { result.NeedsReview = true; result.ReviewReasons.Add(LaborReviewReasons.NoRate); continue; }
				// Fixed-period components are paid regardless of overtime; only components flagged PaidForEachOvertimeHour follow OT hours.
				if (code == PayCodes.Overtime || code == PayCodes.DoubleTime) { if (!component.PaidForEachOvertimeHour) continue; }
				var value = (PayComponentBases)component.Basis switch
				{
					PayComponentBases.PerHour => work.Hours * amount.Value,
					PayComponentBases.PercentOfBase => result.PayAmount * amount.Value / 100m,
					PayComponentBases.PerShift => standardDayHours > 0 ? work.Hours / standardDayHours * amount.Value : 0m,
					PayComponentBases.PerPayPeriod => standardYearHours > 0 ? work.Hours / standardYearHours * amount.Value * 26m : 0m,
					PayComponentBases.FixedAnnual => standardYearHours > 0 ? work.Hours / standardYearHours * amount.Value : 0m,
					_ => 0m
				};
				value = Round(value);
				result.PayComponentAmount += value;
				result.Details.Add(new LaborCostDetail { Kind = "PayComponent", Name = component.Name ?? ((PayComponentCategories)component.Category).ToString(), Basis = ((PayComponentBases)component.Basis).ToString(), Rate = amount.Value, Amount = value, ComponentId = component.EmployeePayComponentId, Version = component.RowVersion });
			}

			var eligiblePay = result.PayAmount + result.PayComponentAmount;
			foreach (var component in (profile.CostComponents ?? new List<EmployeeCostComponent>()).Where(c => !c.IsDeleted && c.Covers(input.AsOf) && Eligible(c.EligiblePayCodesCsv, codeName)))
			{
				var rate = component.RateAmountValue;
				if (!rate.HasValue) { result.NeedsReview = true; result.ReviewReasons.Add(LaborReviewReasons.NoRate); continue; }
				var value = (CostComponentBases)component.Basis switch
				{
					CostComponentBases.PercentOfEligiblePay => eligiblePay * rate.Value / 100m,
					CostComponentBases.PerHour => work.Hours * rate.Value,
					CostComponentBases.PerShift => standardDayHours > 0 ? work.Hours / standardDayHours * rate.Value : 0m,
					CostComponentBases.PerDay => standardDayHours > 0 ? Math.Ceiling(work.Hours / standardDayHours) * rate.Value : 0m,
					CostComponentBases.FixedAnnual => standardYearHours > 0 ? work.Hours / standardYearHours * rate.Value : 0m,
					_ => 0m
				};
				var cap = component.CapValue;
				if (cap.HasValue && value > cap.Value) value = cap.Value;
				value = Round(value);
				result.EmployerCostAmount += value;
				result.Details.Add(new LaborCostDetail { Kind = "EmployerCost", Name = component.Name ?? ((CostComponentCategories)component.Category).ToString(), Basis = ((CostComponentBases)component.Basis).ToString(), Rate = rate.Value, Amount = value, ComponentId = component.EmployeeCostComponentId, Version = component.RowVersion });
			}
			result.NeedsReview = result.NeedsReview || input.IsFallback;
			return result;
		}

		/// <summary>The profile's regular hourly rate for its pay basis.</summary>
		public static decimal? HourlyRate(EmployeeCompensationProfile profile)
		{
			if (profile == null) return null;
			if (profile.RegularHourlyEquivalentValue.HasValue) return profile.RegularHourlyEquivalentValue;
			var amount = profile.BaseAmountValue;
			if (!amount.HasValue) return null;
			var perDay = profile.StandardHoursPerDay ?? 8m;
			var perYear = profile.StandardHoursPerYear ?? (profile.StandardHoursPerWeek.HasValue ? profile.StandardHoursPerWeek.Value * 52m : 2080m);
			return (PayBases)profile.PayBasis switch
			{
				PayBases.Hourly => amount,
				PayBases.Salary => perYear > 0 ? Round4(amount.Value / perYear) : null,
				PayBases.Daily => perDay > 0 ? Round4(amount.Value / perDay) : null,
				PayBases.Shift => perDay > 0 ? Round4(amount.Value / perDay) : null,
				PayBases.Stipend => perYear > 0 ? Round4(amount.Value / perYear) : null,
				_ => null
			};
		}

		public static Dictionary<string, decimal> Multipliers(EmployeeCompensationProfile profile)
		{
			if (string.IsNullOrWhiteSpace(profile?.RateMultipliersJson) || WorkforceProtectionSeam.IsUnavailable(profile.RateMultipliersJson)) return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
			try { return new Dictionary<string, decimal>(JsonConvert.DeserializeObject<Dictionary<string, decimal>>(profile.RateMultipliersJson) ?? new Dictionary<string, decimal>(), StringComparer.OrdinalIgnoreCase); }
			catch { return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase); }
		}

		public static decimal DefaultMultiplier(PayCodes code) => code switch { PayCodes.Overtime => 1.5m, PayCodes.DoubleTime => 2m, PayCodes.Standby => 1m, PayCodes.Travel => 1m, _ => 1m };

		private static bool Eligible(string csv, string code) => string.IsNullOrWhiteSpace(csv) || csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).Contains(code, StringComparer.OrdinalIgnoreCase);

		#endregion

		#region Resources

		public static ResourceCostResult CalculateResource(ResourceCostInput input)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var usage = input.Usage ?? throw new ArgumentException("Usage quantity is required.", nameof(input));
			var result = new ResourceCostResult { IsFallback = input.IsFallback };
			if (input.IsFallback) { result.NeedsReview = true; result.ReviewReasons.Add(ResourceReviewReasons.ClassFallback); }
			var profile = input.Profile;
			if (profile == null) { result.NeedsReview = true; result.ReviewReasons.Add(ResourceReviewReasons.NoProfile); return result; }

			var components = (profile.Components ?? new List<ResourceCostComponent>()).Where(c => !c.IsDeleted && c.Covers(input.AsOf)).ToList();
			var hasDepreciationComponent = components.Any(c => c.Category == (int)ResourceCostCategories.Depreciation);
			// Straight-line depreciation from the acquisition facts when no explicit depreciation component exists.
			if (!hasDepreciationComponent && (profile.AcquisitionCost.HasValue || profile.UsefulLifeQuantity.HasValue || profile.UsefulLifeMonths.HasValue))
			{
				var rate = DepreciationRate(profile);
				var (qty, unit) = QuantityFor((AllocationBases)profile.AllocationBasis, usage);
				if (!rate.HasValue)
				{
					result.NeedsReview = true; result.ReviewReasons.Add(ResourceReviewReasons.DepreciationInputsMissing);
					result.Details.Add(new ResourceCostDetail { Category = ResourceCostCategories.Depreciation.ToString(), Basis = ((AllocationBases)profile.AllocationBasis).ToString(), Quantity = qty, Unit = unit, Blocked = true, Reason = ResourceReviewReasons.DepreciationInputsMissing });
				}
				else result.Details.Add(new ResourceCostDetail { Category = ResourceCostCategories.Depreciation.ToString(), Basis = ((AllocationBases)profile.AllocationBasis).ToString(), Quantity = qty, Unit = unit, Rate = rate.Value, Amount = Round(qty * rate.Value), ComponentId = profile.ResourceCostProfileId, Version = profile.RowVersion });
			}

			var fuelOverridden = false;
			foreach (var component in components)
			{
				var category = (ResourceCostCategories)component.Category;
				var name = category.ToString();
				if (!component.IsApproved) { result.NeedsReview = true; result.ReviewReasons.Add(ResourceReviewReasons.ComponentUnapproved); }
				if (input.ExcludedComponentIds.Contains(component.ResourceCostComponentId))
				{
					result.NeedsReview = true; result.ReviewReasons.Add(ResourceReviewReasons.RepairDoubleCounted);
					result.Details.Add(new ResourceCostDetail { Category = name, Basis = ((ResourceCostBases)component.Basis).ToString(), Blocked = true, Reason = ResourceReviewReasons.RepairDoubleCounted, ComponentId = component.ResourceCostComponentId, Version = component.RowVersion });
					continue;
				}
				if (category == ResourceCostCategories.FuelEnergy && usage.ActualFuelCost.HasValue)
				{
					if (!fuelOverridden) { result.Details.Add(new ResourceCostDetail { Category = name, Basis = "actual", Quantity = 1, Unit = "actual", Rate = usage.ActualFuelCost.Value, Amount = Round(usage.ActualFuelCost.Value), ComponentId = component.ResourceCostComponentId, Version = component.RowVersion }); fuelOverridden = true; }
					continue;
				}
				if (category == ResourceCostCategories.Maintenance && component.Source == (int)ResourceCostSources.WorkOrderRollingActual && (!component.SourceMeterStart.HasValue || !component.SourceMeterEnd.HasValue || component.SourceMeterEnd <= component.SourceMeterStart))
				{
					result.NeedsReview = true; result.ReviewReasons.Add(ResourceReviewReasons.RollingWindowInsufficient);
					result.Details.Add(new ResourceCostDetail { Category = name, Basis = ((ResourceCostBases)component.Basis).ToString(), Blocked = true, Reason = ResourceReviewReasons.RollingWindowInsufficient, ComponentId = component.ResourceCostComponentId, Version = component.RowVersion });
					continue;
				}
				var basis = (ResourceCostBases)component.Basis;
				var (quantity, unit) = QuantityFor(basis, usage, profile);
				decimal? rate = component.Rate;
				if (!rate.HasValue && category == ResourceCostCategories.FuelEnergy && component.ConsumptionQuantity.HasValue && component.UnitPrice.HasValue) rate = component.ConsumptionQuantity.Value * component.UnitPrice.Value;
				if (basis == ResourceCostBases.FixedAnnual)
				{
					var utilization = profile.ExpectedAnnualUtilization;
					if (!utilization.HasValue || utilization <= 0 || !rate.HasValue)
					{
						result.NeedsReview = true; result.ReviewReasons.Add(ResourceReviewReasons.UtilizationMissing);
						result.Details.Add(new ResourceCostDetail { Category = name, Basis = basis.ToString(), Blocked = true, Reason = ResourceReviewReasons.UtilizationMissing, ComponentId = component.ResourceCostComponentId, Version = component.RowVersion });
						continue;
					}
					var (allocQty, allocUnit) = QuantityFor((AllocationBases)profile.AllocationBasis, usage);
					var perUnit = Round4(rate.Value / utilization.Value);
					result.Details.Add(new ResourceCostDetail { Category = name, Basis = "FixedAnnual/" + allocUnit, Quantity = allocQty, Unit = allocUnit, Rate = perUnit, Amount = Round(allocQty * perUnit), ComponentId = component.ResourceCostComponentId, Version = component.RowVersion });
					continue;
				}
				if (!rate.HasValue)
				{
					var reason = category == ResourceCostCategories.FuelEnergy ? ResourceReviewReasons.FuelInputsMissing : ResourceReviewReasons.NoProfile;
					result.NeedsReview = true; result.ReviewReasons.Add(reason);
					result.Details.Add(new ResourceCostDetail { Category = name, Basis = basis.ToString(), Quantity = quantity, Unit = unit, Blocked = true, Reason = reason, ComponentId = component.ResourceCostComponentId, Version = component.RowVersion });
					continue;
				}
				result.Details.Add(new ResourceCostDetail { Category = name, Basis = basis.ToString(), Quantity = quantity, Unit = unit, Rate = Round4(rate.Value), Amount = Round(quantity * rate.Value), ComponentId = component.ResourceCostComponentId, Version = component.RowVersion });
			}
			return result;
		}

		/// <summary>Straight-line depreciation per allocation unit: (acquisition − salvage) ÷ useful-life quantity, or the annual amount ÷ expected annual utilization for a time life.</summary>
		public static decimal? DepreciationRate(ResourceCostProfile profile)
		{
			if (profile == null || !profile.AcquisitionCost.HasValue) return null;
			var depreciable = profile.AcquisitionCost.Value - (profile.SalvageValue ?? 0m);
			if (depreciable < 0) return null;
			if (profile.UsefulLifeQuantity.HasValue && profile.UsefulLifeQuantity.Value > 0) return Round4(depreciable / profile.UsefulLifeQuantity.Value);
			if (profile.UsefulLifeMonths.HasValue && profile.UsefulLifeMonths.Value > 0 && profile.ExpectedAnnualUtilization.HasValue && profile.ExpectedAnnualUtilization.Value > 0)
				return Round4(depreciable / (profile.UsefulLifeMonths.Value / 12m) / profile.ExpectedAnnualUtilization.Value);
			return null;
		}

		public static (decimal Quantity, string Unit) QuantityFor(AllocationBases basis, ResourceUsageQuantity usage) => basis switch
		{
			AllocationBases.Mile => (usage.Miles, "mile"),
			AllocationBases.Kilometer => (Round(usage.Miles / MilesPerKilometer), "km"),
			AllocationBases.EngineHour => (usage.EngineHours, "engine hour"),
			AllocationBases.OperatingHour => (usage.OperatingHours, "operating hour"),
			AllocationBases.Day => (usage.Days, "day"),
			_ => (0m, "?")
		};

		public static (decimal Quantity, string Unit) QuantityFor(ResourceCostBases basis, ResourceUsageQuantity usage, ResourceCostProfile profile) => basis switch
		{
			ResourceCostBases.PerMile => (usage.Miles, "mile"),
			ResourceCostBases.PerKilometer => (Round(usage.Miles / MilesPerKilometer), "km"),
			ResourceCostBases.PerEngineHour => (usage.EngineHours, "engine hour"),
			ResourceCostBases.PerOperatingHour => (usage.OperatingHours, "operating hour"),
			ResourceCostBases.PerIdleHour => (usage.IdleHours, "idle hour"),
			ResourceCostBases.PerDay => (usage.Days, "day"),
			ResourceCostBases.PerDeployment => (usage.Deployments, "deployment"),
			_ => QuantityFor((AllocationBases)profile.AllocationBasis, usage)
		};

		public static decimal ToMiles(decimal distance, string unit) => string.Equals(unit, "km", StringComparison.OrdinalIgnoreCase) ? Round(distance * MilesPerKilometer) : distance;

		#endregion

		public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
		public static decimal Round4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
	}
}
