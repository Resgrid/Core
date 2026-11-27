using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Billing;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// The pack add-on price (protocols-training-knowledge-plan §17.4, decision 7): 25-member packs, graduated (each pack at its own
	/// tier's price, never the whole quantity at one tier's price), USD through Stripe and EUR through Paddle, floor one pack. The
	/// annual figures are the plan's own table, so a change to the tiers that breaks the published prices fails here.
	/// </summary>
	[TestFixture]
	public class PackPricingTests
	{
		[TestCase(1, 79)]
		[TestCase(4, 316)]
		[TestCase(5, 365)]
		[TestCase(20, 1100)]
		[TestCase(21, 1129)]
		[TestCase(40, 1680)]
		public void Usd_packs_are_priced_graduated_at_each_breakpoint(int packs, int monthly)
		{
			LmsPackPricing.Usd.MonthlyTotal(packs).Should().Be(monthly);
		}

		[TestCase(1, 99)]
		[TestCase(4, 396)]
		[TestCase(5, 455)]
		[TestCase(20, 1340)]
		[TestCase(21, 1375)]
		[TestCase(40, 2040)]
		public void Eur_packs_are_priced_graduated_at_each_breakpoint(int packs, int monthly)
		{
			LmsPackPricing.Eur.MonthlyTotal(packs).Should().Be(monthly);
		}

		[TestCase(1, 948, 1188)]
		[TestCase(2, 1896, 2376)]
		[TestCase(4, 3792, 4752)]
		[TestCase(10, 7320, 9000)]
		[TestCase(20, 13200, 16080)]
		[TestCase(40, 20160, 24480)]
		public void Twelve_months_match_the_plans_annual_cost_table(int packs, int usdYear, int eurYear)
		{
			(LmsPackPricing.Usd.MonthlyTotal(packs) * 12).Should().Be(usdYear);
			(LmsPackPricing.Eur.MonthlyTotal(packs) * 12).Should().Be(eurYear);
		}

		[Test]
		public void Graduated_is_not_volume_pricing()
		{
			var usd = LmsPackPricing.Usd;
			usd.MonthlyTotal(5).Should().NotBe(5 * 49m, "the first four packs keep the first tier's price when a fifth is added");
			usd.MonthlyTotal(21).Should().NotBe(21 * 29m);

			var lines = usd.Breakdown(21);
			lines.Select(l => (l.FirstPack, l.LastPack, l.Packs, l.UnitAmount)).Should().Equal((1, 4, 4, 79m), (5, 20, 16, 49m), (21, 21, 1, 29m));
			lines.Sum(l => l.Amount).Should().Be(1129m);
			usd.PriceOfPack(4).Should().Be(79m);
			usd.PriceOfPack(5).Should().Be(49m);
			usd.PriceOfPack(21).Should().Be(29m);
		}

		[TestCase(0, 1)]
		[TestCase(1, 1)]
		[TestCase(20, 1)]
		[TestCase(25, 1)]
		[TestCase(26, 2)]
		[TestCase(100, 4)]
		[TestCase(101, 5)]
		public void Members_round_up_to_whole_packs_never_below_the_floor(int members, int packs)
		{
			LmsPackPricing.Usd.PacksFor(members).Should().Be(packs);
		}

		[Test]
		public void The_schedule_sells_between_the_floor_and_the_ceiling()
		{
			var usd = LmsPackPricing.Usd;
			usd.PackSize.Should().Be(25);
			usd.SeatsFor(3).Should().Be(75);
			usd.IsValidQuantity(0).Should().BeFalse("the floor is one pack");
			usd.IsValidQuantity(1).Should().BeTrue();
			usd.IsValidQuantity(LmsAddonConfig.MaximumPacks).Should().BeTrue();
			usd.IsValidQuantity(LmsAddonConfig.MaximumPacks + 1).Should().BeFalse();
			usd.MonthlyTotal(0).Should().Be(0m);
		}

		[Test]
		public void Tiers_must_start_at_one_and_run_without_gaps()
		{
			FluentActions.Invoking(() => new PackPriceSchedule("USD", 25, 1, 10, new[] { new PackPriceTier(2, null, 10m) })).Should().Throw<ArgumentException>();
			FluentActions.Invoking(() => new PackPriceSchedule("USD", 25, 1, 10, new[] { new PackPriceTier(1, 3, 10m), new PackPriceTier(5, null, 5m) }))
				.Should().Throw<ArgumentException>();
			FluentActions.Invoking(() => new PackPriceSchedule("USD", 0, 1, 10, new[] { new PackPriceTier(1, null, 10m) })).Should().Throw<ArgumentOutOfRangeException>();
		}

		[Test]
		public void Workforce_pro_can_reuse_the_same_path_with_its_own_amounts()
		{
			// Shifts plan §7 (D23): $119 / $79 / $49 at the same breakpoints. Not sold yet; this pins that the path is generic.
			var workforce = PackPriceSchedule.Graduated("USD", 25, 1, 400, (1, 119m), (5, 79m), (21, 49m));
			workforce.MonthlyTotal(5).Should().Be(4 * 119m + 79m);
		}

		[Test]
		public void The_lms_config_holds_the_decided_pack_size_and_graces()
		{
			LmsAddonConfig.PackSize.Should().Be(25);
			LmsAddonConfig.MinimumPacks.Should().Be(1);
			LmsAddonConfig.SecondTierFirstPack.Should().Be(5);
			LmsAddonConfig.ThirdTierFirstPack.Should().Be(21);
			LmsAddonConfig.DunningGraceDays.Should().Be(7, "the platform's failed monthly renewal grace (PlanAddon.GetEndDateFromNow)");
			LmsAddonConfig.ReactivationGraceDays.Should().Be(14, "lapse rule 12's default");
			LmsAddonConfig.PlanAddonId.Should().Be(Resgrid.Providers.Migrations.Migrations.M0351_AddLmsPackBillingAndSeats.LmsAddonId)
				.And.Be(Resgrid.Providers.MigrationsPg.Migrations.M0351_AddLmsPackBillingAndSeatsPg.LmsAddonId);
		}

		#region Quantity change preview

		private static readonly DateTime PeriodStart = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
		private static readonly DateTime PeriodEnd = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

		[Test]
		public void Adding_packs_previews_next_months_price_and_the_prorated_charge_made_now()
		{
			var halfway = PeriodStart.AddDays(15);
			var quote = PackQuotes.Build(LmsPackPricing.Usd, 3, 5, 3, PeriodStart, PeriodEnd, halfway);

			quote.Refusal.Should().BeNull();
			quote.IsIncrease.Should().BeTrue();
			quote.CurrentMonthly.Should().Be(237m);
			quote.NextMonthly.Should().Be(365m);
			quote.MonthlyChange.Should().Be(128m);
			quote.Seats.Should().Be(125);
			quote.ProratedNow.Should().Be(64m, "half of the month is left, so half of the monthly difference is charged at once");
			quote.EffectiveOnUtc.Should().Be(halfway);
		}

		[Test]
		public void Removing_packs_takes_effect_at_the_next_billing_date_and_charges_nothing_now()
		{
			var quote = PackQuotes.Build(LmsPackPricing.Usd, 5, 3, 2, PeriodStart, PeriodEnd, PeriodStart.AddDays(10));

			quote.Refusal.Should().BeNull();
			quote.IsDecrease.Should().BeTrue();
			quote.NextMonthly.Should().Be(237m);
			quote.ProratedNow.Should().Be(0m);
			quote.EffectiveOnUtc.Should().Be(PeriodEnd);
		}

		[Test]
		public void Removing_packs_never_goes_below_the_members_who_are_lms_enabled()
		{
			// 60 enabled members need 3 packs: going from 5 to 2 is refused, 5 to 3 is not.
			PackQuotes.Build(LmsPackPricing.Usd, 5, 2, 3, PeriodStart, PeriodEnd, PeriodStart).Refusal.Should().Be(PackQuotes.BelowEnabledSeats);
			PackQuotes.Build(LmsPackPricing.Usd, 5, 3, 3, PeriodStart, PeriodEnd, PeriodStart).Refusal.Should().BeNull();
			PackQuotes.Build(LmsPackPricing.Usd, 1, 2, 3, PeriodStart, PeriodEnd, PeriodStart).Refusal.Should().BeNull("adding packs is never refused for seats");
		}

		[Test]
		public void A_quantity_outside_the_schedule_is_refused()
		{
			PackQuotes.Build(LmsPackPricing.Usd, 2, 0, 1, PeriodStart, PeriodEnd, PeriodStart).Refusal.Should().Be(PackQuotes.BelowMinimum);
			PackQuotes.Build(LmsPackPricing.Usd, 2, LmsAddonConfig.MaximumPacks + 1, 1, PeriodStart, PeriodEnd, PeriodStart).Refusal.Should().Be(PackQuotes.AboveMaximum);
		}

		[Test]
		public void A_first_purchase_is_charged_the_whole_first_month()
		{
			var now = DateTime.UtcNow;
			var quote = PackQuotes.Build(LmsPackPricing.Eur, 0, 2, 0, null, null, now);

			quote.CurrentMonthly.Should().Be(0m);
			quote.NextMonthly.Should().Be(198m);
			quote.ProratedNow.Should().Be(198m);
			quote.EffectiveOnUtc.Should().Be(now);
		}

		#endregion

		#region Catalog and the add-on card

		[Test]
		public void The_lms_card_is_a_monthly_pack_add_on_behind_its_operator_toggle()
		{
			var lms = AddonCatalog.Get(PlanAddonTypes.Lms);

			lms.IsPackAddon.Should().BeTrue();
			lms.PackSize.Should().Be(25);
			lms.FeatureFlag.Should().Be(FeatureFlagKeys.TrainingLms);
			lms.Period.Should().Be(AddonBillingPeriod.Monthly, "monthly only (R-L5)");
			lms.StripeAmount().Should().Be(79m, "the card shows the first pack's price");
			lms.PaddleAmount().Should().Be(99m);
			lms.Slug.Should().Be("lms");
			// Workforce Pro (2026-11-26) is the second pack add-on on the same billing path.
			AddonCatalog.All.Where(a => a.IsPackAddon).Select(a => a.Type).Should().Equal(PlanAddonTypes.Lms, PlanAddonTypes.WorkforcePro);
		}

		[TestCase(false, false, false)]
		[TestCase(false, true, false)]
		[TestCase(true, false, false)]
		[TestCase(true, true, true)]
		public void The_lms_card_shows_only_with_the_toggle_on_and_a_paid_base_plan(bool toggleOn, bool paidPlan, bool visible)
		{
			var card = new AddonAvailability { Definition = AddonCatalog.Get(PlanAddonTypes.Lms), Offered = toggleOn, Owned = false };

			card.IsVisible(paidPlan).Should().Be(visible);
		}

		[Test]
		public void A_held_lms_add_on_stays_manageable_after_its_toggle_or_plan_goes()
		{
			new AddonAvailability { Definition = AddonCatalog.Get(PlanAddonTypes.Lms), Offered = false, Owned = true }.IsVisible(false).Should().BeTrue();
		}

		[Test]
		public void The_lms_add_on_is_a_dedicated_monthly_add_on_with_a_name()
		{
			PlanAddon.IsDedicatedMonthlyAddon((int)PlanAddonTypes.Lms).Should().BeTrue("its own subscription; the shared plan paths must never cancel or emit for it");
			var addon = new PlanAddon { AddonType = (int)PlanAddonTypes.Lms };
			addon.GetAddonName().Should().Be("LMS");
			addon.GetEndDateFromNow().Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(1));
		}

		#endregion
	}
}
