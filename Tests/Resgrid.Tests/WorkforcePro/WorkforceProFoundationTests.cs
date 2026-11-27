using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Billing;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Tests.WorkforcePro
{
	/// <summary>
	/// The Workforce Pro add-on foundation (shifts plan §7.2, D23): PlanAddonTypes 8 on the dedicated pack path, the catalog row id the
	/// Billing API knows, the graduated schedule ($119 / $79 / $49, €139 / €95 / €59 at packs 1–4 / 5–20 / 21+, which int-CommonApis must match
	/// exactly or checkout fails closed), the add-ons page card, and the gate's access matrix.
	/// </summary>
	[TestFixture]
	public class WorkforceProFoundationTests
	{
		[Test]
		public void Workforce_Pro_is_add_on_type_8_on_the_dedicated_pack_path()
		{
			((int)PlanAddonTypes.WorkforcePro).Should().Be(8);
			PlanAddon.IsDedicatedMonthlyAddon(8).Should().BeTrue();
			new PlanAddon { AddonType = 8 }.GetAddonName().Should().Be("Workforce Pro");
			WorkforceProAddonConfig.PlanAddonId.Should().Be("65516545-bf65-47ce-9e3a-abaf18409d88");
			Resgrid.Providers.Migrations.Migrations.M0422_AddWorkforceProAddon.WorkforceProAddonId.Should().Be(WorkforceProAddonConfig.PlanAddonId);
			Resgrid.Providers.MigrationsPg.Migrations.M0422_AddWorkforceProAddonPg.WorkforceProAddonId.Should().Be(WorkforceProAddonConfig.PlanAddonId);
			Resgrid.Providers.Migrations.Migrations.M0422_AddWorkforceProAddon.FlagKey.Should().Be(FeatureFlagKeys.WorkforcePro).And.Be("Workforce.Pro");
		}

		[TestCase(1, 119)]
		[TestCase(4, 476)]
		[TestCase(5, 555)]
		[TestCase(20, 1740)]
		[TestCase(21, 1789)]
		[TestCase(40, 2720)]
		public void The_USD_schedule_is_graduated_at_the_4_5_and_20_21_boundaries(int packs, int monthly)
		{
			var usd = WorkforceProPackPricing.Usd;
			usd.Currency.Should().Be("USD");
			usd.PackSize.Should().Be(25);
			usd.MinimumPacks.Should().Be(1);
			usd.MaximumPacks.Should().Be(400);
			usd.Tiers.Select(t => (t.FirstPack, t.UnitAmount)).Should().Equal((1, 119m), (5, 79m), (21, 49m));
			usd.MonthlyTotal(packs).Should().Be(monthly);
		}

		[TestCase(1, 139)]
		[TestCase(4, 556)]
		[TestCase(5, 651)]
		[TestCase(20, 2076)]
		[TestCase(21, 2135)]
		public void The_EUR_schedule_is_graduated_at_the_same_boundaries(int packs, int monthly)
		{
			var eur = WorkforceProPackPricing.Eur;
			eur.Currency.Should().Be("EUR");
			eur.Tiers.Select(t => (t.FirstPack, t.UnitAmount)).Should().Equal((1, 139m), (5, 95m), (21, 59m));
			eur.MonthlyTotal(packs).Should().Be(monthly);
			WorkforceProPackPricing.ForCurrency("eur").Currency.Should().Be("EUR");
			WorkforceProPackPricing.ForCurrency("USD").Currency.Should().Be("USD");
		}

		[Test]
		public void The_add_ons_page_card_is_a_monthly_pack_add_on_behind_its_operator_toggle()
		{
			var card = AddonCatalog.Get(PlanAddonTypes.WorkforcePro);

			card.IsPackAddon.Should().BeTrue();
			card.PackSize.Should().Be(25);
			card.FeatureFlag.Should().Be(FeatureFlagKeys.WorkforcePro);
			card.Period.Should().Be(AddonBillingPeriod.Monthly);
			card.StripeAmount().Should().Be(119m, "the card shows the first pack's price");
			card.PaddleAmount().Should().Be(139m);
			card.Slug.Should().Be("workforce-pro");
			new AddonAvailability { Definition = card, Offered = true, Owned = false }.IsVisible(hasPaidPlan: false).Should().BeFalse("sold with a paid base plan");
			new AddonAvailability { Definition = card, Offered = false, Owned = false }.IsVisible(hasPaidPlan: true).Should().BeFalse("seeded dark");
			new AddonAvailability { Definition = card, Offered = true, Owned = false }.IsVisible(hasPaidPlan: true).Should().BeTrue();
		}

		private static WorkforceProAccess Access(bool offered, bool licensed, params string[] seats) => new WorkforceProAccess
		{
			DepartmentId = 1,
			Offered = offered,
			State = licensed ? WorkforceProSubscriptionStates.Active : WorkforceProSubscriptionStates.None,
			Packs = licensed ? 1 : 0,
			PackSize = 25,
			SeatHolderUserIds = new System.Collections.Generic.HashSet<string>(seats, StringComparer.OrdinalIgnoreCase)
		};

		[Test]
		public void The_access_matrix_flag_off_hides_lapsed_reads_and_licensed_edits_only_seat_holders()
		{
			var off = Access(false, true, "a");
			off.Level.Should().Be(WorkforceProAccessLevels.None);
			off.Invoking(a => a.EnsureReadable()).Should().Throw<WorkforceProAccessException>().Which.IsUnavailable.Should().BeTrue();
			off.HoldsSeat("a").Should().BeFalse();

			var lapsed = Access(true, false, "a");
			lapsed.Level.Should().Be(WorkforceProAccessLevels.ReadOnly);
			lapsed.Invoking(a => a.EnsureReadable()).Should().NotThrow();
			lapsed.Invoking(a => a.EnsureWritable()).Should().Throw<WorkforceProAccessException>().Which.Code.Should().Be(WorkforceProCodes.Required);
			lapsed.Invoking(a => a.EnsureWritableFor("a")).Should().Throw<WorkforceProAccessException>().Which.Code.Should().Be(WorkforceProCodes.Required);
			lapsed.PurchasedSeats.Should().Be(0);

			var licensed = Access(true, true, "a");
			licensed.Level.Should().Be(WorkforceProAccessLevels.Full);
			licensed.Invoking(a => a.EnsureWritable()).Should().NotThrow();
			licensed.Invoking(a => a.EnsureWritableFor("A")).Should().NotThrow();
			licensed.Invoking(a => a.EnsureWritableFor("b")).Should().Throw<WorkforceProAccessException>().Which.Code.Should().Be(WorkforceProCodes.SeatRequired);
			licensed.Invoking(a => a.EnsureWritableFor(null)).Should().Throw<WorkforceProAccessException>();
			// Every refusal is a MemberWellnessException, so the wellness controllers map it to their existing error shapes.
			licensed.Invoking(a => a.EnsureWritableFor("b")).Should().Throw<MemberWellnessException>();
		}

		[Test]
		public void The_seat_rules_count_active_members_in_enable_order()
		{
			var now = DateTime.UtcNow;
			var seats = new[]
			{
				new WorkforceProSeat { WorkforceProSeatId = 3, UserId = "c", EnabledOn = now.AddMinutes(-1) },
				new WorkforceProSeat { WorkforceProSeatId = 1, UserId = "a", EnabledOn = now.AddMinutes(-3) },
				new WorkforceProSeat { WorkforceProSeatId = 2, UserId = "b", EnabledOn = now.AddMinutes(-2) },
				new WorkforceProSeat { WorkforceProSeatId = 4, UserId = "gone", EnabledOn = now.AddMinutes(-9) }
			};
			var active = new System.Collections.Generic.HashSet<string>(new[] { "a", "b", "c" });

			WorkforceProSeatRules.CountedSeats(seats, active).Select(s => s.UserId).Should().Equal("a", "b", "c");
			WorkforceProSeatRules.Licensed(seats, active, 2, true).Should().BeEquivalentTo(new[] { "a", "b" });
			WorkforceProSeatRules.Licensed(seats, active, 25, false).Should().BeEmpty();
			WorkforceProSeatRules.PacksNeeded(26, 25, 25).Should().Be(1);
			WorkforceProSeatRules.PacksNeeded(25, 25, 25).Should().Be(0);
			WorkforceProSeatRules.MinimumPacks(0, 25, 1).Should().Be(1);
			WorkforceProSeatRules.MinimumPacks(51, 25, 1).Should().Be(3);
		}
	}
}
