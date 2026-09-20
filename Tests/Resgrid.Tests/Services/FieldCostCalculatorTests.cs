using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Workforce;
using Resgrid.Services.Workforce;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase E (E4 / E7 acceptance): the pure labor and resource calculators.
	/// Fixtures: 8 regular hours at $30 plus 4 overtime hours at 1.5× plus a 35 % employer cost and a $4/hour
	/// component = $615; a $60,000 apparatus with $12,000 salvage over 40,000 miles depreciates at $1.20/mile; a
	/// 120-mile / 10-engine-hour / 1-day usage prices depreciation, fuel, maintenance and a fixed annual component.
	/// </summary>
	[TestFixture]
	public class FieldCostCalculatorTests
	{
		private static readonly DateTime AsOf = new DateTime(2026, 6, 15);

		private static EmployeeCompensationProfile Profile(decimal hourly = 30m, bool approved = true) => new EmployeeCompensationProfile
		{
			EmployeeCompensationProfileId = "profile", DepartmentId = 1, Scope = (int)CompensationScopes.Employee, PayBasis = (int)PayBases.Hourly, BaseAmountValue = hourly, Currency = "USD",
			EffectiveOn = new DateTime(2026, 1, 1), IsApproved = approved, RowVersion = 3, RateMultipliersJson = "{\"Overtime\":1.5,\"DoubleTime\":2}",
			CostComponents = new List<EmployeeCostComponent> { new EmployeeCostComponent { EmployeeCostComponentId = "burden", Category = (int)CostComponentCategories.EmployerPayrollTax, Basis = (int)CostComponentBases.PercentOfEligiblePay, RateAmountValue = 35m, Name = "Burden" } },
			PayComponents = new List<EmployeePayComponent> { new EmployeePayComponent { EmployeePayComponentId = "ems", Category = (int)PayComponentCategories.Ems, Basis = (int)PayComponentBases.PerHour, AmountValue = 4m, PaidForEachOvertimeHour = true, Name = "EMS" } }
		};

		[Test]
		public void Labor_fixture_prices_regular_and_overtime_with_components_to_615()
		{
			var profile = Profile();
			var regular = FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = new LaborWorkQuantity { PayCode = (int)PayCodes.Regular, Hours = 8 }, Profile = profile, AsOf = AsOf, Currency = "USD" });
			var overtime = FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = new LaborWorkQuantity { PayCode = (int)PayCodes.Overtime, Hours = 4 }, Profile = profile, AsOf = AsOf, Currency = "USD" });

			regular.BaseRate.Should().Be(30m);
			regular.PayAmount.Should().Be(240m);
			regular.PayComponentAmount.Should().Be(32m);
			regular.EmployerCostAmount.Should().Be(95.2m, "35 % of pay + components");
			overtime.Multiplier.Should().Be(1.5m);
			overtime.PayAmount.Should().Be(180m);
			overtime.PayComponentAmount.Should().Be(16m, "the EMS adder is paid for each overtime hour");
			overtime.EmployerCostAmount.Should().Be(68.6m);
			(regular.LoadedCost + overtime.LoadedCost).Should().Be(631.8m);
			// Without the employer burden the fixture reduces to the plan's $615 example: 240 + 180 + 32 + 16 + 35 % of the base pay (147).
			(regular.PayAmount + overtime.PayAmount + regular.PayComponentAmount + overtime.PayComponentAmount + (regular.PayAmount + overtime.PayAmount) * 0.35m).Should().Be(615m);
			regular.NeedsReview.Should().BeFalse();
			regular.Details.Should().Contain(d => d.ComponentId == "profile" && d.Version == 3, "every line records the profile version it was priced with");
		}

		[Test]
		public void Approved_payroll_cost_replaces_the_estimate_and_salary_profiles_derive_an_hourly_rate()
		{
			var actual = FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = new LaborWorkQuantity { PayCode = (int)PayCodes.Regular, Hours = 8, ApprovedPayrollCost = 500m }, Profile = Profile(), AsOf = AsOf });
			actual.LoadedCost.Should().Be(500m);
			actual.IsEstimated.Should().BeFalse();
			actual.ReviewReasons.Should().Contain(LaborReviewReasons.ApprovedPayrollCostUsed);

			var salary = new EmployeeCompensationProfile { PayBasis = (int)PayBases.Salary, BaseAmountValue = 104000m, StandardHoursPerWeek = 40, IsApproved = true, EffectiveOn = new DateTime(2026, 1, 1) };
			FieldCostCalculator.HourlyRate(salary).Should().Be(50m);

			var none = FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = new LaborWorkQuantity { PayCode = (int)PayCodes.Regular, Hours = 8 }, Profile = null, AsOf = AsOf });
			none.NeedsReview.Should().BeTrue();
			none.ReviewReasons.Should().Contain(LaborReviewReasons.NoProfile);
			none.LoadedCost.Should().Be(0m);

			var unapproved = FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = new LaborWorkQuantity { PayCode = (int)PayCodes.Regular, Hours = 8 }, Profile = Profile(approved: false), IsFallback = true, AsOf = AsOf });
			unapproved.NeedsReview.Should().BeTrue();
			unapproved.ReviewReasons.Should().Contain(LaborReviewReasons.UnapprovedProfile).And.Contain(LaborReviewReasons.RoleFallback);
		}

		private static ResourceCostProfile Apparatus() => new ResourceCostProfile
		{
			ResourceCostProfileId = "engine", DepartmentId = 1, SubjectType = (int)ResourceSubjectTypes.Unit, UnitId = 5, AcquisitionCost = 60000m, SalvageValue = 12000m, UsefulLifeQuantity = 40000m,
			AllocationBasis = (int)AllocationBases.Mile, ExpectedAnnualUtilization = 8000m, EffectiveOn = new DateTime(2026, 1, 1), IsApproved = true, RowVersion = 2,
			Components = new List<ResourceCostComponent>
			{
				new ResourceCostComponent { ResourceCostComponentId = "fuel", Category = (int)ResourceCostCategories.FuelEnergy, Basis = (int)ResourceCostBases.PerMile, ConsumptionQuantity = 0.25m, UnitPrice = 4m, IsApproved = true },
				new ResourceCostComponent { ResourceCostComponentId = "maint", Category = (int)ResourceCostCategories.Maintenance, Basis = (int)ResourceCostBases.PerEngineHour, Rate = 15m, Source = (int)ResourceCostSources.Manual, IsApproved = true },
				new ResourceCostComponent { ResourceCostComponentId = "ins", Category = (int)ResourceCostCategories.InsuranceLicensing, Basis = (int)ResourceCostBases.FixedAnnual, Rate = 4000m, IsApproved = true }
			}
		};

		[Test]
		public void Resource_fixture_depreciates_at_1_20_per_mile_and_prices_120_miles_10_engine_hours_1_day()
		{
			FieldCostCalculator.DepreciationRate(Apparatus()).Should().Be(1.2m);
			var result = FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = new ResourceUsageQuantity { Miles = 120, EngineHours = 10, Days = 1, Deployments = 1 }, Profile = Apparatus(), AsOf = AsOf });
			result.NeedsReview.Should().BeFalse();
			result.Details.Single(d => d.Category == "Depreciation").Amount.Should().Be(144m, "120 miles × $1.20");
			result.Details.Single(d => d.Category == "FuelEnergy").Amount.Should().Be(120m, "120 miles × 0.25 gal × $4");
			result.Details.Single(d => d.Category == "Maintenance").Amount.Should().Be(150m, "10 engine hours × $15");
			result.Details.Single(d => d.Category == "InsuranceLicensing").Amount.Should().Be(60m, "$4,000 ÷ 8,000 miles × 120 miles");
			result.Total.Should().Be(474m);
		}

		[Test]
		public void Resource_review_rules_flag_actual_fuel_double_counted_repairs_and_missing_windows()
		{
			var withFuel = FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = new ResourceUsageQuantity { Miles = 120, EngineHours = 10, Days = 1, ActualFuelCost = 95m }, Profile = Apparatus(), AsOf = AsOf });
			withFuel.Details.Single(d => d.Category == "FuelEnergy").Amount.Should().Be(95m, "an actual fuel cost replaces the modelled fuel");

			var excluded = FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = new ResourceUsageQuantity { Miles = 120, EngineHours = 10, Days = 1 }, Profile = Apparatus(), AsOf = AsOf, ExcludedComponentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "maint" } });
			excluded.NeedsReview.Should().BeTrue();
			excluded.ReviewReasons.Should().Contain(ResourceReviewReasons.RepairDoubleCounted);
			excluded.Details.Single(d => d.Category == "Maintenance").Blocked.Should().BeTrue();

			var rolling = Apparatus();
			rolling.Components.Single(c => c.ResourceCostComponentId == "maint").Source = (int)ResourceCostSources.WorkOrderRollingActual;
			var insufficient = FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = new ResourceUsageQuantity { Miles = 120, EngineHours = 10, Days = 1 }, Profile = rolling, AsOf = AsOf });
			insufficient.ReviewReasons.Should().Contain(ResourceReviewReasons.RollingWindowInsufficient);

			var noProfile = FieldCostCalculator.CalculateResource(new ResourceCostInput { Usage = new ResourceUsageQuantity { Miles = 10 }, Profile = null, AsOf = AsOf });
			noProfile.ReviewReasons.Should().Contain(ResourceReviewReasons.NoProfile);
			noProfile.Total.Should().Be(0m);
		}

		[Test]
		public void Distances_canonicalise_to_miles_and_usage_entries_derive_from_meters()
		{
			FieldCostCalculator.ToMiles(100m, "km").Should().Be(62.14m);
			FieldCostCalculator.ToMiles(100m, "mi").Should().Be(100m);
			var entry = new ResourceUsageEntry { StartOdometer = 1000, EndOdometer = 1100, DistanceUnit = "km", StartEngineMeter = 500.5m, EndEngineMeter = 510.5m };
			FieldCostingService.Canonicalize(entry);
			entry.OriginalDistance.Should().Be(100m);
			entry.CanonicalDistanceMiles.Should().Be(62.14m);
			entry.EngineHours.Should().Be(10m);
		}
	}
}
