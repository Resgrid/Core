using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Helpers
{
	/// <summary>
	/// Department link share flags are per direction: DepartmentShare* is what the creating department shares with the
	/// linked department, LinkedDepartmentShare* is what the linked department shares back. A viewer only reads the other
	/// side, only for the kinds that side shares toward it, and only over an enabled link.
	/// </summary>
	[TestFixture]
	public class DepartmentLinkSharingTests
	{
		private const int Creator = 100;
		private const int Linked = 200;
		private const int Outsider = 300;

		private static readonly DepartmentLinkShareTypes[] AllTypes =
		{
			DepartmentLinkShareTypes.Calls, DepartmentLinkShareTypes.Units, DepartmentLinkShareTypes.Personnel, DepartmentLinkShareTypes.Orders
		};

		private static DepartmentLink Link(bool enabled = true) =>
			new DepartmentLink { DepartmentLinkId = 9, DepartmentId = Creator, LinkedDepartmentId = Linked, LinkEnabled = enabled };

		private static void CreatorShares(DepartmentLink link, DepartmentLinkShareTypes type, bool value)
		{
			switch (type)
			{
				case DepartmentLinkShareTypes.Calls: link.DepartmentShareCalls = value; break;
				case DepartmentLinkShareTypes.Units: link.DepartmentShareUnits = value; break;
				case DepartmentLinkShareTypes.Personnel: link.DepartmentSharePersonnel = value; break;
				case DepartmentLinkShareTypes.Orders: link.DepartmentShareOrders = value; break;
			}
		}

		private static void LinkedShares(DepartmentLink link, DepartmentLinkShareTypes type, bool value)
		{
			switch (type)
			{
				case DepartmentLinkShareTypes.Calls: link.LinkedDepartmentShareCalls = value; break;
				case DepartmentLinkShareTypes.Units: link.LinkedDepartmentShareUnits = value; break;
				case DepartmentLinkShareTypes.Personnel: link.LinkedDepartmentSharePersonnel = value; break;
				case DepartmentLinkShareTypes.Orders: link.LinkedDepartmentShareOrders = value; break;
			}
		}

		private static DepartmentLink EverythingSharedBothWays(bool enabled = true)
		{
			var link = Link(enabled);
			foreach (var type in AllTypes)
			{
				CreatorShares(link, type, true);
				LinkedShares(link, type, true);
			}
			return link;
		}

		[TestCaseSource(nameof(AllTypes))]
		public void The_linked_department_reads_the_creator_only_for_what_the_creator_shares(DepartmentLinkShareTypes type)
		{
			var link = Link();
			CreatorShares(link, type, true);

			DepartmentLinkSharing.GetSharedDepartmentId(link, Linked, type).Should().Be(Creator);
		}

		[TestCaseSource(nameof(AllTypes))]
		public void The_linked_department_reads_nothing_the_creator_does_not_share(DepartmentLinkShareTypes type)
		{
			// Everything else is shared both ways, and the linked department shares this kind back: none of it counts.
			var link = EverythingSharedBothWays();
			CreatorShares(link, type, false);

			DepartmentLinkSharing.GetSharedDepartmentId(link, Linked, type).Should().BeNull();
		}

		[TestCaseSource(nameof(AllTypes))]
		public void The_creator_reads_the_linked_department_only_for_what_it_shares_back(DepartmentLinkShareTypes type)
		{
			var link = Link();
			LinkedShares(link, type, true);

			DepartmentLinkSharing.GetSharedDepartmentId(link, Creator, type).Should().Be(Linked);
		}

		[TestCaseSource(nameof(AllTypes))]
		public void The_creator_does_not_read_the_linked_department_through_its_own_share_flags(DepartmentLinkShareTypes type)
		{
			var link = EverythingSharedBothWays();
			LinkedShares(link, type, false);

			DepartmentLinkSharing.GetSharedDepartmentId(link, Creator, type).Should().BeNull();
		}

		[TestCaseSource(nameof(AllTypes))]
		public void A_disabled_link_shares_nothing(DepartmentLinkShareTypes type)
		{
			var link = EverythingSharedBothWays(enabled: false);

			DepartmentLinkSharing.GetSharedDepartmentId(link, Linked, type).Should().BeNull();
			DepartmentLinkSharing.GetSharedDepartmentId(link, Creator, type).Should().BeNull();
		}

		[TestCaseSource(nameof(AllTypes))]
		public void A_department_outside_the_link_reads_nothing(DepartmentLinkShareTypes type)
		{
			DepartmentLinkSharing.GetSharedDepartmentId(EverythingSharedBothWays(), Outsider, type).Should().BeNull();
			DepartmentLinkSharing.IsParty(EverythingSharedBothWays(), Outsider).Should().BeFalse();
		}

		[Test]
		public void A_missing_link_or_department_reads_nothing()
		{
			DepartmentLinkSharing.GetSharedDepartmentId(null, Linked, DepartmentLinkShareTypes.Calls).Should().BeNull();
			DepartmentLinkSharing.GetSharedDepartmentId(EverythingSharedBothWays(), 0, DepartmentLinkShareTypes.Calls).Should().BeNull();
			DepartmentLinkSharing.IsParty(null, Linked).Should().BeFalse();
			DepartmentLinkSharing.IsParty(EverythingSharedBothWays(), 0).Should().BeFalse();
		}

		[Test]
		public void A_link_to_itself_shares_nothing()
		{
			var link = EverythingSharedBothWays();
			link.LinkedDepartmentId = Creator;

			DepartmentLinkSharing.GetSharedDepartmentId(link, Creator, DepartmentLinkShareTypes.Calls).Should().BeNull();
		}

		[Test]
		public void Share_group_names_differ_per_department_kind_and_epoch_and_never_equal_the_department_group()
		{
			var names = new[]
			{
				DepartmentLinkSharing.GetShareGroupName(Creator, DepartmentLinkShareTypes.Calls, "a"),
				DepartmentLinkSharing.GetShareGroupName(Creator, DepartmentLinkShareTypes.Units, "a"),
				DepartmentLinkSharing.GetShareGroupName(Creator, DepartmentLinkShareTypes.Personnel, "a"),
				DepartmentLinkSharing.GetShareGroupName(Linked, DepartmentLinkShareTypes.Calls, "a"),
				DepartmentLinkSharing.GetShareGroupName(Creator, DepartmentLinkShareTypes.Calls, "b")
			};

			names.Should().OnlyHaveUniqueItems();
			names.Should().NotContain(Creator.ToString()).And.NotContain(Linked.ToString());
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("  ")]
		public void Without_an_epoch_there_is_no_share_group_to_fall_back_to(string accessVersion)
		{
			DepartmentLinkSharing.GetShareGroupName(Creator, DepartmentLinkShareTypes.Calls, accessVersion).Should().BeNull();
		}

		[Test]
		public void Disabling_changing_parties_or_turning_off_a_share_reduces_access()
		{
			DepartmentLinkSharing.ReducesAccess(EverythingSharedBothWays(), EverythingSharedBothWays(enabled: false)).Should().BeTrue("disabled");
			DepartmentLinkSharing.ReducesAccess(EverythingSharedBothWays(), null).Should().BeTrue("gone");

			var otherParty = EverythingSharedBothWays();
			otherParty.LinkedDepartmentId = Outsider;
			DepartmentLinkSharing.ReducesAccess(EverythingSharedBothWays(), otherParty).Should().BeTrue("parties changed");

			foreach (var type in AllTypes)
			{
				var narrowed = EverythingSharedBothWays();
				CreatorShares(narrowed, type, false);
				DepartmentLinkSharing.ReducesAccess(EverythingSharedBothWays(), narrowed).Should().BeTrue($"creator stopped sharing {type}");

				narrowed = EverythingSharedBothWays();
				LinkedShares(narrowed, type, false);
				DepartmentLinkSharing.ReducesAccess(EverythingSharedBothWays(), narrowed).Should().BeTrue($"linked department stopped sharing {type}");
			}
		}

		[Test]
		public void Accepting_widening_recoloring_or_saving_a_disabled_link_does_not_reduce_access()
		{
			DepartmentLinkSharing.ReducesAccess(Link(enabled: false), EverythingSharedBothWays()).Should().BeFalse("accepted");
			DepartmentLinkSharing.ReducesAccess(null, Link(enabled: false)).Should().BeFalse("new link");
			DepartmentLinkSharing.ReducesAccess(Link(), EverythingSharedBothWays()).Should().BeFalse("widened");
			DepartmentLinkSharing.ReducesAccess(Link(enabled: false), Link(enabled: false)).Should().BeFalse("still disabled");

			var recolored = EverythingSharedBothWays();
			recolored.DepartmentColor = "#ff0000";
			DepartmentLinkSharing.ReducesAccess(EverythingSharedBothWays(), recolored).Should().BeFalse("recolored");
		}

		[Test]
		public void The_other_department_is_the_side_the_viewer_is_not_on()
		{
			DepartmentLinkSharing.GetOtherDepartmentId(Link(), Creator).Should().Be(Linked);
			DepartmentLinkSharing.GetOtherDepartmentId(Link(), Linked).Should().Be(Creator);
			DepartmentLinkSharing.GetOtherDepartmentId(Link(), Outsider).Should().BeNull();
			DepartmentLinkSharing.GetOtherDepartmentId(null, Creator).Should().BeNull();
		}

		[Test]
		public void Unsubscribing_returns_only_the_groups_joined_for_that_link_and_not_still_needed_by_another()
		{
			IDictionary<object, object> items = new Dictionary<object, object>();

			DepartmentLinkSharing.TakeSubscription(items, 9).Should().BeEmpty("never subscribed");

			DepartmentLinkSharing.RecordSubscription(items, 9, new[] { "calls:a", "units:a" });
			DepartmentLinkSharing.RecordSubscription(items, 10, new[] { "units:a" });

			DepartmentLinkSharing.TakeSubscription(items, 9).Should().Equal("calls:a");
			DepartmentLinkSharing.TakeSubscription(items, 9).Should().BeEmpty("already left");
			DepartmentLinkSharing.TakeSubscription(items, 10).Should().Equal("units:a");
		}
	}
}
