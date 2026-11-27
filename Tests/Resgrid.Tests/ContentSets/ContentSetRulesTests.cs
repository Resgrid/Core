using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Services.ContentSets;

namespace Resgrid.Tests.ContentSets
{
	/// <summary>
	/// Round 10 A3, the pure rules: the Protocols and Training share kinds through <see cref="DepartmentLinkSharing"/> (direction, revocation,
	/// each side edits only its own flags), the lock/override merge, the update time schedule, the sponsor seat arithmetic, the sponsored gate
	/// and the baseline training "version".
	/// </summary>
	[TestFixture]
	public class ContentSetRulesTests
	{
		private static DepartmentLink Link(Action<DepartmentLink> set = null)
		{
			var link = new DepartmentLink { DepartmentLinkId = 3, DepartmentId = 10, LinkedDepartmentId = 20, LinkEnabled = true };
			set?.Invoke(link);
			return link;
		}

		[Test]
		public void The_new_share_kinds_follow_the_direction_aware_rule()
		{
			var creatorShares = Link(l => { l.DepartmentShareProtocols = true; l.DepartmentShareTraining = true; });
			DepartmentLinkSharing.GetSharedDepartmentId(creatorShares, 20, DepartmentLinkShareTypes.Protocols).Should().Be(10);
			DepartmentLinkSharing.GetSharedDepartmentId(creatorShares, 10, DepartmentLinkShareTypes.Protocols).Should().BeNull("the creator reads only what the linked side shares back");
			DepartmentLinkSharing.Shares(creatorShares, 10, 20, DepartmentLinkShareTypes.Training).Should().BeTrue();
			DepartmentLinkSharing.Shares(creatorShares, 20, 10, DepartmentLinkShareTypes.Training).Should().BeFalse();
			DepartmentLinkSharing.Shares(creatorShares, 10, 30, DepartmentLinkShareTypes.Protocols).Should().BeFalse("not a party");

			var linkedShares = Link(l => l.LinkedDepartmentShareProtocols = true);
			DepartmentLinkSharing.GetSharedDepartmentId(linkedShares, 10, DepartmentLinkShareTypes.Protocols).Should().Be(20);
			DepartmentLinkSharing.GetSharedDepartmentId(linkedShares, 20, DepartmentLinkShareTypes.Protocols).Should().BeNull();

			var disabled = Link(l => { l.DepartmentShareProtocols = true; l.LinkEnabled = false; });
			DepartmentLinkSharing.Shares(disabled, 10, 20, DepartmentLinkShareTypes.Protocols).Should().BeFalse();
			DepartmentLinkSharing.EventedShareTypes.Should().NotContain(DepartmentLinkShareTypes.Protocols, "protocols are read on request, never pushed");
		}

		[Test]
		public void Withdrawing_protocols_or_training_reduces_access_and_rotates()
		{
			var stored = Link(l => { l.DepartmentShareProtocols = true; l.LinkedDepartmentShareTraining = true; });
			DepartmentLinkSharing.ReducesAccess(stored, Link(l => l.LinkedDepartmentShareTraining = true)).Should().BeTrue();
			DepartmentLinkSharing.ReducesAccess(stored, Link(l => l.DepartmentShareProtocols = true)).Should().BeTrue();
			DepartmentLinkSharing.ReducesAccess(stored, Link(l => { l.DepartmentShareProtocols = true; l.LinkedDepartmentShareTraining = true; l.DepartmentShareCalls = true; }))
				.Should().BeFalse("adding a kind takes nothing away");
		}

		[Test]
		public void Each_side_sets_only_its_own_share_flags()
		{
			var link = Link(l => l.DepartmentShareCalls = true);
			DepartmentLinkSharing.SetSideShares(link, 20, false, false, false, false, true, true).Should().BeTrue();
			link.LinkedDepartmentShareProtocols.Should().BeTrue();
			link.LinkedDepartmentShareTraining.Should().BeTrue();
			link.DepartmentShareCalls.Should().BeTrue("the linked side never changes what the creator shares");
			DepartmentLinkSharing.SetSideShares(link, 10, false, false, false, false, false, false).Should().BeTrue();
			link.DepartmentShareCalls.Should().BeFalse();
			DepartmentLinkSharing.SetSideShares(link, 30, true, true, true, true, true, true).Should().BeFalse();
			DepartmentLinkSharing.SideShares(link, 20, DepartmentLinkShareTypes.Protocols).Should().BeTrue();
		}

		private static ProtocolContentDocument Doc(params (string Key, string Html)[] blocks)
		{
			var document = new ProtocolContentDocument();
			foreach (var (key, html) in blocks)
				document.Blocks.Add(new ProtocolContentBlock { Type = "html", Key = key, Html = html });
			return document;
		}

		[Test]
		public void The_merge_keeps_customizable_replacements_and_additions_and_never_overrides_a_locked_block()
		{
			var source = Doc(("b1", "<p>Clinical</p>"), ("b2", "<p>Phone</p>"));
			var local = new ContentSetLocalCustomization();
			local.Overrides["b1"] = "<p>Changed clinical</p>";
			local.Overrides["b2"] = "<p>Our phone</p>";
			local.Additions.Add("<p>Local note</p>");

			var customizable = ContentSetMerge.Merge(source, ContentSetLockModes.CustomizableBlocks, new[] { "b2" }, local, out var dropped).Render();
			customizable.Should().Be("<p>Clinical</p><p>Our phone</p><p>Local note</p>");
			dropped.Should().Equal("b1");

			var locked = ContentSetMerge.Merge(source, ContentSetLockModes.Locked, new[] { "b2" }, local, out var lockedDropped).Render();
			locked.Should().Be("<p>Clinical</p><p>Phone</p>", "locked content is the publisher's and takes no additions");
			lockedDropped.Should().Equal("b1", "b2");

			ContentSetMerge.Merge(source, ContentSetLockModes.Open, Array.Empty<string>(), local, out _).Render()
				.Should().Be("<p>Changed clinical</p><p>Our phone</p><p>Local note</p>");

			// The publisher removes the block a subscriber changed: the change is reported, never shown.
			ContentSetMerge.Merge(Doc(("b1", "<p>Clinical v2</p>")), ContentSetLockModes.CustomizableBlocks, new[] { "b2" }, local, out var gone).Render()
				.Should().Be("<p>Clinical v2</p><p>Local note</p>");
			gone.Should().Contain("b2");
		}

		[Test]
		public void Lock_rules_say_what_a_subscriber_may_do()
		{
			ContentSetMerge.CanDecline(ContentSetLockModes.Locked).Should().BeFalse();
			ContentSetMerge.CanDecline(ContentSetLockModes.CustomizableBlocks).Should().BeFalse();
			ContentSetMerge.CanDecline(ContentSetLockModes.Open).Should().BeTrue();
			ContentSetMerge.AllowsAdditions(ContentSetLockModes.Locked).Should().BeFalse();
			ContentSetMerge.ParseKeys(" b1, b2,,b1 ").Should().Equal("b1", "b2");
			ContentSetMerge.JoinKeys(new[] { "b2", "b2", " " }).Should().Be("b2");
			ContentSetMerge.ChangedBlockKeys(Doc(("a", "1"), ("b", "2")), Doc(("a", "1"), ("b", "3"), ("c", "4"))).Should().Equal("b", "c");
		}

		[Test]
		public void Updates_go_into_force_at_the_local_update_time_on_or_after_the_effective_date()
		{
			var morning = new DateTime(2026, 12, 1, 5, 0, 0, DateTimeKind.Utc);
			ContentSetSchedule.NextUpdateTime(morning, "UTC", 7 * 60).Should().Be(new DateTime(2026, 12, 1, 7, 0, 0, DateTimeKind.Utc));
			ContentSetSchedule.NextUpdateTime(morning.AddHours(3), "UTC", 7 * 60).Should().Be(new DateTime(2026, 12, 2, 7, 0, 0, DateTimeKind.Utc));

			// 07:00 in New York in December is 12:00 UTC (EST); a Windows id works as well as an IANA one.
			ContentSetSchedule.NextUpdateTime(morning, "Eastern Standard Time", 7 * 60).Should().Be(new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc));
			ContentSetSchedule.NextUpdateTime(morning, "America/New_York", 7 * 60).Should().Be(new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc));
			ContentSetSchedule.NextUpdateTime(morning, "Not/AZone", 7 * 60).Should().Be(new DateTime(2026, 12, 1, 7, 0, 0, DateTimeKind.Utc), "an unknown zone is UTC");

			var effective = morning.AddDays(5);
			ContentSetSchedule.ApplyTime(morning, effective, morning.AddDays(1), "UTC", 420).Should().Be(effective, "never before the publisher's effective date");
			ContentSetSchedule.ApplyTime(morning, effective, effective.AddDays(2), "UTC", 420).Should().Be(effective.AddDays(2));
			ContentSetSchedule.ApplyTime(morning, effective, null, "UTC", 420).Should().Be(effective.Date.AddHours(7));

			ContentSetSchedule.Deadline(ContentSetLockModes.Locked, effective, 30).Should().Be(effective.AddDays(30));
			ContentSetSchedule.Deadline(ContentSetLockModes.Open, effective, 30).Should().BeNull();
			ContentSetSchedule.Deadline(ContentSetLockModes.CustomizableBlocks, effective, 5000).Should().Be(effective.AddDays(ContentSet.MaxDeferDays));
		}

		[Test]
		public void Settings_parse_with_safe_defaults()
		{
			ContentSetSettings.Parse(null).UpdateTimeOfDayMinutes.Should().Be(420);
			ContentSetSettings.Parse("not json").NotifyManagers.Should().BeTrue();
			var round = ContentSetSettings.Parse(new ContentSetSettings { UpdateTimeOfDayMinutes = 1439, NotifyManagers = false }.ToJson());
			round.UpdateTimeOfDayMinutes.Should().Be(1439);
			round.NotifyManagers.Should().BeFalse();
			ContentSetSettings.Parse("{\"updateMinutes\":99999}").UpdateTimeOfDayMinutes.Should().Be(1439);
		}

		[Test]
		public void A_sponsored_department_gets_what_the_sponsors_packs_leave_after_its_own_members()
		{
			// 50 purchased; the sponsor uses 20, external learners 0, another sponsored department 10; this one holds 5 now.
			LmsSponsorshipRules.CapacityFor(50, 20, 0, 10, 5, null).Should().Be(20);
			LmsSponsorshipRules.CapacityFor(50, 20, 0, 10, 5, 8).Should().Be(8, "the seat limit caps it");
			LmsSponsorshipRules.CapacityFor(25, 30, 0, 0, 3, null).Should().Be(0, "the sponsor's own members come first");
			LmsSponsorshipRules.SponsoredSeats(new List<(int, int?)> { (12, 10), (4, null) }).Should().Be(14);

			var seats = Enumerable.Range(1, 3).Select(i => new LmsMemberSeat { DepartmentId = 1, UserId = "u" + i, EnabledOn = new DateTime(2026, 1, i) }).ToList();
			LmsSponsorshipRules.Licensed(seats, new HashSet<string>(new[] { "u1", "u2", "u3" }, StringComparer.OrdinalIgnoreCase), 2).Should().BeEquivalentTo(new[] { "u1", "u2" });
		}

		[Test]
		public void A_sponsored_course_runs_on_the_sponsors_add_on_and_lapses_from_when_the_sponsorship_stopped()
		{
			var stopped = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
			var gate = new LmsDepartmentGate
			{
				DepartmentId = 1, ModuleOn = true, FlagOn = true, AddonActive = false,
				SponsoredCourses = new Dictionary<int, DateTime?> { [7] = null, [8] = stopped }
			};
			gate.ForCourse(7).CanAuthor.Should().BeTrue("a paying sponsorship runs and assigns the copy");
			LmsLapseRules.CanAssignNew(gate.ForCourse(7), false).Should().BeTrue();
			LmsLapseRules.CanAssignNew(gate.ForCourse(9), false).Should().BeFalse("the department's own courses need its own add-on");
			LmsLapseRules.CanAssignNew(gate.ForCourse(8), false).Should().BeFalse("nothing new after a sponsorship stopped");
			gate.ForCourse(8).IsLapsed.Should().BeTrue();
			gate.ForCourse(8).LastEntitledOn.Should().Be(stopped);

			var assignedBefore = new LmsEnrollment { AssignedOn = stopped.AddDays(-2), DueOn = stopped.AddDays(10) };
			LmsLapseRules.CanRun(gate.ForCourse(8), false, assignedBefore, stopped.AddDays(5)).Should().BeTrue("assigned before the stop, takeable until due");
			LmsLapseRules.CanRun(gate.ForCourse(8), false, assignedBefore, stopped.AddDays(11)).Should().BeFalse();

			// A department on its own add-on is not affected, and a null gate stays null.
			var own = new LmsDepartmentGate { ModuleOn = true, FlagOn = true, AddonActive = true, SponsoredCourses = gate.SponsoredCourses };
			own.ForCourse(8).Should().BeSameAs(own);
			((LmsDepartmentGate)null).For(7).Should().BeNull();
		}

		[Test]
		public void A_training_version_changes_only_with_the_text_a_subscriber_receives()
		{
			var training = new Training { Name = "CPR", Description = "Adult", TrainingText = "<p>Push hard</p>", MinimumScore = 80, Hours = 2m };
			var version = TrainingSetItemSource.ContentVersion(training);
			version.Should().BePositive();
			training.UsersToAdd = "someone";
			TrainingSetItemSource.ContentVersion(training).Should().Be(version);
			training.TrainingText = "<p>Push hard and fast</p>";
			TrainingSetItemSource.ContentVersion(training).Should().NotBe(version);
		}
	}
}
