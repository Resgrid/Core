using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Timers key on a status's meaning. A status with no meaning, or a dispatch status whose meaning has no timer, is never
	/// timed, and the Big Board then reports every unit as within its timers (customer report, 2026-10-17).
	/// </summary>
	[TestFixture]
	public class UnitStatusTimerCoverageTests
	{
		private const int SetId = 41;
		private const int Telefonisch = 4101;
		private const int Uitgerukt = 4102;
		private const int TerPlaatse = 4103;
		private const int Radiofonisch = 4104;

		private static UnitStatusThresholds Timers(params ActionBaseTypes[] baseTypes) => new UnitStatusThresholds
		{
			Thresholds = baseTypes.Select(x => new UnitStatusThreshold { BaseType = (int)x, WarnSeconds = 240, AlertSeconds = 480 }).ToList()
		};

		private static CustomState UnitSet(int telefonischMeaning = -1)
		{
			var set = new CustomState { CustomStateId = SetId, Type = (int)CustomStateTypes.Unit, Name = "Statussen", Details = new Collection<CustomStateDetail>() };
			set.Details.Add(new CustomStateDetail { CustomStateDetailId = Telefonisch, CustomStateId = SetId, ButtonText = "Telefonisch", BaseType = telefonischMeaning, Order = 0 });
			set.Details.Add(new CustomStateDetail { CustomStateDetailId = Uitgerukt, CustomStateId = SetId, ButtonText = "Uitgerukt", BaseType = (int)ActionBaseTypes.Responding, Order = 1 });
			set.Details.Add(new CustomStateDetail { CustomStateDetailId = TerPlaatse, CustomStateId = SetId, ButtonText = "Ter plaatse", BaseType = (int)ActionBaseTypes.OnScene, Order = 2 });
			set.Details.Add(new CustomStateDetail { CustomStateDetailId = Radiofonisch, CustomStateId = SetId, ButtonText = "Radiofonisch", BaseType = (int)ActionBaseTypes.Available, Order = 3 });
			return set;
		}

		private static List<UnitType> EmsTypes() => new List<UnitType>
		{
			new UnitType { UnitTypeId = 1, Type = "MUG", CustomStatesId = SetId },
			new UnitType { UnitTypeId = 2, Type = "PIT", CustomStatesId = SetId },
			new UnitType { UnitTypeId = 3, Type = "Ziekenwagen", CustomStatesId = SetId }
		};

		private static List<UnitTypeCallStatusOverride> DispatchTo(int option) =>
			EmsTypes().Select(t => new UnitTypeCallStatusOverride { UnitTypeId = t.UnitTypeId, DispatchStatus = option, ReleaseStatus = Radiofonisch }).ToList();

		[Test]
		public void Without_timers_nothing_is_reported()
		{
			UnitStatusTimerCoverage.Check(new UnitStatusThresholds(), EmsTypes(), new[] { UnitSet() }, StatusTarget.Default, DispatchTo(Telefonisch))
				.IsEmpty.Should().BeTrue();
		}

		[Test]
		public void Dispatching_to_a_status_with_no_meaning_is_reported_once_with_every_unit_type()
		{
			var report = UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Dispatched), EmsTypes(), new[] { UnitSet() }, StatusTarget.Default, DispatchTo(Telefonisch));

			var untimed = report.UntimedDispatchStatuses.Should().ContainSingle().Subject;
			untimed.Option.ButtonText.Should().Be("Telefonisch");
			untimed.HasMeaning.Should().BeFalse();
			untimed.UnitTypes.Select(x => x.Type).Should().Equal("MUG", "PIT", "Ziekenwagen");
			report.UnitTypesWithUntimedDispatchStatus.Should().Be(3);

			var set = report.SetsWithoutMeanings.Should().ContainSingle().Subject;
			set.Options.Select(x => x.ButtonText).Should().Equal("Telefonisch");
			set.UnitTypes.Should().HaveCount(3);
		}

		[Test]
		public void Giving_the_dispatch_status_a_timed_meaning_clears_the_report()
		{
			UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Dispatched), EmsTypes(), new[] { UnitSet((int)ActionBaseTypes.Dispatched) }, StatusTarget.Default, DispatchTo(Telefonisch))
				.IsEmpty.Should().BeTrue();
		}

		[Test]
		public void A_dispatch_meaning_without_a_timer_is_reported()
		{
			var report = UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.OnScene), EmsTypes(), new[] { UnitSet((int)ActionBaseTypes.Dispatched) }, StatusTarget.Default, DispatchTo(Telefonisch));

			var untimed = report.UntimedDispatchStatuses.Should().ContainSingle().Subject;
			untimed.HasMeaning.Should().BeTrue();
			untimed.BaseType.Should().Be((int)ActionBaseTypes.Dispatched);
			report.SetsWithoutMeanings.Should().BeEmpty();
		}

		[Test]
		public void Without_an_override_the_department_wide_target_is_resolved_against_the_set()
		{
			// Default dispatch looks for Dispatched, then Responding, then Enroute: here that is Uitgerukt (Responding).
			UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Responding), EmsTypes(), new[] { UnitSet() }, StatusTarget.Default, null)
				.UntimedDispatchStatuses.Should().BeEmpty();

			var report = UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.OnScene), EmsTypes(), new[] { UnitSet() }, StatusTarget.Default, null);

			report.UntimedDispatchStatuses.Should().ContainSingle().Which.Option.ButtonText.Should().Be("Uitgerukt");
		}

		[Test]
		public void An_override_naming_an_option_outside_the_set_falls_back_to_the_department_target()
		{
			var report = UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Responding), EmsTypes(), new[] { UnitSet() }, StatusTarget.Default, DispatchTo(99999));

			report.UntimedDispatchStatuses.Should().BeEmpty();
		}

		[Test]
		public void Built_in_statuses_carry_their_meaning()
		{
			var types = new List<UnitType> { new UnitType { UnitTypeId = 9, Type = "Engine" } };

			UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Responding), types, null, StatusTarget.Default, null).IsEmpty.Should().BeTrue();

			var report = UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Dispatched), types, null, StatusTarget.Default, null);
			var untimed = report.UntimedDispatchStatuses.Should().ContainSingle().Subject;
			untimed.Option.Should().BeNull();
			untimed.BuiltInStatus.Should().Be((int)UnitStateTypes.Responding);
		}

		[Test]
		public void A_dispatch_meaning_the_timers_grid_does_not_offer_is_not_reported()
		{
			// Dispatched to an "available" meaning: nobody meant that to be timed.
			UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Dispatched), EmsTypes(), new[] { UnitSet() }, StatusTarget.Default, DispatchTo(Radiofonisch))
				.UntimedDispatchStatuses.Should().BeEmpty();
		}

		[Test]
		public void Leaving_the_status_unchanged_on_dispatch_writes_nothing_to_report()
		{
			var types = new List<UnitType> { new UnitType { UnitTypeId = 9, Type = "Engine" } };

			UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.OnScene), types, null, StatusTarget.LeaveUnchanged, null)
				.UntimedDispatchStatuses.Should().BeEmpty();
		}

		[Test]
		public void Only_unit_sets_a_unit_type_uses_and_active_options_count()
		{
			var used = UnitSet((int)ActionBaseTypes.Dispatched);
			used.Details.Add(new CustomStateDetail { CustomStateDetailId = 4199, CustomStateId = SetId, ButtonText = "Oud", BaseType = -1, IsDeleted = true });
			var unused = new CustomState { CustomStateId = 77, Type = (int)CustomStateTypes.Unit, Name = "Unused", Details = new Collection<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = 7701, CustomStateId = 77, ButtonText = "Nothing", BaseType = -1 }
			} };
			var personnel = new CustomState { CustomStateId = 78, Type = (int)CustomStateTypes.Personnel, Name = "Personnel", Details = new Collection<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = 7801, CustomStateId = 78, ButtonText = "Pager", BaseType = -1 }
			} };

			var report = UnitStatusTimerCoverage.Check(Timers(ActionBaseTypes.Dispatched), EmsTypes(), new[] { used, unused, personnel }, StatusTarget.Default, DispatchTo(Telefonisch));

			report.IsEmpty.Should().BeTrue();
		}
	}
}
