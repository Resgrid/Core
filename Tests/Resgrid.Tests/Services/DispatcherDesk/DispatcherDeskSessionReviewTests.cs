using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// PR 589 review: the desk session (sign-off that hands back all work, the sweep acting only on the session it read, the
	/// heartbeat never restoring a signed-off state, revoked dispatchers off the desk), the desk settings cache, the desk
	/// worker's isolated parts and the shift delete that takes its desk rows with it.
	/// </summary>
	[TestFixture]
	public class DispatcherDeskSessionReviewTests
	{
		private const int Dept = 7;
		private static readonly DateTime Now = new DateTime(2026, 11, 21, 16, 0, 0, DateTimeKind.Utc);

		private InMemoryDispatcherDeskStore _store;
		private DispatcherDeskConfig _config;
		private HashSet<string> _eligible;
		private List<DispatcherDutyWindow> _duty;
		private Mock<IDispatcherDeskConfigService> _configService;
		private Mock<IDispatcherRoutingService> _routing;
		private Func<string, bool> _releaseFails;
		private DispatcherPresenceService _presence;

		[SetUp]
		public void SetUp()
		{
			_store = new InMemoryDispatcherDeskStore();
			_config = new DispatcherDeskConfig { Enabled = true, RequireShift = false };
			_eligible = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ann", "bob", "cat" };
			_duty = new List<DispatcherDutyWindow>();
			_releaseFails = _ => false;

			_configService = new Mock<IDispatcherDeskConfigService>();
			_configService.Setup(x => x.GetConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _config);

			var shiftDispatchers = new Mock<IShiftDispatchersService>();
			shiftDispatchers.Setup(x => x.IsEligibleDispatcherAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string u) => _eligible.Contains(u));
			shiftDispatchers.Setup(x => x.GetOnDutyAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync((int d, DateTime at) => DispatcherDutyCalculator.OnDuty(_duty, at));
			shiftDispatchers.Setup(x => x.GetDutyForUserAsync(Dept, It.IsAny<string>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int d, string u, DateTime at) => DispatcherDutyCalculator.OnDuty(_duty, at).FirstOrDefault(x => x.UserId == u));

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = "owner", AdminUsers = new List<string>(), TimeZone = "Eastern Standard Time" });

			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Dictionary<string, UserProfile>());

			_routing = new Mock<IDispatcherRoutingService>();
			_routing.Setup(x => x.ReleaseAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, string u, CancellationToken c) =>
				{
					if (_releaseFails(id))
						throw new InvalidOperationException("release failed");

					var item = _store.Items.Single(x => x.DispatcherQueueItemId == id);
					item.State = (int)DispatcherQueueItemStates.Queued;
					item.AssignedUserId = null;
					item.Version++;
					return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
				});
			// The desk's own hand-back: version-fenced like the real one (an item changed since the read is left alone).
			_routing.Setup(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatcherQueueItem observed, CancellationToken c) =>
				{
					if (_releaseFails(observed.DispatcherQueueItemId))
						throw new InvalidOperationException("release failed");

					var item = _store.Items.Single(x => x.DispatcherQueueItemId == observed.DispatcherQueueItemId);
					if (item.Version != observed.Version)
						return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable);

					item.State = (int)DispatcherQueueItemStates.Queued;
					item.AssignedUserId = null;
					item.Version++;
					return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
				});

			_presence = new DispatcherPresenceService(_store.PresencesRepository, _store.BreaksRepository, _store.ItemsRepository, shiftDispatchers.Object,
				_configService.Object, departments.Object, profiles.Object, new Mock<IAuthorizationService>().Object, new Mock<IDispatcherDeskNotifier>().Object,
				new Lazy<IDispatcherRoutingService>(() => _routing.Object))
			{ UtcNow = () => Now };
		}

		private void OnDuty(string user) =>
			_duty.Add(new DispatcherDutyWindow { UserId = user, ShiftId = 3, ShiftDayId = 100, ShiftName = "Dispatch A", StartUtc = Now.AddHours(-4), EndUtc = Now.AddHours(8) });

		private DispatcherQueueItem Held(string id, string user, DispatcherQueueItemStates state = DispatcherQueueItemStates.Accepted)
		{
			var item = new DispatcherQueueItem
			{
				DispatcherQueueItemId = id, DepartmentId = Dept, Kind = (int)DispatcherQueueItemKinds.PendingCall, State = (int)state, AssignedUserId = user,
				CreatedOn = Now.AddMinutes(-5), LastActivityOn = Now.AddMinutes(-1)
			};
			_store.Items.Add(item);
			return item;
		}

		// ------------------------------------------------------------------ phone numbers

		[TestCase("+3545551234", "+3545551234")]
		[TestCase("+44 20 7946 0958", "+442079460958")]
		[TestCase("555-123-4567", "+15551234567")]
		[TestCase("(555) 123 4567", "+15551234567")]
		[TestCase("+1 555 123 4567", "+15551234567")]
		[TestCase("12", null)]
		public void An_international_callback_number_keeps_its_country_code(string input, string expected)
		{
			DispatcherPresenceService.NormalizePhone(input).Should().Be(expected);
		}

		// ------------------------------------------------------------------ duty

		[Test]
		public async Task An_unscheduled_session_past_its_explicit_end_is_not_on_duty()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now);
			presence.DutyMode = (int)DispatcherDutyModes.Unscheduled;
			presence.DutyEndsOn = Now.AddMinutes(-1);

			(await _presence.GetMemberAsync(Dept, "ann", Now)).IsOnDuty.Should().BeFalse("its own end has passed, shift or no shift");

			presence.DutyEndsOn = null;
			(await _presence.GetMemberAsync(Dept, "ann", Now)).IsOnDuty.Should().BeTrue("an open-ended unscheduled session stays on");
		}

		[Test]
		public async Task A_scheduled_session_without_a_shift_window_stays_on_duty_where_no_shift_is_required()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now);
			presence.DutyMode = (int)DispatcherDutyModes.Scheduled;

			(await _presence.GetMemberAsync(Dept, "ann", Now)).IsOnDuty.Should().BeTrue();
		}

		// ------------------------------------------------------------------ sign-off

		[Test]
		public async Task A_sign_off_whose_hand_back_fails_part_way_says_so_and_a_second_sign_off_finishes_it()
		{
			_store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now);
			Held("x", "ann");
			Held("y", "ann");
			var failOnce = true;
			_releaseFails = id =>
			{
				if (id != "y" || !failOnce)
					return false;
				failOnce = false;
				return true;
			};

			var first = await _presence.SignOffAsync(Dept, "ann", "ann");

			first.Error.Should().Be(DispatcherDeskErrors.HoldingWork);
			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.Offline, "offline first, so nothing comes back to her");
			_store.Items.Single(x => x.DispatcherQueueItemId == "x").State.Should().Be((int)DispatcherQueueItemStates.Queued, "one failure never keeps the rest");
			_store.Items.Single(x => x.DispatcherQueueItemId == "y").AssignedUserId.Should().Be("ann");

			var second = await _presence.SignOffAsync(Dept, "ann", "ann");

			second.Success.Should().BeTrue();
			second.Value.Should().BeTrue();
			_store.Items.Should().OnlyContain(x => x.State == (int)DispatcherQueueItemStates.Queued);
		}

		[Test]
		public async Task Signing_off_twice_with_nothing_held_is_a_no_op()
		{
			_store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now);

			(await _presence.SignOffAsync(Dept, "ann", "ann")).Value.Should().BeTrue();
			(await _presence.SignOffAsync(Dept, "ann", "ann")).Value.Should().BeFalse();
		}

		[Test]
		public async Task The_sweep_never_signs_off_a_session_that_began_after_it_read_the_expired_one()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now.AddHours(-9));
			presence.DutyMode = (int)DispatcherDutyModes.Unscheduled;
			presence.DutyEndsOn = Now.AddMinutes(-5);

			// The dispatcher signs in again while the sweep is between its read and its sign-off.
			var signedInAgain = false;
			_configService.Setup(x => x.GetConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() =>
			{
				if (!signedInAgain)
				{
					signedInAgain = true;
					presence.SignedInOn = Now;
					presence.DutyEndsOn = Now.AddHours(8);
				}

				return _config;
			});

			await _presence.ProcessPresenceAsync(Now);

			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.Available, "the new session is not the one the sweep evaluated");
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_sweep_still_signs_off_the_expired_session_it_read()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now.AddHours(-9));
			presence.DutyMode = (int)DispatcherDutyModes.Unscheduled;
			presence.DutyEndsOn = Now.AddMinutes(-5);
			Held("x", "ann");

			(await _presence.ProcessPresenceAsync(Now)).Should().BeGreaterThan(0);

			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.Offline);
			_store.Items.Single().State.Should().Be((int)DispatcherQueueItemStates.Queued);
		}

		[Test]
		public async Task A_heartbeat_writes_only_the_last_seen_time()
		{
			var repository = new Mock<IDispatcherPresencesRepository>();
			repository.Setup(x => x.GetForUserAsync(Dept, "ann")).ReturnsAsync(new DispatcherPresence
			{
				DispatcherPresenceId = "p1", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherPresenceStates.Available, SignedInOn = Now
			});
			var presence = new DispatcherPresenceService(repository.Object, _store.BreaksRepository, _store.ItemsRepository, new Mock<IShiftDispatchersService>().Object,
				_configService.Object, new Mock<IDepartmentsService>().Object, new Mock<IUserProfileService>().Object, new Mock<IAuthorizationService>().Object,
				new Mock<IDispatcherDeskNotifier>().Object, new Lazy<IDispatcherRoutingService>(() => _routing.Object))
			{ UtcNow = () => Now };

			await presence.HeartbeatAsync(Dept, "ann");

			repository.Verify(x => x.TouchAsync("p1", Now, It.IsAny<CancellationToken>()), Times.Once);
			repository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DispatcherPresence>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never,
				"a whole-row save would write the signed-in state back over a sign-off committed in between");
		}

		[Test]
		public async Task A_heartbeat_cannot_bring_a_signed_off_row_back()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now);

			(await _store.PresencesRepository.TrySignOffAsync(presence.DispatcherPresenceId, presence.SignedInOn, Now)).Should().BeTrue();
			(await _store.PresencesRepository.TouchAsync(presence.DispatcherPresenceId, Now.AddMinutes(1))).Should().BeFalse();

			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.Offline);
		}

		// ------------------------------------------------------------------ revoked dispatchers

		[Test]
		public async Task A_dispatcher_who_lost_the_dispatch_login_gets_no_offers_and_is_signed_off_by_the_sweep()
		{
			OnDuty("ann");
			OnDuty("bob");
			_store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now, Now.AddMinutes(-30));
			_store.AddPresence(Dept, "bob", DispatcherPresenceStates.Available, Now, Now.AddMinutes(-60));

			(await _presence.GetRoutableAsync(Dept, Now)).Select(x => x.UserId).Should().BeEquivalentTo(new[] { "ann", "bob" }, "both may dispatch");

			_eligible.Remove("bob");
			(await _presence.GetRoutableAsync(Dept, Now)).Select(x => x.UserId).Should().Equal(new[] { "ann" });

			Held("b1", "bob");
			await _presence.ProcessPresenceAsync(Now);

			_store.Presences.Single(x => x.UserId == "bob").State.Should().Be((int)DispatcherPresenceStates.Offline);
			_store.Presences.Single(x => x.UserId == "ann").State.Should().Be((int)DispatcherPresenceStates.Available);
			_store.Items.Single().State.Should().Be((int)DispatcherQueueItemStates.Queued, "the revoked dispatcher's work goes back");
		}

		[Test]
		public async Task The_sweep_hands_back_work_still_held_by_someone_signed_off()
		{
			// An accept that committed just after the sign-off released her work, or a hand-back that failed part way.
			_store.AddPresence(Dept, "cat", DispatcherPresenceStates.Offline, Now);
			Held("c1", "cat");

			(await _presence.ProcessPresenceAsync(Now)).Should().Be(1);

			_store.Items.Single().State.Should().Be((int)DispatcherQueueItemStates.Queued);
		}

		// ------------------------------------------------------------------ PR 589 round 2

		[Test]
		public async Task The_hand_back_releases_only_the_assignment_it_read()
		{
			_store.AddPresence(Dept, "cat", DispatcherPresenceStates.Offline, Now);
			var item = Held("c1", "cat");
			item.Version = 4;

			// Cat signs in again and accepts the same item again after the sweep read it (a new assignment: the Version moves on).
			_routing.Setup(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatcherQueueItem observed, CancellationToken c) =>
				{
					var stored = _store.Items.Single(x => x.DispatcherQueueItemId == observed.DispatcherQueueItemId);
					stored.Version++;
					return stored.Version - 1 == observed.Version
						? DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable)
						: DispatcherDeskResult<DispatcherQueueItem>.Ok(stored);
				});

			await _presence.ProcessPresenceAsync(Now);

			_routing.Verify(x => x.ReleaseObservedAsync(It.Is<DispatcherQueueItem>(i => i.DispatcherQueueItemId == "c1" && i.Version == 4), It.IsAny<CancellationToken>()),
				Times.Once, "the release names the assignment as it was read");
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"the holder's own release would pass for whatever the item has become");
			_store.Items.Single().AssignedUserId.Should().Be("cat", "her new assignment is not handed back");
		}

		[Test]
		public async Task A_sign_in_after_a_sign_off_whose_break_close_failed_does_not_resume_the_old_break()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Offline, Now.AddMinutes(-30));
			presence.StateChangedOn = Now.AddMinutes(-20);
			var leftover = new DispatcherBreak
			{
				DispatcherBreakId = "b-old", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
				StartedOn = Now.AddMinutes(-25), ExpectedReturnOn = Now.AddMinutes(-10), CreatedOn = Now.AddMinutes(-25)
			};
			_store.Breaks.Add(leftover);

			var signedIn = await _presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest());

			signedIn.Success.Should().BeTrue();
			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.Available, "the new session is not put on the old break");
			_store.Presences.Single().ActiveBreakId.Should().BeNull();
			var closed = _store.Breaks.Single();
			closed.State.Should().Be((int)DispatcherBreakStates.Completed);
			closed.EndReason.Should().Be((int)DispatcherBreakEndReasons.SignedOff);
			closed.EndedOn.Should().Be(Now.AddMinutes(-20), "it ended with the session that signed off");
		}

		[Test]
		public async Task A_sign_in_that_refreshes_a_session_on_break_keeps_the_break()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.OnBreak, Now.AddMinutes(-30));
			presence.ActiveBreakId = "b-now";
			_store.Breaks.Add(new DispatcherBreak
			{
				DispatcherBreakId = "b-now", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
				StartedOn = Now.AddMinutes(-5), ExpectedReturnOn = Now.AddMinutes(10), CreatedOn = Now.AddMinutes(-5)
			});

			(await _presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest())).Success.Should().BeTrue();

			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.OnBreak);
			_store.Breaks.Single().State.Should().Be((int)DispatcherBreakStates.Active);
		}

		[Test]
		public async Task Signing_off_again_closes_a_break_the_first_sign_off_left_open_but_never_a_later_sessions_break()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Offline, Now.AddMinutes(-30));
			presence.StateChangedOn = Now.AddMinutes(-20);
			_store.Breaks.Add(new DispatcherBreak
			{
				DispatcherBreakId = "b-old", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
				StartedOn = Now.AddMinutes(-25), CreatedOn = Now.AddMinutes(-25)
			});

			await _presence.SignOffAsync(Dept, "ann", "ann");

			_store.Breaks.Single().State.Should().Be((int)DispatcherBreakStates.Completed);

			// A break started after that sign-off (a later session's) is not this sign-off's to close.
			_store.Breaks.Add(new DispatcherBreak
			{
				DispatcherBreakId = "b-new", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
				StartedOn = Now.AddMinutes(-1), CreatedOn = Now.AddMinutes(-1)
			});
			await _presence.SignOffAsync(Dept, "ann", "ann");

			_store.Breaks.Single(x => x.DispatcherBreakId == "b-new").State.Should().Be((int)DispatcherBreakStates.Active);
		}

		[Test]
		public async Task The_sweep_never_writes_over_a_duty_another_worker_moved_on()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now.AddHours(-12));
			presence.DutyMode = (int)DispatcherDutyModes.Scheduled;
			presence.DutyShiftId = 3;
			presence.DutyShiftDayId = 100;
			presence.DutyEndsOn = Now.AddMinutes(-1);
			_config.SignOffAtShiftEnd = true;
			_config.RequireShift = false;

			// Another worker process carried her on to the next shift day between this sweep's read and its write.
			_store.BeforePresenceWrite = _ =>
			{
				_store.BeforePresenceWrite = null;
				var stored = _store.Presences.Single();
				stored.DutyShiftDayId = 101;
				stored.DutyEndsOn = Now.AddHours(8);
			};

			await _presence.ProcessPresenceAsync(Now);

			var after = _store.Presences.Single();
			after.DutyMode.Should().Be((int)DispatcherDutyModes.Scheduled, "the stale 'unscheduled, no end' write does not land");
			after.DutyShiftDayId.Should().Be(101);
			after.DutyEndsOn.Should().Be(Now.AddHours(8));
		}

		// ------------------------------------------------------------------ PR 589 round 3

		[Test]
		public async Task A_sign_in_that_loses_to_another_sessions_break_reads_again_and_keeps_that_break()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.Offline, Now.AddMinutes(-30));
			presence.StateChangedOn = Now.AddMinutes(-20);

			// Between this sign-in's read and its write, another sign-in commits and starts a break.
			_store.BeforePresenceWrite = _ =>
			{
				_store.BeforePresenceWrite = null;
				var stored = _store.Presences.Single();
				stored.State = (int)DispatcherPresenceStates.OnBreak;
				stored.SignedInOn = Now.AddMinutes(-1);
				stored.ActiveBreakId = "b-new";
				_store.Breaks.Add(new DispatcherBreak
				{
					DispatcherBreakId = "b-new", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
					StartedOn = Now.AddSeconds(-30), ExpectedReturnOn = Now.AddMinutes(15), CreatedOn = Now.AddSeconds(-30)
				});
			};

			(await _presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest())).Success.Should().BeTrue();

			var after = _store.Presences.Single();
			after.State.Should().Be((int)DispatcherPresenceStates.OnBreak, "the stale Offline read is not written over the newer session");
			after.ActiveBreakId.Should().Be("b-new");
			_store.Breaks.Single().State.Should().Be((int)DispatcherBreakStates.Active);
		}

		[Test]
		public async Task A_sign_in_whose_break_close_is_refused_reads_again_instead_of_clearing_the_break()
		{
			// Read as signed off; by the time the break is read, another session has signed in and started it (after that sign-off).
			_store.AddPresence(Dept, "ann", DispatcherPresenceStates.Offline, Now.AddMinutes(-30)).StateChangedOn = Now.AddMinutes(-20);
			_store.Breaks.Add(new DispatcherBreak
			{
				DispatcherBreakId = "b-new", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
				StartedOn = Now.AddMinutes(-2), ExpectedReturnOn = Now.AddMinutes(13), CreatedOn = Now.AddMinutes(-2)
			});
			_store.BeforeActiveBreakRead = _ =>
			{
				_store.BeforeActiveBreakRead = null;
				var stored = _store.Presences.Single();
				stored.State = (int)DispatcherPresenceStates.OnBreak;
				stored.SignedInOn = Now.AddMinutes(-3);
				stored.ActiveBreakId = "b-new";
			};

			(await _presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest())).Success.Should().BeTrue();

			var after = _store.Presences.Single();
			after.State.Should().Be((int)DispatcherPresenceStates.OnBreak, "the newer session's break is kept, not cleared from the stale read");
			after.ActiveBreakId.Should().Be("b-new");
			_store.Breaks.Single().State.Should().Be((int)DispatcherBreakStates.Active);
		}

		[Test]
		public async Task A_break_that_can_never_be_closed_as_signed_off_does_not_lock_the_dispatcher_out()
		{
			// The break's start is after the row's sign-off time and nothing else changes (clocks apart): not a race.
			_store.AddPresence(Dept, "ann", DispatcherPresenceStates.Offline, Now.AddMinutes(-30)).StateChangedOn = Now.AddMinutes(-20);
			_store.Breaks.Add(new DispatcherBreak
			{
				DispatcherBreakId = "b-odd", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
				StartedOn = Now.AddMinutes(-2), CreatedOn = Now.AddMinutes(-2)
			});

			(await _presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest())).Success.Should().BeTrue();

			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.Available);
		}

		[Test]
		public async Task A_sign_off_whose_break_close_fails_still_hands_back_the_work()
		{
			var presence = _store.AddPresence(Dept, "ann", DispatcherPresenceStates.OnBreak, Now.AddMinutes(-30));
			presence.ActiveBreakId = "b-1";
			_store.Breaks.Add(new DispatcherBreak
			{
				DispatcherBreakId = "b-1", DepartmentId = Dept, UserId = "ann", State = (int)DispatcherBreakStates.Active, DurationMinutes = 15,
				StartedOn = Now.AddMinutes(-5), ExpectedReturnOn = Now.AddMinutes(10), CreatedOn = Now.AddMinutes(-5)
			});
			Held("x", "ann");
			_store.BeforeBreakWrite = _ => throw new InvalidOperationException("break close failed");

			var signedOff = await _presence.SignOffAsync(Dept, "ann", "ann");

			signedOff.Success.Should().BeTrue();
			_store.Presences.Single().State.Should().Be((int)DispatcherPresenceStates.Offline);
			_store.Items.Single().State.Should().Be((int)DispatcherQueueItemStates.Queued, "the failed close does not keep the work");
			_store.Breaks.Single().State.Should().Be((int)DispatcherBreakStates.Active, "left for the next sign-off, sign-in or the break timer to close");
		}

		[Test]
		public async Task A_sign_off_that_could_not_hand_back_an_item_touched_meanwhile_reports_it_still_held()
		{
			_store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now.AddMinutes(-30));
			Held("x", "ann");

			// A participant message touches the item (new Version, same holder) after sign-off read it: the fenced release refuses.
			_routing.Setup(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatcherQueueItem observed, CancellationToken c) =>
				{
					_store.Items.Single().Version++;
					return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable);
				});

			var signedOff = await _presence.SignOffAsync(Dept, "ann", "ann");

			signedOff.Error.Should().Be(DispatcherDeskErrors.HoldingWork, "she is signed off and still holds the item: the caller retries");
			_store.Items.Single().AssignedUserId.Should().Be("ann");
		}

		[Test]
		public async Task A_dispatcher_signed_in_again_during_the_hand_back_keeps_their_work_and_holds_none_back()
		{
			_store.AddPresence(Dept, "ann", DispatcherPresenceStates.Available, Now.AddMinutes(-30));
			Held("x", "ann");

			// She signs in again and takes the item back while the hand-back runs.
			_routing.Setup(x => x.ReleaseObservedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatcherQueueItem observed, CancellationToken c) =>
				{
					var stored = _store.Presences.Single();
					stored.State = (int)DispatcherPresenceStates.Available;
					stored.SignedInOn = Now;
					_store.Items.Single().Version++;
					return DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable);
				});

			(await _presence.SignOffAsync(Dept, "ann", "ann")).Success.Should().BeTrue("work held by a dispatcher back on the desk is theirs");
		}

		// ------------------------------------------------------------------ desk settings cache

		[Test]
		public async Task A_settings_read_that_finishes_after_a_save_cannot_put_the_old_settings_back_in_the_cache()
		{
			var previous = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = true;
			try
			{
				var cache = new Resgrid.Tests.Security.Live.InMemoryCacheProvider();
				var stored = ObjectSerialization.Serialize(new DispatcherDeskConfig { Enabled = false });
				var settingsRepository = new Mock<IDepartmentSettingsRepository>();
				var settings = new Mock<IDepartmentSettingsService>();
				var service = new DispatcherDeskConfigService(settingsRepository.Object, settings.Object, cache);

				settings.Setup(x => x.SaveOrUpdateSettingAsync(Dept, It.IsAny<string>(), DepartmentSettingTypes.DispatcherDeskConfig, It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, string value, DepartmentSettingTypes t, CancellationToken c) =>
					{
						stored = value;
						return new DepartmentSetting { DepartmentId = d, Setting = value, SettingType = (int)t };
					});

				var saveDuringRead = true;
				settingsRepository.Setup(x => x.GetDepartmentSettingByIdTypeAsync(Dept, DepartmentSettingTypes.DispatcherDeskConfig)).Returns(async () =>
				{
					var read = new DepartmentSetting { DepartmentId = Dept, Setting = stored };
					if (saveDuringRead)
					{
						saveDuringRead = false;
						await service.SaveConfigAsync(Dept, new DispatcherDeskConfig { Enabled = true }, "admin");
					}

					return read;
				});

				var slow = await service.GetConfigAsync(Dept);
				var next = await service.GetConfigAsync(Dept);
				var cached = await service.GetConfigAsync(Dept);

				slow.Enabled.Should().BeFalse("it read before the save");
				next.Enabled.Should().BeTrue("the next read must see the saved settings");
				cached.Enabled.Should().BeTrue();
				settingsRepository.Verify(x => x.GetDepartmentSettingByIdTypeAsync(Dept, DepartmentSettingTypes.DispatcherDeskConfig), Times.Exactly(2),
					"the third read is a cache hit");
			}
			finally
			{
				SystemBehaviorConfig.CacheEnabled = previous;
			}
		}

		[Test]
		public async Task A_save_whose_cache_version_cannot_move_is_still_a_save()
		{
			var previous = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = true;
			try
			{
				var cache = new Mock<ICacheProvider>();
				cache.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(false);
				var settings = new Mock<IDepartmentSettingsService>();
				var service = new DispatcherDeskConfigService(new Mock<IDepartmentSettingsRepository>().Object, settings.Object, cache.Object);

				var saved = await service.SaveConfigAsync(Dept, new DispatcherDeskConfig { Enabled = true }, "admin");

				saved.Enabled.Should().BeTrue();
				settings.Verify(x => x.SaveOrUpdateSettingAsync(Dept, It.IsAny<string>(), DepartmentSettingTypes.DispatcherDeskConfig, It.IsAny<CancellationToken>()), Times.Once);
				cache.Verify(x => x.SetStringAsync(It.Is<string>(k => k.StartsWith("DSetDispatcherDeskConfigVersion1_")), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
			}
			finally
			{
				SystemBehaviorConfig.CacheEnabled = previous;
			}
		}

		// ------------------------------------------------------------------ desk worker

		private static DateTime _workerClock = new DateTime(2300, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>The worker's purge clock is process-wide, so each test starts hours past anything earlier.</summary>
		private static DateTime NextWorkerTime() => _workerClock = _workerClock.AddDays(1);

		[Test]
		public async Task A_failed_signed_in_lookup_skips_the_call_sweep_but_not_the_timeouts_or_the_purge()
		{
			var presences = new Mock<IDispatcherPresencesRepository>();
			presences.Setup(x => x.GetSignedInAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("database down"));
			var routing = new Mock<IDispatcherRoutingService>();
			var worker = new DispatcherDeskWorkerService(new Mock<IDispatcherBreaksService>().Object, new Mock<IDispatcherPresenceService>().Object, routing.Object,
				presences.Object);

			await worker.TickAsync(NextWorkerTime());

			routing.Verify(x => x.ProcessTimeoutsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			routing.Verify(x => x.PurgeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			routing.Verify(x => x.SyncCallsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_failed_purge_runs_again_on_the_next_tick()
		{
			var presences = new Mock<IDispatcherPresencesRepository>();
			presences.Setup(x => x.GetSignedInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<DispatcherPresence>());
			var routing = new Mock<IDispatcherRoutingService>();
			routing.SetupSequence(x => x.PurgeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("purge failed"))
				.ReturnsAsync(0)
				.ReturnsAsync(0);
			var worker = new DispatcherDeskWorkerService(new Mock<IDispatcherBreaksService>().Object, new Mock<IDispatcherPresenceService>().Object, routing.Object,
				presences.Object);

			var start = NextWorkerTime();
			await worker.TickAsync(start);
			await worker.TickAsync(start.AddMinutes(1));
			await worker.TickAsync(start.AddMinutes(2));

			routing.Verify(x => x.PurgeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(2),
				"the failed purge is retried at once; the successful one (no rows) waits the hour");
		}

		// ------------------------------------------------------------------ shift delete

		private (ShiftsService Service, Mock<IShiftsRepository> Shifts, Mock<IShiftDispatchersService> Dispatchers, Mock<IUnitOfWork> UnitOfWork,
			Mock<ISearchProjectionService> Search) ShiftsHarness()
		{
			var shifts = new Mock<IShiftsRepository>();
			var signups = new Mock<IShiftSignupRepository>();
			signups.Setup(x => x.GetAllShiftSignupsByShiftIdAsync(It.IsAny<int>())).ReturnsAsync(new List<ShiftSignup>());
			var dispatchers = new Mock<IShiftDispatchersService>();
			var unitOfWork = new Mock<IUnitOfWork>();
			var search = new Mock<ISearchProjectionService>();

			var service = new ShiftsService(shifts.Object, new Mock<IShiftPersonRepository>().Object, new Mock<IShiftDaysRepository>().Object,
				new Mock<IShiftGroupsRepository>().Object, signups.Object, new Mock<IShiftSignupTradeRepository>().Object, new Mock<IPersonnelRolesService>().Object,
				new Mock<IShiftSignupTradeUserRepository>().Object, new Mock<IShiftSignupTradeUserShiftsRepository>().Object, new Mock<IShiftStaffingRepository>().Object,
				new Mock<IShiftStaffingPersonRepository>().Object, new Mock<IDepartmentsService>().Object, new Mock<IDepartmentGroupsService>().Object,
				new Mock<IShiftGroupAssignmentsRepository>().Object, new Mock<IShiftGroupRolesRepository>().Object, new Mock<IEventAggregator>().Object,
				new Mock<IDepartmentSettingsService>().Object, searchProjections: new Lazy<ISearchProjectionService>(() => search.Object),
				shiftDispatchers: new Lazy<IShiftDispatchersService>(() => dispatchers.Object), unitOfWork: unitOfWork.Object);

			return (service, shifts, dispatchers, unitOfWork, search);
		}

		[Test]
		public async Task A_shift_delete_whose_desk_cleanup_fails_is_rolled_back_whole()
		{
			var h = ShiftsHarness();
			var shift = new Shift { ShiftId = 3, DepartmentId = Dept };
			h.Shifts.Setup(x => x.DeleteAsync(shift, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			h.Dispatchers.Setup(x => x.DeleteForShiftAsync(3, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("desk cleanup failed"));

			Func<Task> delete = () => h.Service.DeleteShift(shift);

			await delete.Should().ThrowAsync<InvalidOperationException>();
			h.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once, "the shift is still there for a retry to delete with its desk rows");
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Never);
			h.Search.Verify(x => x.RemoveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_shift_delete_commits_with_its_desk_rows_then_drops_the_search_entry()
		{
			var h = ShiftsHarness();
			var shift = new Shift { ShiftId = 3, DepartmentId = Dept };
			var order = new List<string>();
			h.Shifts.Setup(x => x.DeleteAsync(shift, It.IsAny<CancellationToken>())).Callback(() => order.Add("shift")).ReturnsAsync(true);
			h.Dispatchers.Setup(x => x.DeleteForShiftAsync(3, It.IsAny<CancellationToken>())).Callback(() => order.Add("desk")).Returns(Task.CompletedTask);
			h.UnitOfWork.Setup(x => x.CommitChanges()).Callback(() => order.Add("commit"));
			h.Search.Setup(x => x.RemoveAsync(Dept, SearchEntityTypes.Shift, "3", It.IsAny<CancellationToken>())).Callback(() => order.Add("search")).Returns(Task.CompletedTask);

			(await h.Service.DeleteShift(shift)).Should().BeTrue();

			order.Should().Equal(new[] { "shift", "desk", "commit", "search" });
		}
	}
}
