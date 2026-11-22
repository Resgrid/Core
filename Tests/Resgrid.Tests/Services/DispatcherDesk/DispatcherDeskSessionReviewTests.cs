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
