using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model.Invoicing;
using Resgrid.Services.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The pure contractor billing calculator (Workforce &amp; Business Operations plan, C4 steps 1–7; decisions 16, 17, 21).
	/// Every rule is exercised on a hand-built graph so the arithmetic is pinned independently of any repository.
	/// </summary>
	[TestFixture]
	public class ContractorChargeCalculatorTests
	{
		private static readonly DateTime Day = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

		#region Builders

		private static RateScheduleEntry Hourly(string id, decimal deployment, decimal? ot1 = null, decimal? ot2 = null, decimal? standby = null, string cert = null, string groupKey = null, int? crew = null, int type = (int)RateEntryTypes.PersonnelCertification)
		{
			var entry = new RateScheduleEntry { RateScheduleEntryId = id, Name = id, EntryType = type, BillingBasis = (int)BillingBases.Hourly, CertificationCode = cert, GroupKey = groupKey, CrewSize = crew, IsActive = true };
			entry.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Deployment, Rate = deployment, ThresholdStartHours = 0 });
			if (ot1.HasValue) entry.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Overtime1, Rate = ot1.Value, ThresholdStartHours = 8 });
			if (ot2.HasValue) entry.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Overtime2, Rate = ot2.Value, ThresholdStartHours = 12 });
			if (standby.HasValue) entry.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.Standby, Rate = standby.Value });
			return entry;
		}

		private static DeploymentTimeEntry Span(int subjectType, string subjectId, DeploymentTimeEntryTypes type, int startHour, int endHour, int unpaidBreak = 0, int? crew = null, decimal? km = null, decimal? litres = null, bool agencyMeals = false)
		{
			var e = new DeploymentTimeEntry { DeploymentTimeEntryId = Guid.NewGuid().ToString(), DeploymentTimeReportId = "r1", SubjectType = subjectType, EntryType = (int)type, StartTime = Day.AddHours(startHour), EndTime = Day.AddHours(endHour), UnpaidBreakMinutes = unpaidBreak, CrewSizeSnapshot = crew, MileageKm = km, FuelDeductionLitres = litres, AgencySuppliedMeals = agencyMeals };
			if (subjectType == (int)DeploymentTimeSubjectTypes.Personnel) e.DeploymentPersonnelId = subjectId;
			else if (subjectType == (int)DeploymentTimeSubjectTypes.Unit) e.DeploymentUnitId = subjectId;
			else e.DeploymentEquipmentId = subjectId;
			return e;
		}

		private static ContractorChargeInput Input(RateSchedule schedule, params DeploymentTimeEntry[] entries)
		{
			var report = new DeploymentTimeReport { DeploymentTimeReportId = "r1", ReportNumber = 7, ReportDate = Day, IncidentNumber = "INC-1", Status = (int)DeploymentTimeReportStatuses.Approved, Entries = entries.ToList() };
			return new ContractorChargeInput
			{
				Deployment = new Deployment { DeploymentId = "d1", DepartmentId = 1, Currency = "USD" },
				Schedule = schedule,
				Reports = new List<DeploymentTimeReport> { report },
				Personnel = new List<DeploymentPersonnel> { new DeploymentPersonnel { DeploymentPersonnelId = "p1", UserId = "u1", RateScheduleEntryId = "fft2", DeploymentUnitId = "unit1", AddedOn = Day.AddDays(-1) }, new DeploymentPersonnel { DeploymentPersonnelId = "p2", UserId = "u2", CertificationCode = "ENGB", DeploymentUnitId = "unit1", AddedOn = Day.AddDays(-1) } },
				Units = new List<DeploymentUnit> { new DeploymentUnit { DeploymentUnitId = "unit1", UnitId = 1, RateScheduleEntryId = "crew3" } },
				Equipment = new List<DeploymentEquipment> { new DeploymentEquipment { DeploymentEquipmentId = "eq1", RateScheduleEntryId = "pump" } },
				SubjectNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["p1"] = "Alvarez", ["p2"] = "Chen", ["unit1"] = "Engine 6", ["eq1"] = "Pump" }
			};
		}

		private static RateSchedule Schedule(params RateScheduleEntry[] entries) => new RateSchedule { RateScheduleId = "s1", Currency = "USD", PolicyJson = new RateSchedulePolicy().ToJson(), Entries = entries.ToList() };

		#endregion

		[Test]
		public void Hourly_day_splits_across_deployment_and_overtime_thresholds_after_unpaid_breaks_and_rounding()
		{
			var schedule = Schedule(Hourly("fft2", 40, 60, 80));
			// 06:00–19:20 with a 30-minute unpaid break = 12h50m → 13h at 30-minute rounding: 8h @ 40 + 4h @ 60 + 1h @ 80.
			var set = ContractorChargeCalculator.Calculate(Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 6, 19).With(e => { e.EndTime = Day.AddHours(19).AddMinutes(20); e.UnpaidBreakMinutes = 30; })));
			var line = set.Lines.Single(l => l.Kind == ContractorChargeKinds.Hourly);
			line.Bands.Select(b => (b.BandType, b.Hours, b.Rate)).Should().BeEquivalentTo(new[] { ((int)RateBandTypes.Deployment, 8m, 40m), ((int)RateBandTypes.Overtime1, 4m, 60m), ((int)RateBandTypes.Overtime2, 1m, 80m) });
			line.Amount.Should().Be(320 + 240 + 80);
			line.Description.Should().StartWith("2026-09-18 — DTR #7 — Incident INC-1 — fft2 — Alvarez — 13h (");
			set.SubTotal.Should().Be(640);
		}

		[Test]
		public void No_clear_8_carry_over_starts_the_day_in_the_overtime_band_and_standby_never_earns_overtime()
		{
			var schedule = Schedule(Hourly("fft2", 40, 60, 80, standby: 20));
			var input = Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 6, 12), Span(0, "p1", DeploymentTimeEntryTypes.Standby, 12, 22));
			input.Reports[0].NoClear8 = true;
			var line = ContractorChargeCalculator.Calculate(input).Lines.Single(l => l.Kind == ContractorChargeKinds.Hourly);
			// 6h of deployment start at the OT1 threshold (8): 4h OT1 (8→12) then 2h OT2; 10h standby flat.
			line.Bands.Should().ContainEquivalentOf(new { BandType = (int)RateBandTypes.Overtime1, Hours = 4m, Rate = 60m }, o => o.ExcludingMissingMembers());
			line.Bands.Should().ContainEquivalentOf(new { BandType = (int)RateBandTypes.Overtime2, Hours = 2m, Rate = 80m }, o => o.ExcludingMissingMembers());
			line.Bands.Should().ContainEquivalentOf(new { BandType = (int)RateBandTypes.Standby, Hours = 10m, Rate = 20m }, o => o.ExcludingMissingMembers());
			line.Amount.Should().Be(240 + 160 + 200);
		}

		[Test]
		public void Consecutive_basis_splits_overtime_per_continuous_run_while_daily_total_splits_once()
		{
			var schedule = Schedule(Hourly("fft2", 40, 60));
			// Two 6-hour runs separated by 3 hours: consecutive basis → no overtime; daily total → 12h = 8 + 4 OT1.
			var consecutive = ContractorChargeCalculator.Calculate(Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 4, 10), Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 13, 19)));
			consecutive.Lines.Single().Bands.Should().OnlyContain(b => b.BandType == (int)RateBandTypes.Deployment);
			consecutive.SubTotal.Should().Be(12 * 40);

			schedule.PolicyJson = new RateSchedulePolicy { OvertimeBasis = OvertimeBases.DailyTotalHours }.ToJson();
			var total = ContractorChargeCalculator.Calculate(Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 4, 10), Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 13, 19)));
			total.Lines.Single().Bands.Should().ContainEquivalentOf(new { BandType = (int)RateBandTypes.Overtime1, Hours = 4m }, o => o.ExcludingMissingMembers());
			total.SubTotal.Should().Be(8 * 40 + 4 * 60);
		}

		[Test]
		public void Travel_is_capped_and_bills_flat_unless_portal_to_portal()
		{
			var schedule = Schedule(Hourly("fft2", 40, 60));
			schedule.PolicyJson = new RateSchedulePolicy { TravelDayCapHours = 10 }.ToJson();
			var set = ContractorChargeCalculator.Calculate(Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Travel, 0, 14), Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 14, 18)));
			set.Warnings.Should().Contain(w => w.Code == ContractorChargeWarningCodes.TravelCapped);
			var line = set.Lines.Single();
			line.Bands.Should().ContainEquivalentOf(new { Label = "Travel", Hours = 10m, Rate = 40m }, o => o.ExcludingMissingMembers());
			line.Bands.Should().ContainEquivalentOf(new { Label = "Deployment", Hours = 4m }, o => o.ExcludingMissingMembers());
			line.Bands.Should().NotContain(b => b.BandType == (int)RateBandTypes.Overtime1, "travel never accrues overtime");

			schedule.PolicyJson = new RateSchedulePolicy { TravelDayCapHours = 10, PortalToPortal = true }.ToJson();
			var portal = ContractorChargeCalculator.Calculate(Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Travel, 0, 14), Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 14, 18))).Lines.Single();
			portal.Bands.Sum(b => b.Hours).Should().Be(14);
			portal.Bands.Should().ContainEquivalentOf(new { BandType = (int)RateBandTypes.Overtime1, Hours = 6m }, o => o.ExcludingMissingMembers());
		}

		[Test]
		public void Minimums_lift_the_day_to_cancellation_unsafe_stand_down_and_daily_guarantee_hours()
		{
			var schedule = Schedule(Hourly("fft2", 40, 60));
			schedule.PolicyJson = new RateSchedulePolicy { DailyGuaranteeHours = 6 }.ToJson();
			ContractorChargeCalculator.Calculate(Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 8, 10))).SubTotal.Should().Be(6 * 40, "a mobilized day bills the guarantee");

			var cancellation = Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 8, 9));
			cancellation.CancellationDate = Day;
			schedule.PolicyJson = new RateSchedulePolicy { CancellationMinimumHours = 4 }.ToJson();
			ContractorChargeCalculator.Calculate(cancellation).SubTotal.Should().Be(4 * 40);

			var unsafeDay = Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 8, 9));
			unsafeDay.Reports[0].UnsafeConditionsStandDown = true;
			schedule.PolicyJson = new RateSchedulePolicy { UnsafeStandDownHours = 8 }.ToJson();
			ContractorChargeCalculator.Calculate(unsafeDay).SubTotal.Should().Be(8 * 40);
		}

		[Test]
		public void Unit_bills_at_the_filled_crew_size_and_falls_back_to_the_nearest_lower_sibling_with_a_warning()
		{
			var schedule = Schedule(
				Hourly("crew3", 300, groupKey: "t6", crew: 3, type: (int)RateEntryTypes.Crew),
				Hourly("crew4", 380, groupKey: "t6", crew: 4, type: (int)RateEntryTypes.Crew),
				Hourly("crew5", 450, groupKey: "t6", crew: 5, type: (int)RateEntryTypes.Crew));
			var exact = ContractorChargeCalculator.Calculate(Input(schedule, Span(1, "unit1", DeploymentTimeEntryTypes.Deployment, 8, 12, crew: 4)));
			exact.Lines.Single().RateScheduleEntryId.Should().Be("crew4");
			exact.Warnings.Should().BeEmpty();

			var fallback = ContractorChargeCalculator.Calculate(Input(schedule, Span(1, "unit1", DeploymentTimeEntryTypes.Deployment, 8, 12, crew: 6)));
			fallback.Lines.Single().RateScheduleEntryId.Should().Be("crew5");
			fallback.Warnings.Should().Contain(w => w.Code == ContractorChargeWarningCodes.CrewSizeFallback);

			// No snapshot on the entries: the seated roster (two people on unit1) decides; 2 is below the family → pinned entry + warning.
			var roster = ContractorChargeCalculator.Calculate(Input(schedule, Span(1, "unit1", DeploymentTimeEntryTypes.Deployment, 8, 12)));
			roster.Lines.Single().RateScheduleEntryId.Should().Be("crew3");
			roster.Warnings.Should().Contain(w => w.Code == ContractorChargeWarningCodes.CrewSizeFallback);
		}

		[Test]
		public void Daily_subjects_pick_deployment_or_standby_tier_by_hours_and_cancellation_bills_a_full_day_per_policy()
		{
			var pump = new RateScheduleEntry { RateScheduleEntryId = "pump", Name = "Pump", EntryType = (int)RateEntryTypes.Equipment, BillingBasis = (int)BillingBases.Daily, IsActive = true };
			pump.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.DailyDeployment, Rate = 100, DailyTierMinHours = 0, DailyTierMaxHours = 4, Label = "Half day" });
			pump.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.DailyDeployment, Rate = 180, DailyTierMinHours = 4.01m, Label = "Full day" });
			pump.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.DailyStandby, Rate = 50 });
			var schedule = Schedule(pump);

			ContractorChargeCalculator.Calculate(Input(schedule, Span(2, "eq1", DeploymentTimeEntryTypes.Deployment, 8, 11))).Lines.Single().Amount.Should().Be(100);
			ContractorChargeCalculator.Calculate(Input(schedule, Span(2, "eq1", DeploymentTimeEntryTypes.Deployment, 8, 18))).Lines.Single().Amount.Should().Be(180);
			ContractorChargeCalculator.Calculate(Input(schedule, Span(2, "eq1", DeploymentTimeEntryTypes.Standby, 8, 18))).Lines.Single().Amount.Should().Be(50);

			// The cancellation-day DTR lists the pump with a one-hour standby span and no deployment hours.
			var cancelled = Input(schedule, Span(2, "eq1", DeploymentTimeEntryTypes.Standby, 8, 9));
			cancelled.CancellationDate = Day;
			ContractorChargeCalculator.Calculate(cancelled).Lines.Single().Bands.Single().Rate.Should().Be(100, "a cancellation day with no deployment hours bills the deployment day at the lowest tier");
			schedule.PolicyJson = new RateSchedulePolicy { CancellationVehiclesFullDay = false }.ToJson();
			ContractorChargeCalculator.Calculate(cancelled).Lines.Single().Amount.Should().Be(50, "vehicles fall back to the standby day when the policy withholds the full day");
		}

		[Test]
		public void Premiums_stack_per_band_and_never_multiply()
		{
			var schedule = Schedule(Hourly("fft2", 40, 60));
			schedule.Premiums.Add(new RatePremium { RatePremiumId = "night", Name = "Night", DeploymentAdder = 5, Overtime1Adder = 7.5m, IsActive = true });
			schedule.Premiums.Add(new RatePremium { RatePremiumId = "lead", Name = "Lead", DeploymentAdder = 3, Overtime1Adder = 3, IsActive = true });
			var input = Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 6, 16));
			input.Personnel[0].PremiumIdsJson = JsonConvert.SerializeObject(new[] { "night", "lead" });
			var set = ContractorChargeCalculator.Calculate(input);
			set.Lines.Where(l => l.Kind == ContractorChargeKinds.Premium).Should().HaveCount(2);
			set.Lines.Single(l => l.RatePremiumId == "night").Amount.Should().Be(8 * 5 + 2 * 7.5m);
			set.Lines.Single(l => l.RatePremiumId == "lead").Amount.Should().Be(8 * 3 + 2 * 3);
			set.SubTotal.Should().Be(8 * 40 + 2 * 60 + 55 + 30);
		}

		[Test]
		public void Out_of_province_per_diem_needs_the_flag_and_air_travel_when_the_band_requires_it()
		{
			var entry = Hourly("fft2", 40);
			entry.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.OutOfProvincePerPersonDaily, Rate = 75, RequiresAirTravel = true, Label = "OOP" });
			var schedule = Schedule(entry);
			var input = Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 8, 12));
			ContractorChargeCalculator.Calculate(input).Lines.Should().NotContain(l => l.Kind == ContractorChargeKinds.OutOfProvince);
			input.Deployment.OutOfProvince = true;
			ContractorChargeCalculator.Calculate(input).Lines.Should().NotContain(l => l.Kind == ContractorChargeKinds.OutOfProvince, "the band requires air travel");
			input.Deployment.TravelViaAir = true;
			var line = ContractorChargeCalculator.Calculate(input).Lines.Single(l => l.Kind == ContractorChargeKinds.OutOfProvince);
			line.Amount.Should().Be(75);
			line.Taxable.Should().BeFalse();
		}

		[Test]
		public void Mileage_subtracts_free_units_and_fuel_deducts_per_litre_from_the_policy()
		{
			var pump = Hourly("pump", 10, type: (int)RateEntryTypes.Vehicle);
			pump.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.MileagePerKm, Rate = 0.5m, FreeUnitsPerDay = 100 });
			var schedule = Schedule(pump);
			schedule.PolicyJson = new RateSchedulePolicy { FuelDeductionRatePerLitre = 1.2m }.ToJson();
			var set = ContractorChargeCalculator.Calculate(Input(schedule, Span(2, "eq1", DeploymentTimeEntryTypes.Deployment, 8, 10, km: 260, litres: 50)));
			set.Lines.Single(l => l.Kind == ContractorChargeKinds.Mileage).Amount.Should().Be(160 * 0.5m);
			set.Lines.Single(l => l.Kind == ContractorChargeKinds.FuelDeduction).Amount.Should().Be(-60);
			set.SubTotal.Should().Be(20 + 80 - 60);
		}

		[Test]
		public void Billable_expenses_pass_through_with_per_diem_validation_warnings()
		{
			var entry = Hourly("fft2", 40);
			entry.Bands.Add(new RateScheduleEntryBand { BandType = (int)RateBandTypes.PerDiemMeal, Rate = 18, MealCode = "B" });
			var schedule = Schedule(entry);
			schedule.PolicyJson = new RateSchedulePolicy { MealEligibility = new List<MealEligibilityWindow> { new MealEligibilityWindow { MealCode = "B", StartsBeforeMinutes = 7 * 60 } } }.ToJson();
			var input = Input(schedule, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 9, 17, agencyMeals: true));
			input.Expenses = new List<DeploymentExpense>
			{
				new DeploymentExpense { DeploymentExpenseId = "x1", DeploymentTimeReportId = "r1", ExpenseDate = Day, ExpenseType = (int)DeploymentExpenseTypes.PerDiemMeal, MealCode = "B", Amount = 20, Billable = true },
				new DeploymentExpense { DeploymentExpenseId = "x2", ExpenseDate = Day, ExpenseType = (int)DeploymentExpenseTypes.Ferry, Amount = 45, Billable = true, Description = "Ferry crossing" },
				new DeploymentExpense { DeploymentExpenseId = "x3", ExpenseDate = Day, ExpenseType = (int)DeploymentExpenseTypes.Fuel, Amount = 99, Billable = false }
			};
			var set = ContractorChargeCalculator.Calculate(input);
			var expenses = set.Lines.Where(l => l.Kind == ContractorChargeKinds.Expense).ToList();
			expenses.Should().HaveCount(2);
			expenses.Should().OnlyContain(l => !l.Taxable);
			expenses.Single(l => l.DeploymentExpenseId == "x2").Description.Should().Contain("Ferry — Ferry crossing");
			set.Warnings.Select(w => w.Code).Should().Contain(new[] { ContractorChargeWarningCodes.PerDiemMismatch, ContractorChargeWarningCodes.PerDiemIneligible, ContractorChargeWarningCodes.PerDiemAgencyMeals });
		}

		[Test]
		public void Missing_schedule_or_entry_warns_instead_of_charging()
		{
			var none = Input(null, Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 8, 12));
			var set = ContractorChargeCalculator.Calculate(none);
			set.HasCharges.Should().BeFalse();
			set.Warnings.Should().ContainSingle(w => w.Code == ContractorChargeWarningCodes.ScheduleMissing);

			var byCode = ContractorChargeCalculator.Calculate(Input(Schedule(Hourly("engb", 55, cert: "ENGB")), Span(0, "p2", DeploymentTimeEntryTypes.Deployment, 8, 12)));
			byCode.Lines.Single().RateScheduleEntryId.Should().Be("engb", "a person without a pinned entry matches the certification code");

			var unmatched = ContractorChargeCalculator.Calculate(Input(Schedule(Hourly("crew3", 300, type: (int)RateEntryTypes.Crew)), Span(0, "p2", DeploymentTimeEntryTypes.Deployment, 8, 12)));
			unmatched.HasCharges.Should().BeFalse();
			unmatched.Warnings.Should().ContainSingle(w => w.Code == ContractorChargeWarningCodes.RateEntryMissing && w.SubjectId == "p2");
		}

		[Test]
		public void Discount_snapshot_reduces_the_total_before_tax()
		{
			var input = Input(Schedule(Hourly("fft2", 100)), Span(0, "p1", DeploymentTimeEntryTypes.Deployment, 8, 12));
			input.DiscountPercent = 10;
			var set = ContractorChargeCalculator.Calculate(input);
			set.SubTotal.Should().Be(400);
			set.DiscountAmount.Should().Be(40);
			set.TotalBeforeTax.Should().Be(360);
			set.ReportIds.Should().Equal("r1");
		}

		[Test]
		public void Rounding_and_split_helpers_behave()
		{
			ContractorChargeCalculator.Round(7.74m, 30).Should().Be(7.5m);
			ContractorChargeCalculator.Round(7.75m, 30).Should().Be(8m);
			ContractorChargeCalculator.Round(7.74m, 0).Should().Be(7.74m);
			ContractorChargeCalculator.Split(10, 0, 8, 12).Should().Equal((RateBandTypes.Deployment, 8m), (RateBandTypes.Overtime1, 2m));
			ContractorChargeCalculator.Split(6, 0, null, null).Should().Equal((RateBandTypes.Deployment, 6m));
			ContractorChargeCalculator.Split(6, 8, 8, null).Should().Equal((RateBandTypes.Overtime1, 6m));
		}
	}

	internal static class TestEntryExtensions
	{
		public static DeploymentTimeEntry With(this DeploymentTimeEntry entry, Action<DeploymentTimeEntry> apply) { apply(entry); return entry; }
	}
}
