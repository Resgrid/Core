using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// Dispatcher breaks (M0324) when something else changes the break between a read and the write that acts on it (PR #589):
	/// timer alerts and the missed mark are conditional on the plan they read, a re-plan resets its reminders in the same write,
	/// auto return ends only the break it evaluated, a whole-day re-plan commits as one change, a break start takes the department
	/// lock, work that could not be handed back at the start of a break is handed back by the timer, and self-scheduled breaks
	/// must sit inside a duty window on a shift that allows them.
	/// </summary>
	[TestFixture]
	public class DispatcherBreaksConcurrencyTests
	{
		private const int Dept = 7;

		private sealed class Harness
		{
			public DateTime Now = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc);
			public readonly InMemoryDispatcherDeskStore Store = new InMemoryDispatcherDeskStore();
			public readonly DispatcherDeskConfig Config = new DispatcherDeskConfig { Enabled = true, MinimumAvailableDispatchers = 0, BreakReminderMinutes = 15 };
			public readonly List<DispatcherDutyWindow> Duty = new List<DispatcherDutyWindow>();
			public readonly List<(string Kind, string UserId, string BreakId)> Pushes = new List<(string, string, string)>();
			public readonly Dictionary<int, ShiftDispatchSetting> Settings = new Dictionary<int, ShiftDispatchSetting>();
			public readonly HashSet<string> Supervisors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public readonly List<string> SupervisorsOnDuty = new List<string>();
			public readonly Mock<IDispatcherRoutingService> Routing = new Mock<IDispatcherRoutingService>();
			public readonly Mock<IDispatcherPresenceService> Presence = new Mock<IDispatcherPresenceService>();
			public DispatcherDeskSnapshot Desk = new DispatcherDeskSnapshot();
			/// <summary>Whether the desk push accepts the break alerts (false: the provider refused or failed).</summary>
			public bool Delivers = true;
			public Mock<IUnitOfWork> UnitOfWork;
			public DispatcherBreaksService Breaks;

			public Harness(bool withUnitOfWork = false)
			{
				var config = new Mock<IDispatcherDeskConfigService>();
				config.Setup(x => x.GetConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => Config);

				var shiftDispatchers = new Mock<IShiftDispatchersService>();
				shiftDispatchers.Setup(x => x.GetDutyWindowsAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
					.ReturnsAsync((int d, DateTime f, DateTime t) => Duty.Where(x => x.EndUtc > f && x.StartUtc < t).ToList());
				shiftDispatchers.Setup(x => x.GetSettingsForDepartmentAsync(Dept)).ReturnsAsync(() => Settings.Values.ToList());
				shiftDispatchers.Setup(x => x.GetSettingsForShiftAsync(It.IsAny<int>())).ReturnsAsync((int id) => Settings.TryGetValue(id, out var s) ? s : null);
				shiftDispatchers.Setup(x => x.GetDayRosterAsync(Dept, It.IsAny<int>()))
					.ReturnsAsync((int d, int day) => Duty.Where(x => x.ShiftDayId == day).ToList());

				Presence.Setup(x => x.CanSuperviseAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string u) => u != null && Supervisors.Contains(u));
				Presence.Setup(x => x.GetDeskAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(() => Desk);
				Presence.Setup(x => x.GetSupervisorUserIdsAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(() => SupervisorsOnDuty.ToList());

				var notifier = new Mock<IDispatcherDeskNotifier>();
				notifier.Setup(x => x.NotifyBreakReminderAsync(It.IsAny<DispatcherBreak>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.Callback((DispatcherBreak b, bool due, CancellationToken c) => Pushes.Add((due ? "due" : "reminder", b.UserId, b.DispatcherBreakId)))
					.ReturnsAsync(() => Delivers);
				notifier.Setup(x => x.NotifyBreakOverAsync(It.IsAny<DispatcherBreak>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.Callback((DispatcherBreak b, IEnumerable<string> s, bool auto, CancellationToken c) =>
						Pushes.Add((s == null ? (auto ? "auto-returned" : "break-over") : "supervisors:" + string.Join(",", s), b.UserId, b.DispatcherBreakId)))
					.ReturnsAsync(() => Delivers);

				Routing.Setup(x => x.RouteDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

				if (withUnitOfWork)
				{
					UnitOfWork = new Mock<IUnitOfWork>();
					UnitOfWork.Setup(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DbConnection)null);
				}

				Breaks = new DispatcherBreaksService(Store.BreaksRepository, Store.PresencesRepository, Store.ItemsRepository, shiftDispatchers.Object,
					Presence.Object, config.Object, notifier.Object, new Lazy<IDispatcherRoutingService>(() => Routing.Object), UnitOfWork?.Object)
				{ UtcNow = () => Now };
			}

			public void OnDuty(string user, int shiftId = 3, int shiftDayId = 100, bool selfScheduling = true)
			{
				Duty.Add(new DispatcherDutyWindow { UserId = user, ShiftId = shiftId, ShiftDayId = shiftDayId, ShiftName = "Dispatch", StartUtc = Now.AddHours(-4), EndUtc = Now.AddHours(8) });
				Settings[shiftId] = new ShiftDispatchSetting { ShiftId = shiftId, DepartmentId = Dept, IsEnabled = true, AllowSelfScheduledBreaks = selfScheduling };
			}

			public DispatcherPresence SignedIn(string user, DispatcherPresenceStates state = DispatcherPresenceStates.Available) =>
				Store.AddPresence(Dept, user, state, Now);

			public DispatcherBreak Planned(string user, DateTime start, int minutes = 15, int? shiftId = 3)
			{
				var row = new DispatcherBreak
				{
					DispatcherBreakId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = user, ShiftId = shiftId, ShiftDayId = shiftId.HasValue ? 100 : (int?)null,
					BreakType = (int)DispatcherBreakTypes.Break, State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = start, DurationMinutes = minutes,
					CreatedOn = Now.AddDays(-2)
				};
				Store.Breaks.Add(row);
				return row;
			}

			public DispatcherBreak Active(string user, DateTime started, int minutes = 15)
			{
				var row = new DispatcherBreak
				{
					DispatcherBreakId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = user, BreakType = (int)DispatcherBreakTypes.Break,
					State = (int)DispatcherBreakStates.Active, DurationMinutes = minutes, StartedOn = started, ExpectedReturnOn = started.AddMinutes(minutes), CreatedOn = started
				};
				Store.Breaks.Add(row);
				return row;
			}

			public DispatcherQueueItem Held(string id, string user)
			{
				var item = new DispatcherQueueItem
				{
					DispatcherQueueItemId = id, DepartmentId = Dept, Kind = (int)DispatcherQueueItemKinds.Text, State = (int)DispatcherQueueItemStates.Accepted,
					AssignedUserId = user, CreatedOn = Now.AddMinutes(-5), LastActivityOn = Now.AddMinutes(-5)
				};
				Store.Items.Add(item);
				return item;
			}

			public DispatcherBreak Stored(string id) => Store.Breaks.Single(x => x.DispatcherBreakId == id);

			/// <summary>Runs <paramref name="change"/> once, just before the next conditional break write.</summary>
			public void BeforeNextWrite(Action<DispatcherBreak> change)
			{
				Store.BeforeBreakWrite = row =>
				{
					Store.BeforeBreakWrite = null;
					change(row);
				};
			}
		}

		// ------------------------------------------------------------------ timer alerts and the missed mark

		[Test]
		public async Task A_reminder_is_not_sent_for_a_plan_that_moved_after_the_timer_read_it()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now.AddMinutes(10));

			// A supervisor moves the break to noon tomorrow between the timer's read and its claim.
			h.BeforeNextWrite(_ => h.Stored(planned.DispatcherBreakId).ScheduledStartOn = h.Now.AddHours(20));

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Pushes.Should().BeEmpty("the reminder was for the old time");
			h.Stored(planned.DispatcherBreakId).ReminderSentOn.Should().BeNull("the new plan still gets its own reminder");
		}

		[Test]
		public async Task A_reminder_is_not_sent_for_a_break_started_after_the_timer_read_it()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now.AddMinutes(-2));

			h.BeforeNextWrite(_ =>
			{
				var stored = h.Stored(planned.DispatcherBreakId);
				stored.State = (int)DispatcherBreakStates.Active;
				stored.StartedOn = h.Now;
			});

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Pushes.Should().NotContain(x => x.Kind == "due", "the dispatcher is already on that break");
			h.Stored(planned.DispatcherBreakId).DueAlertSentOn.Should().BeNull();
		}

		[Test]
		public async Task The_missed_mark_does_not_overwrite_a_break_re_planned_after_the_timer_read_it()
		{
			var h = new Harness();
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now.AddHours(-24).AddMinutes(-30));

			h.BeforeNextWrite(_ => h.Stored(planned.DispatcherBreakId).ScheduledStartOn = h.Now.AddHours(3));

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			var stored = h.Stored(planned.DispatcherBreakId);
			stored.State.Should().Be((int)DispatcherBreakStates.Scheduled);
			stored.ScheduledStartOn.Should().Be(h.Now.AddHours(3), "the re-plan stands");
		}

		[Test]
		public async Task A_break_the_timer_window_has_passed_by_is_still_marked_missed()
		{
			// The worker was down for more than a day: the break is older than the timer's 25-hour window.
			var h = new Harness();
			var planned = h.Planned("ann", h.Now.AddHours(-30));

			(await h.Breaks.ProcessBreakTimersAsync(h.Now)).Should().Be(1);

			h.Stored(planned.DispatcherBreakId).State.Should().Be((int)DispatcherBreakStates.Missed);
		}

		[Test]
		public async Task A_break_planned_on_a_deleted_shift_reminds_nobody()
		{
			var h = new Harness();
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now.AddMinutes(10), shiftId: 44);

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Pushes.Should().BeEmpty("shift 44 has no desk settings left: it was deleted");
			h.Stored(planned.DispatcherBreakId).State.Should().Be((int)DispatcherBreakStates.Scheduled, "the row stays for the record");

			var unscheduled = h.Planned("ann", h.Now.AddMinutes(10), shiftId: null);
			await h.Breaks.ProcessBreakTimersAsync(h.Now);
			h.Pushes.Should().ContainSingle(x => x.Kind == "reminder" && x.BreakId == unscheduled.DispatcherBreakId, "a break with no shift is unaffected");
		}

		// ------------------------------------------------------------------ re-planning

		[Test]
		public async Task A_re_plan_resets_its_reminders_and_never_overwrites_a_break_started_meanwhile()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now.AddMinutes(10));
			h.Stored(planned.DispatcherBreakId).ReminderSentOn = h.Now.AddMinutes(-5);

			var moved = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { DispatcherBreakId = planned.DispatcherBreakId, UserId = "ann", ScheduledStartOn = h.Now.AddHours(2), DurationMinutes = 15 }, "ann");
			moved.Success.Should().BeTrue();
			h.Stored(planned.DispatcherBreakId).ReminderSentOn.Should().BeNull("a re-planned break reminds again");

			// The dispatcher starts the break between the editor's read and its write.
			h.BeforeNextWrite(_ =>
			{
				var stored = h.Stored(planned.DispatcherBreakId);
				stored.State = (int)DispatcherBreakStates.Active;
				stored.StartedOn = h.Now;
				stored.ExpectedReturnOn = h.Now.AddMinutes(15);
			});

			var late = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { DispatcherBreakId = planned.DispatcherBreakId, UserId = "ann", ScheduledStartOn = h.Now.AddHours(3), DurationMinutes = 15 }, "ann");

			late.Success.Should().BeFalse();
			late.Error.Should().Be(DispatcherDeskErrors.BreakNotStartable);
			var after = h.Stored(planned.DispatcherBreakId);
			after.State.Should().Be((int)DispatcherBreakStates.Active, "the start is not overwritten by the stale plan");
			after.StartedOn.Should().Be(h.Now);
			after.ExpectedReturnOn.Should().Be(h.Now.AddMinutes(15));
		}

		[Test]
		public async Task Re_planning_the_day_commits_its_cancellations_and_new_breaks_together()
		{
			var h = new Harness(withUnitOfWork: true);
			h.OnDuty("ann");
			h.OnDuty("bob");
			h.Settings[3].SetBreakPlan(new[] { new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Break, DurationMinutes = 15 } });
			var annPlanned = h.Planned("ann", h.Now.AddHours(2));
			h.Planned("bob", h.Now.AddHours(3));

			// Ann starts her planned break while the supervisor re-plans the day.
			h.BeforeNextWrite(_ =>
			{
				var stored = h.Stored(annPlanned.DispatcherBreakId);
				stored.State = (int)DispatcherBreakStates.Active;
				stored.StartedOn = h.Now;
			});

			var result = await h.Breaks.PlanBreaksAsync(Dept, 100, true, "sup");

			result.Success.Should().BeFalse("the replacement was worked out around a break that has since started");
			h.Store.Breaks.Should().HaveCount(2, "no replacement break is inserted");
			h.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once, "the cancellations already made roll back");
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Never);

			var replanned = await h.Breaks.PlanBreaksAsync(Dept, 100, true, "sup");
			replanned.Success.Should().BeTrue();
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Once);
			h.Store.Breaks.Count(x => x.State == (int)DispatcherBreakStates.Scheduled && x.CreatedByUserId == "sup").Should().Be(replanned.Value.Count);
		}

		[Test]
		public async Task A_dispatcher_cannot_move_their_own_break_into_a_shift_that_does_not_allow_self_scheduled_breaks()
		{
			var h = new Harness();
			h.OnDuty("ann", shiftId: 3, shiftDayId: 100);
			h.Duty.Add(new DispatcherDutyWindow { UserId = "ann", ShiftId = 9, ShiftDayId = 900, ShiftName = "Nights", StartUtc = h.Now.AddHours(8), EndUtc = h.Now.AddHours(20) });
			h.Settings[9] = new ShiftDispatchSetting { ShiftId = 9, DepartmentId = Dept, IsEnabled = true, AllowSelfScheduledBreaks = false };
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now.AddHours(1));

			var intoNights = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { DispatcherBreakId = planned.DispatcherBreakId, UserId = "ann", ShiftId = 3, ScheduledStartOn = h.Now.AddHours(10), DurationMinutes = 15 }, "ann");
			intoNights.Error.Should().Be(DispatcherDeskErrors.NotPermitted, "the shift the break moves into decides");

			var offDuty = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { DispatcherBreakId = planned.DispatcherBreakId, UserId = "ann", ShiftId = 3, ScheduledStartOn = h.Now.AddHours(30), DurationMinutes = 15 }, "ann");
			offDuty.Error.Should().Be(DispatcherDeskErrors.NotPermitted, "a self-scheduled break must sit inside approved duty");

			h.Stored(planned.DispatcherBreakId).ScheduledStartOn.Should().Be(h.Now.AddHours(1));

			h.Supervisors.Add("sup");
			(await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { DispatcherBreakId = planned.DispatcherBreakId, UserId = "ann", ScheduledStartOn = h.Now.AddHours(10), DurationMinutes = 15 }, "sup"))
				.Success.Should().BeTrue("a supervisor plans anywhere");
		}

		// ------------------------------------------------------------------ starting and ending breaks

		[Test]
		public async Task A_break_start_takes_the_department_lock_before_it_reads_coverage_and_commits()
		{
			var h = new Harness(withUnitOfWork: true);
			h.OnDuty("ann");
			h.SignedIn("ann");
			var lockedBeforeCoverage = false;
			h.Presence.Setup(x => x.GetDeskAsync(Dept, It.IsAny<DateTime>()))
				.Callback(() => lockedBeforeCoverage = h.Store.BreakStartLocks.Contains(Dept))
				.ReturnsAsync(() => h.Desk);

			var started = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, false, "ann");

			started.Success.Should().BeTrue();
			h.Store.BreakStartLocks.Should().Equal(new[] { Dept });
			lockedBeforeCoverage.Should().BeTrue("the coverage decision is made under the lock");
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Once);
		}

		[Test]
		public async Task Work_that_could_not_be_handed_back_at_the_start_of_a_break_is_handed_back_by_the_timer()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.Held("i-1", "ann");
			h.Held("i-2", "ann");
			var failNext = true;
			h.Routing.Setup(x => x.ReleaseAsync(Dept, It.IsAny<string>(), "ann", It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, string u, CancellationToken c) =>
				{
					if (id == "i-2" && failNext)
					{
						failNext = false;
						throw new InvalidOperationException("routing write failed");
					}

					h.Store.Items.Single(x => x.DispatcherQueueItemId == id).State = (int)DispatcherQueueItemStates.Queued;
					return DispatcherDeskResult<DispatcherQueueItem>.Ok(null);
				});

			var started = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, true, "ann");

			started.Success.Should().BeTrue("the break is stored and the dispatcher is unavailable");
			started.Warnings.Should().NotContain(x => x.Kind == DispatcherDeskWarningKinds.WorkReturnedToQueue, "not all of it went back");
			h.Store.Items.Single(x => x.DispatcherQueueItemId == "i-2").State.Should().Be((int)DispatcherQueueItemStates.Accepted);

			(await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(1))).Should().Be(1);

			h.Store.Items.Should().OnlyContain(x => x.State == (int)DispatcherQueueItemStates.Queued);
			h.Routing.Verify(x => x.ReleaseAsync(Dept, "i-2", "ann", It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task Auto_return_ends_only_the_break_the_timer_evaluated()
		{
			var h = new Harness();
			h.Config.AutoReturnAfterMinutes = 5;
			var presence = h.SignedIn("ann", DispatcherPresenceStates.OnBreak);
			var first = h.Active("ann", h.Now.AddMinutes(-30));
			presence.ActiveBreakId = first.DispatcherBreakId;
			DispatcherBreak second = null;

			// She came back from the first break and started a second one after the timer read the first (before its first write).
			h.BeforeNextWrite(_ =>
			{
				var stored = h.Stored(first.DispatcherBreakId);
				stored.State = (int)DispatcherBreakStates.Completed;
				stored.EndedOn = h.Now.AddMinutes(-1);
				second = h.Active("ann", h.Now);
				var current = h.Store.Presences.Single(x => x.UserId == "ann");
				current.State = (int)DispatcherPresenceStates.OnBreak;
				current.ActiveBreakId = second.DispatcherBreakId;
			});

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Stored(second.DispatcherBreakId).State.Should().Be((int)DispatcherBreakStates.Active, "her new break has its own return time");
			var after = h.Store.Presences.Single(x => x.UserId == "ann");
			after.State.Should().Be((int)DispatcherPresenceStates.OnBreak);
			after.ActiveBreakId.Should().Be(second.DispatcherBreakId);
			h.Pushes.Should().NotContain(x => x.Kind == "auto-returned");
			h.Pushes.Should().NotContain(x => x.Kind == "break-over", "the first break is already over and the second is not due");
		}

		[Test]
		public async Task Auto_return_of_an_unchanged_break_still_ends_it()
		{
			var h = new Harness();
			h.Config.AutoReturnAfterMinutes = 5;
			var presence = h.SignedIn("ann", DispatcherPresenceStates.OnBreak);
			var active = h.Active("ann", h.Now.AddMinutes(-30));
			presence.ActiveBreakId = active.DispatcherBreakId;

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Stored(active.DispatcherBreakId).State.Should().Be((int)DispatcherBreakStates.Completed);
			h.Stored(active.DispatcherBreakId).EndReason.Should().Be((int)DispatcherBreakEndReasons.AutoReturn);
			h.Store.Presences.Single(x => x.UserId == "ann").State.Should().Be((int)DispatcherPresenceStates.Available);
			h.Pushes.Should().Contain(x => x.Kind == "auto-returned");
		}

		[Test]
		public async Task With_no_supervisor_to_tell_the_dispatcher_gets_no_second_break_over_alert()
		{
			var h = new Harness();
			h.Config.NotifySupervisorsOnOverdueBreak = true;
			var presence = h.SignedIn("ann", DispatcherPresenceStates.OnBreak);
			var active = h.Active("ann", h.Now.AddMinutes(-30));
			presence.ActiveBreakId = active.DispatcherBreakId;

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Pushes.Should().ContainSingle(x => x.Kind == "break-over", "her own alert");
			h.Pushes.Should().NotContain(x => x.Kind.StartsWith("supervisors"), "nobody resolved to supervise, so nothing goes to the supervisor path");
			h.Stored(active.DispatcherBreakId).OverdueAlertSentOn.Should().NotBeNull("the overdue check ran once");

			h.SupervisorsOnDuty.Add("sup");
			var later = h.Active("bob", h.Now.AddMinutes(-30));
			h.SignedIn("bob", DispatcherPresenceStates.OnBreak).ActiveBreakId = later.DispatcherBreakId;
			await h.Breaks.ProcessBreakTimersAsync(h.Now);
			h.Pushes.Should().Contain(x => x.Kind == "supervisors:sup" && x.UserId == "bob");
		}

		// ------------------------------------------------------------------ PR 589 round 2

		[Test]
		public async Task Auto_return_never_writes_a_signed_off_dispatcher_back_as_available()
		{
			var h = new Harness();
			h.Config.AutoReturnAfterMinutes = 5;
			var presence = h.SignedIn("ann", DispatcherPresenceStates.OnBreak);
			var active = h.Active("ann", h.Now.AddMinutes(-30));
			presence.ActiveBreakId = active.DispatcherBreakId;

			// She signs off after auto return read her presence and before it writes it back.
			h.Store.BeforePresenceWrite = _ =>
			{
				h.Store.BeforePresenceWrite = null;
				var stored = h.Store.Presences.Single(x => x.UserId == "ann");
				stored.State = (int)DispatcherPresenceStates.Offline;
				stored.ActiveBreakId = null;
			};

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Stored(active.DispatcherBreakId).State.Should().Be((int)DispatcherBreakStates.Completed);
			h.Store.Presences.Single(x => x.UserId == "ann").State.Should().Be((int)DispatcherPresenceStates.Offline, "the sign-off wins over the stale copy");
		}

		[Test]
		public async Task A_break_still_active_after_its_dispatcher_signed_off_is_closed_by_the_timer()
		{
			var h = new Harness();
			var presence = h.SignedIn("ann", DispatcherPresenceStates.Offline);
			presence.StateChangedOn = h.Now.AddMinutes(-3);
			var active = h.Active("ann", h.Now.AddMinutes(-10));

			(await h.Breaks.ProcessBreakTimersAsync(h.Now)).Should().Be(1);

			var closed = h.Stored(active.DispatcherBreakId);
			closed.State.Should().Be((int)DispatcherBreakStates.Completed);
			closed.EndReason.Should().Be((int)DispatcherBreakEndReasons.SignedOff);
			closed.EndedOn.Should().Be(h.Now.AddMinutes(-3), "it ended with the session");
			h.Pushes.Should().BeEmpty("nobody is told a signed-off dispatcher's break is over");
		}

		[Test]
		public async Task A_reminder_the_push_did_not_accept_is_sent_again_by_the_next_pass_and_never_after_it_is_delivered()
		{
			var h = new Harness();
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now.AddMinutes(10), shiftId: null);
			h.Delivers = false;

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Pushes.Should().ContainSingle(x => x.Kind == "reminder");
			h.Stored(planned.DispatcherBreakId).ReminderSentOn.Should().BeNull("the claim is given back for the next pass");

			h.Delivers = true;
			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(1));
			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(2));

			h.Pushes.Count(x => x.Kind == "reminder").Should().Be(2, "sent again once, then never after it was accepted");
			h.Stored(planned.DispatcherBreakId).ReminderSentOn.Should().Be(h.Now.AddMinutes(1));
		}

		[Test]
		public async Task An_alert_the_push_keeps_refusing_is_given_up_after_the_retry_window()
		{
			var h = new Harness();
			var presence = h.SignedIn("ann", DispatcherPresenceStates.OnBreak);
			var active = h.Active("ann", h.Now.AddMinutes(-60), 15);
			presence.ActiveBreakId = active.DispatcherBreakId;
			h.Delivers = false;

			// Due 45 minutes ago: past the retry window, so the failed claim stays (logged) instead of being retried every pass.
			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.Pushes.Should().ContainSingle(x => x.Kind == "break-over");
			h.Stored(active.DispatcherBreakId).ReturnAlertSentOn.Should().Be(h.Now);

			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(1));
			h.Pushes.Count(x => x.Kind == "break-over").Should().Be(1);
		}

		[Test]
		public async Task A_supervisor_alert_that_reached_nobody_is_sent_again()
		{
			var h = new Harness();
			h.Config.NotifySupervisorsOnOverdueBreak = true;
			h.SupervisorsOnDuty.Add("sup");
			var presence = h.SignedIn("ann", DispatcherPresenceStates.OnBreak);
			var active = h.Active("ann", h.Now.AddMinutes(-21), 15);
			presence.ActiveBreakId = active.DispatcherBreakId;
			h.Delivers = false;

			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			var stored = h.Stored(active.DispatcherBreakId);
			stored.ReturnAlertSentOn.Should().BeNull("her own alert is retried");
			stored.OverdueAlertSentOn.Should().BeNull("the supervisors' alert is retried");

			h.Delivers = true;
			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(1));

			h.Pushes.Count(x => x.Kind == "supervisors:sup").Should().Be(2);
			h.Stored(active.DispatcherBreakId).OverdueAlertSentOn.Should().NotBeNull();
		}

		[Test]
		public async Task Planning_a_day_reads_its_breaks_under_the_department_lock()
		{
			var h = new Harness(withUnitOfWork: true);
			h.OnDuty("ann");
			h.Settings[3].SetBreakPlan(new[] { new DispatcherBreakPlanEntry { BreakType = (int)DispatcherBreakTypes.Break, DurationMinutes = 15 } });

			var first = await h.Breaks.PlanBreaksAsync(Dept, 100, false, "sup");

			first.Success.Should().BeTrue();
			first.Value.Should().NotBeEmpty();
			h.Store.BreakStartLocks.Should().Equal(new[] { Dept }, "the day's breaks are read and the plan inserted under the lock");

			// A second editor planning the same day, serialized behind the first, plans against what it committed: nothing twice.
			var second = await h.Breaks.PlanBreaksAsync(Dept, 100, false, "sup2");

			second.Success.Should().BeTrue();
			second.Value.Should().BeEmpty("ann already has the day's plan");
			h.Store.Breaks.Count(x => x.UserId == "ann" && x.State == (int)DispatcherBreakStates.Scheduled).Should().Be(first.Value.Count);
			h.Store.BreakStartLocks.Should().Equal(new[] { Dept, Dept });
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Exactly(2));
		}

		[Test]
		public async Task A_break_re_planned_after_the_start_read_it_is_not_started_with_the_old_plan()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var planned = h.Planned("ann", h.Now, 15);

			// A supervisor makes it a 30 minute break between the start's read and its write.
			h.BeforeNextWrite(_ => h.Stored(planned.DispatcherBreakId).DurationMinutes = 30);

			var started = await h.Breaks.StartBreakAsync(Dept, "ann", planned.DispatcherBreakId, null, null, false, "ann");

			started.Error.Should().Be(DispatcherDeskErrors.BreakNotStartable, "the edited plan is reloaded before it starts");
			var stored = h.Stored(planned.DispatcherBreakId);
			stored.State.Should().Be((int)DispatcherBreakStates.Scheduled);
			stored.DurationMinutes.Should().Be(30, "the supervisor's edit stands");

			(await h.Breaks.StartBreakAsync(Dept, "ann", planned.DispatcherBreakId, null, null, false, "ann")).Success.Should().BeTrue();
			h.Stored(planned.DispatcherBreakId).ExpectedReturnOn.Should().Be(h.Now.AddMinutes(30));
		}

		[Test]
		public async Task A_hand_back_that_loses_a_version_race_while_still_held_is_not_reported_as_returned()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.Held("i-1", "ann");
			h.Routing.Setup(x => x.ReleaseAsync(Dept, "i-1", "ann", It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable));

			var started = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, true, "ann");

			started.Success.Should().BeTrue();
			started.Warnings.Should().NotContain(x => x.Kind == DispatcherDeskWarningKinds.WorkReturnedToQueue, "the item is still hers");
		}

		[Test]
		public async Task A_hand_back_that_loses_a_version_race_to_another_holder_counts_as_returned()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.Held("i-1", "ann");
			h.Routing.Setup(x => x.ReleaseAsync(Dept, "i-1", "ann", It.IsAny<CancellationToken>()))
				.ReturnsAsync(() =>
				{
					// A supervisor moved it to bob between the hand-back's read and its write.
					var item = h.Store.Items.Single();
					item.AssignedUserId = "bob";
					return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable);
				});

			var started = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, true, "ann");

			started.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.WorkReturnedToQueue, "it is no longer work she holds");
		}
	}
}
