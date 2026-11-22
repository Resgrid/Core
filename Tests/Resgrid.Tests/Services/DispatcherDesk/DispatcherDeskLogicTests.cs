using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>Dispatcher desk pure rules (M0324): duty windows, coverage, break staggering, routing order, config round trip.</summary>
	[TestFixture]
	public class DispatcherDeskLogicTests
	{
		private static readonly Department Eastern = new Department { DepartmentId = 7, TimeZone = "Eastern Standard Time" };

		// Thursday 19 November 2026 (EST, UTC-5).
		private static ShiftDayTimesRow Day(int shiftDayId, int day, string start = "7:00 AM", string end = "7:00 PM") => new ShiftDayTimesRow
		{
			ShiftId = 3,
			ShiftDayId = shiftDayId,
			Day = new DateTime(2026, 11, day),
			ShiftName = "Dispatch A",
			StartTime = start,
			EndTime = end
		};

		private static ShiftDispatcher Row(string userId, int? shiftDayId, ShiftDispatcherStatuses status = ShiftDispatcherStatuses.Active, bool supervisor = false,
			DateTime? addedOn = null) => new ShiftDispatcher
		{
			ShiftDispatcherId = Guid.NewGuid().ToString(),
			DepartmentId = 7,
			ShiftId = 3,
			ShiftDayId = shiftDayId,
			UserId = userId,
			Status = (int)status,
			Source = shiftDayId.HasValue ? (int)ShiftDispatcherSources.Signup : (int)ShiftDispatcherSources.Assigned,
			IsSupervisor = supervisor,
			AddedOn = addedOn ?? new DateTime(2026, 11, 1)
		};

		[Test]
		public void Standing_dispatchers_are_on_every_day_in_department_time()
		{
			var windows = DispatcherDutyCalculator.Build(new[] { Day(100, 19), Day(101, 20) }, new[] { Row("ann", null, supervisor: true) }, Eastern);

			windows.Should().HaveCount(2);
			windows[0].StartUtc.Should().Be(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc));
			windows[0].EndUtc.Should().Be(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
			windows[0].IsSupervisor.Should().BeTrue();
			windows[1].ShiftDayId.Should().Be(101);
		}

		[Test]
		public void A_denied_day_row_keeps_a_standing_dispatcher_off_that_day_only()
		{
			var windows = DispatcherDutyCalculator.Build(new[] { Day(100, 19), Day(101, 20) },
				new[] { Row("ann", null), Row("ann", 100, ShiftDispatcherStatuses.Denied) }, Eastern);

			windows.Should().ContainSingle().Which.ShiftDayId.Should().Be(101);
		}

		[Test]
		public void Pending_signups_are_listed_but_never_on_duty()
		{
			var windows = DispatcherDutyCalculator.Build(new[] { Day(100, 19) }, new[] { Row("bob", 100, ShiftDispatcherStatuses.PendingApproval) }, Eastern);

			windows.Should().ContainSingle().Which.IsPending.Should().BeTrue();
			DispatcherDutyCalculator.OnDuty(windows, new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc)).Should().BeEmpty();
		}

		[Test]
		public void The_latest_day_row_decides()
		{
			var windows = DispatcherDutyCalculator.Build(new[] { Day(100, 19) }, new[]
			{
				Row("bob", 100, ShiftDispatcherStatuses.Denied, addedOn: new DateTime(2026, 11, 2)),
				Row("bob", 100, ShiftDispatcherStatuses.Active, addedOn: new DateTime(2026, 11, 3))
			}, Eastern);

			windows.Should().ContainSingle().Which.IsPending.Should().BeFalse();
		}

		[Test]
		public void Overnight_shifts_run_into_the_next_morning()
		{
			var windows = DispatcherDutyCalculator.Build(new[] { Day(100, 19, "7:00 PM", "7:00 AM") }, new[] { Row("ann", null) }, Eastern);

			windows.Single().StartUtc.Should().Be(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
			windows.Single().EndUtc.Should().Be(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
			DispatcherDutyCalculator.OnDuty(windows, new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc)).Should().ContainSingle();
		}

		[Test]
		public void Coverage_counts_breaks_and_flags_the_minimum_and_overlaps()
		{
			var start = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
			var duty = new List<DispatcherDutyWindow>
			{
				new DispatcherDutyWindow { UserId = "ann", ShiftId = 3, StartUtc = start, EndUtc = start.AddHours(12) },
				new DispatcherDutyWindow { UserId = "bob", ShiftId = 3, StartUtc = start, EndUtc = start.AddHours(12) }
			};
			var breaks = new List<DispatcherBreak>
			{
				new DispatcherBreak { UserId = "ann", State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = start.AddHours(4), DurationMinutes = 30 },
				new DispatcherBreak { UserId = "bob", State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = start.AddHours(4).AddMinutes(15), DurationMinutes = 30 }
			};

			var coverage = DispatcherCoverageCalculator.Build(start, start.AddHours(12), 5, duty, breaks, _ => 1, start);

			coverage.Slots.Should().HaveCount(144);
			coverage.Slots.First().Available.Should().Be(2);
			coverage.Slots.Single(x => x.StartUtc == start.AddHours(4).AddMinutes(20)).OnBreak.Should().Be(2);

			coverage.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.BreaksOverlap &&
				x.StartUtc == start.AddHours(4).AddMinutes(15) && x.EndUtc == start.AddHours(4).AddMinutes(30) && x.UserIds.Count == 2);
			coverage.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.NoCoverage && x.Available == 0);
		}

		[Test]
		public void A_started_break_occupies_the_desk_from_its_real_start_for_its_length()
		{
			var now = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc);
			var taken = new DispatcherBreak
			{
				State = (int)DispatcherBreakStates.Active,
				ScheduledStartOn = now.AddMinutes(30),
				DurationMinutes = 60,
				StartedOn = now.AddMinutes(-10),
				ExpectedReturnOn = now.AddMinutes(50)
			};

			var window = taken.CoverageWindow(now);

			window.Value.Start.Should().Be(now.AddMinutes(-10));
			window.Value.End.Should().Be(now.AddMinutes(50), "a lunch started early is due back early: its length, not its planned window");
		}

		[Test]
		public void Planner_staggers_breaks_so_the_minimum_stays_on_the_desk()
		{
			var start = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
			var dispatchers = new[] { "ann", "bob", "cat" }
				.Select(x => new DispatcherDutyWindow { UserId = x, ShiftId = 3, ShiftDayId = 100, StartUtc = start, EndUtc = start.AddHours(12) }).ToList();
			var plan = new List<DispatcherBreakPlanEntry>
			{
				new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Break, DurationMinutes = 15 },
				new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 45, TargetOffsetMinutes = 300 }
			};

			// Three on duty, two must stay available: one break at a time.
			var placed = DispatcherBreakPlanner.Plan(dispatchers, plan, Enumerable.Empty<(string, DateTime, DateTime)>(), 2);

			placed.Should().HaveCount(6);
			placed.Should().OnlyContain(x => !x.BreaksMinimum);
			foreach (var a in placed)
				foreach (var b in placed.Where(x => !ReferenceEquals(x, a)))
				{
					var overlap = a.StartUtc < b.StartUtc.AddMinutes(b.DurationMinutes) && b.StartUtc < a.StartUtc.AddMinutes(a.DurationMinutes);
					overlap.Should().BeFalse($"{a.UserId} {a.StartUtc:t} and {b.UserId} {b.StartUtc:t} overlap");
				}

			placed.Where(x => x.BreakType == (int)DispatcherBreakTypes.Meal).Min(x => x.StartUtc).Should().Be(start.AddMinutes(300));
			placed.Should().OnlyContain(x => x.StartUtc >= start.AddMinutes(DispatcherBreakPlanner.EdgeMarginMinutes) &&
											 x.StartUtc.AddMinutes(x.DurationMinutes) <= start.AddHours(12).AddMinutes(-DispatcherBreakPlanner.EdgeMarginMinutes));
		}

		[Test]
		public void Planner_avoids_overlap_where_the_day_allows_even_when_the_minimum_would_permit_it()
		{
			// Found 2026-11-21: five on duty with a minimum of 1 let four meals start together at the same target, although the
			// shift's help text promises the plan staggers breaks "so they do not overlap".
			var start = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
			var dispatchers = new[] { "ann", "bob", "cat", "dan", "eve" }
				.Select(x => new DispatcherDutyWindow { UserId = x, ShiftId = 3, ShiftDayId = 100, StartUtc = start, EndUtc = start.AddHours(12) }).ToList();
			var plan = new List<DispatcherBreakPlanEntry> { new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, TargetOffsetMinutes = 300 } };

			var placed = DispatcherBreakPlanner.Plan(dispatchers, plan, Enumerable.Empty<(string, DateTime, DateTime)>(), 1);

			placed.Should().HaveCount(5);
			foreach (var a in placed)
				foreach (var b in placed.Where(x => !ReferenceEquals(x, a)))
					(a.StartUtc < b.StartUtc.AddMinutes(b.DurationMinutes) && b.StartUtc < a.StartUtc.AddMinutes(a.DurationMinutes)).Should().BeFalse($"{a.UserId} and {b.UserId} overlap");
			placed.Should().OnlyContain(x => Math.Abs((x.StartUtc - start.AddMinutes(300)).TotalMinutes) <= DispatcherBreakPlanner.NoOverlapDriftMinutes);
		}

		[Test]
		public void Planner_lets_breaks_overlap_within_the_minimum_rather_than_move_them_hours_away()
		{
			// Ten 60-minute meals cannot all fit without overlap within two hours of the target; the minimum (2 of 10) allows overlap.
			var start = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
			var dispatchers = Enumerable.Range(1, 10)
				.Select(x => new DispatcherDutyWindow { UserId = "d" + x, ShiftId = 3, ShiftDayId = 100, StartUtc = start, EndUtc = start.AddHours(12) }).ToList();
			var plan = new List<DispatcherBreakPlanEntry> { new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 60, TargetOffsetMinutes = 360 } };

			var placed = DispatcherBreakPlanner.Plan(dispatchers, plan, Enumerable.Empty<(string, DateTime, DateTime)>(), 2);

			placed.Should().HaveCount(10);
			placed.Should().OnlyContain(x => !x.BreaksMinimum);
			placed.Should().OnlyContain(x => Math.Abs((x.StartUtc - start.AddMinutes(360)).TotalMinutes) <= DispatcherBreakPlanner.NoOverlapDriftMinutes + 60,
				"once nobody-else-on-break times run out near the target, the plan overlaps within the minimum instead of drifting far");
		}

		[Test]
		public void Planning_part_way_through_a_shift_places_past_targets_from_now_on()
		{
			// A 24-hour day from 10:00; planned at 21:35 with targets at +2 h, +5 h and +8 h (all already past).
			var start = new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);
			var now = start.AddHours(11).AddMinutes(35);
			var dispatchers = new List<DispatcherDutyWindow> { new DispatcherDutyWindow { UserId = "ann", ShiftId = 3, ShiftDayId = 100, StartUtc = start, EndUtc = start.AddHours(24) } };
			var plan = new List<DispatcherBreakPlanEntry>
			{
				new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Break, DurationMinutes = 15, TargetOffsetMinutes = 120 },
				new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, TargetOffsetMinutes = 300 },
				new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Break, DurationMinutes = 15, TargetOffsetMinutes = 480 }
			};

			var placed = DispatcherBreakPlanner.Plan(dispatchers, plan, Enumerable.Empty<(string, DateTime, DateTime)>(), 0, now.AddMinutes(5));

			placed.Should().HaveCount(3, "past targets are moved up, not dropped");
			placed.Should().OnlyContain(x => x.StartUtc >= now.AddMinutes(5));
			placed.Select(x => x.BreakType).Should().Equal((int)DispatcherBreakTypes.Break, (int)DispatcherBreakTypes.Meal, (int)DispatcherBreakTypes.Break);
			var ordered = placed.OrderBy(x => x.StartUtc).ToList();
			for (var i = 1; i < ordered.Count; i++)
				ordered[i].StartUtc.Should().BeOnOrAfter(ordered[i - 1].StartUtc.AddMinutes(ordered[i - 1].DurationMinutes + DispatcherBreakPlanner.OwnGapMinutes));
		}

		[Test]
		public void Planner_works_around_breaks_already_on_the_day()
		{
			var start = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
			var dispatchers = new List<DispatcherDutyWindow> { new DispatcherDutyWindow { UserId = "bob", ShiftId = 3, ShiftDayId = 100, StartUtc = start, EndUtc = start.AddHours(12) } };
			var plan = new List<DispatcherBreakPlanEntry> { new DispatcherBreakPlanEntry { DurationMinutes = 30, TargetOffsetMinutes = 240 } };
			var existing = new[] { ("ann", start.AddMinutes(240), start.AddMinutes(270)) };

			// Two on duty (ann already has her break), one must stay: bob cannot overlap ann. The service passes the minimum less
			// those on duty who are not being planned (1 − 1 = 0), so the one planned dispatcher may be on break alone.
			var placed = DispatcherBreakPlanner.Plan(dispatchers, plan, existing, 0);

			placed.Should().ContainSingle();
			placed[0].StartUtc.Should().Be(start.AddMinutes(270));
			placed[0].BreaksMinimum.Should().BeFalse();
		}

		[Test]
		public void Planner_flags_every_break_when_the_minimum_leaves_nobody_free_to_go()
		{
			// Two on duty and both must stay available: any break at all goes below the minimum. Breaks are still placed, apart.
			var start = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
			var dispatchers = new[] { "ann", "bob" }
				.Select(x => new DispatcherDutyWindow { UserId = x, ShiftId = 3, ShiftDayId = 100, StartUtc = start, EndUtc = start.AddHours(12) }).ToList();
			var plan = new List<DispatcherBreakPlanEntry> { new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, TargetOffsetMinutes = 300 } };

			var placed = DispatcherBreakPlanner.Plan(dispatchers, plan, Enumerable.Empty<(string, DateTime, DateTime)>(), 2);

			placed.Should().HaveCount(2);
			placed.Should().OnlyContain(x => x.BreaksMinimum, "no placement keeps two of two on the desk");
			(placed[0].StartUtc < placed[1].StartUtc.AddMinutes(30) && placed[1].StartUtc < placed[0].StartUtc.AddMinutes(30)).Should().BeFalse();
		}

		[Test]
		public void Busy_is_derived_from_held_work()
		{
			var presence = new DispatcherPresence { State = (int)DispatcherPresenceStates.Available };

			DispatcherRoutingPolicy.DisplayState(presence, 0, 1).Should().Be(DispatcherDisplayStates.Available);
			DispatcherRoutingPolicy.DisplayState(presence, 1, 1).Should().Be(DispatcherDisplayStates.Busy);
			DispatcherRoutingPolicy.DisplayState(presence, 1, 2).Should().Be(DispatcherDisplayStates.Available);
			DispatcherRoutingPolicy.DisplayState(new DispatcherPresence { State = (int)DispatcherPresenceStates.OnBreak }, 0, 1).Should().Be(DispatcherDisplayStates.OnBreak);
			DispatcherRoutingPolicy.DisplayState(null, 0, 1).Should().Be(DispatcherDisplayStates.Offline);
		}

		[Test]
		public void Routable_needs_duty_availability_capacity_and_no_open_offer()
		{
			var now = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc);
			DispatcherDeskMember Member(Action<DispatcherDeskMember> change = null)
			{
				var member = new DispatcherDeskMember
				{
					UserId = "ann",
					IsOnDuty = true,
					Capacity = 1,
					Presence = new DispatcherPresence { State = (int)DispatcherPresenceStates.Available, LastSeenOn = now.AddMinutes(-30) }
				};
				change?.Invoke(member);
				return member;
			}

			DispatcherRoutingPolicy.IsRoutable(Member(), now, 0).Should().BeTrue();
			DispatcherRoutingPolicy.IsRoutable(Member(x => x.IsOnDuty = false), now, 0).Should().BeFalse();
			DispatcherRoutingPolicy.IsRoutable(Member(x => x.ActiveItemCount = 1), now, 0).Should().BeFalse();
			DispatcherRoutingPolicy.IsRoutable(Member(x => x.OfferedItemCount = 1), now, 0).Should().BeFalse();
			DispatcherRoutingPolicy.IsRoutable(Member(x => x.Presence.State = (int)DispatcherPresenceStates.Away), now, 0).Should().BeFalse();
			DispatcherRoutingPolicy.IsRoutable(Member(), now, 10).Should().BeFalse("seen 30 minutes ago with a 10 minute heartbeat timeout");
		}

		[Test]
		public void Least_loaded_then_longest_idle_is_offered_first()
		{
			var members = new List<DispatcherDeskMember>
			{
				new DispatcherDeskMember { UserId = "ann", Name = "Ann", Presence = new DispatcherPresence { LastOfferedOn = new DateTime(2026, 11, 19, 15, 0, 0) } },
				new DispatcherDeskMember { UserId = "bob", Name = "Bob", Presence = new DispatcherPresence { LastOfferedOn = new DateTime(2026, 11, 19, 14, 0, 0) } },
				new DispatcherDeskMember { UserId = "cat", Name = "Cat", ActiveItemCount = 1, Presence = new DispatcherPresence() }
			};

			DispatcherRoutingPolicy.Order(members).Select(x => x.UserId).Should().Equal("bob", "ann", "cat");
		}

		[Test]
		public void Conversation_keys_normalize_numbers_and_join_voice_with_text()
		{
			DispatcherRoutingPolicy.ConversationKey(DispatcherQueueItemKinds.Text, "(555) 123-4567")
				.Should().Be(DispatcherRoutingPolicy.ConversationKey(DispatcherQueueItemKinds.Voice, "+15551234567"));
			DispatcherRoutingPolicy.ConversationKey(DispatcherQueueItemKinds.Email, "Caller@Example.com").Should().Be("5:caller@example.com");
			DispatcherRoutingPolicy.ConversationKey(DispatcherQueueItemKinds.Text, " ").Should().BeNull();
		}

		[Test]
		public void Config_round_trips_false_and_zero_over_true_and_non_zero_defaults()
		{
			var config = new DispatcherDeskConfig
			{
				Enabled = true,
				RequireShift = false,
				RoutePendingCalls = false,
				RouteEmail = false,
				SuppressAlertsOnBreak = false,
				NotifySupervisorsOnOverdueBreak = false,
				SignOffAtShiftEnd = false,
				AcknowledgeTexts = false,
				MinimumAvailableDispatchers = 0,
				MissedOffersBeforeAway = 0,
				BreakReminderMinutes = 0,
				QueueEscalationMinutes = 0,
				AutoReturnAfterMinutes = 10,
				EmailCallMode = (int)DispatcherEmailCallModes.Pending,
				DefaultBreakPlan = new List<DispatcherDeskBreakPlanEntry>
				{
					new DispatcherDeskBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, TargetOffsetMinutes = 0 }
				}
			}.Normalize();

			var read = ObjectSerialization.Deserialize<DispatcherDeskConfig>(ObjectSerialization.Serialize(config));

			read.RequireShift.Should().BeFalse();
			read.RoutePendingCalls.Should().BeFalse();
			read.RouteEmail.Should().BeFalse();
			read.SuppressAlertsOnBreak.Should().BeFalse();
			read.NotifySupervisorsOnOverdueBreak.Should().BeFalse();
			read.SignOffAtShiftEnd.Should().BeFalse();
			read.AcknowledgeTexts.Should().BeFalse();
			read.MinimumAvailableDispatchers.Should().Be(0);
			read.MissedOffersBeforeAway.Should().Be(0);
			read.BreakReminderMinutes.Should().Be(0);
			read.QueueEscalationMinutes.Should().Be(0);
			read.AutoReturnAfterMinutes.Should().Be(10);
			read.GetEmailCallMode().Should().Be(DispatcherEmailCallModes.Pending);
			read.GetDefaultBreakPlan().Should().ContainSingle().Which.TargetOffsetMinutes.Should().Be(0);
		}

		[Test]
		public void A_new_config_is_off_and_dispatches_email_at_once()
		{
			var config = ObjectSerialization.Deserialize<DispatcherDeskConfig>(ObjectSerialization.Serialize(new DispatcherDeskConfig()));

			config.Enabled.Should().BeFalse();
			config.Routes(DispatcherQueueItemKinds.Text).Should().BeFalse();
			config.GetEmailCallMode().Should().Be(DispatcherEmailCallModes.AutoDispatch);
			config.RequireShift.Should().BeTrue();
			config.EffectiveOfferTimeoutSeconds().Should().Be(DispatcherDeskConfig.DefaultOfferTimeoutSeconds);
			config.EffectiveAutoReturnAfterMinutes().Should().Be(0);
		}

		[Test]
		public void Normalize_clamps_out_of_range_values()
		{
			var config = new DispatcherDeskConfig { OfferTimeoutSeconds = 3, MaxActiveItemsPerDispatcher = 99, EmailCallMode = 42, AutoReturnAfterMinutes = 100000 }.Normalize();

			config.OfferTimeoutSeconds.Should().Be(15);
			config.MaxActiveItemsPerDispatcher.Should().Be(10);
			config.EmailCallMode.Should().Be((int)DispatcherEmailCallModes.AutoDispatch);
			config.AutoReturnAfterMinutes.Should().Be(480);
		}

		[Test]
		public void Preview_flattens_a_calls_rich_text_nature_and_keeps_texted_angle_brackets()
		{
			// The web call form stores the nature as editor HTML; the desk list and the offer alert show plain text.
			DispatcherQueueItem.TruncatePreview("<p>Dispatcher desk QA walk.</p><p>Do <strong>not</strong> dispatch.</p>")
				.Should().Be("Dispatcher desk QA walk. Do not dispatch.");
			DispatcherQueueItem.TruncatePreview("pt <unconscious> bp 80").Should().Be("pt <unconscious> bp 80");
			DispatcherQueueItem.TruncatePreview("<p><br></p>").Should().BeNull();
			DispatcherQueueItem.TruncatePreview("<p>" + new string('a', 600) + "</p>").Should().HaveLength(DispatcherQueueItem.PreviewMaxLength).And.EndWith("…");
		}
	}
}
