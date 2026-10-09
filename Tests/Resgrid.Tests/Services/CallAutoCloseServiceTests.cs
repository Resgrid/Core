using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Belgian EMS, 2026-10-07: a call closes on its own when its last unit reports back in service or out of service
	/// (DepartmentSettingTypes.CloseCallWhenUnitsClear), and stays open for the dispatcher whenever that is not certain.
	/// </summary>
	[TestFixture]
	public class CallAutoCloseServiceTests
	{
		private const int DeptId = 7;
		private const int CallId = 40;
		private const int UnitA = 1;
		private const int UnitB = 2;

		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ICustomStateService> _customStates;
		private Mock<ICallDispatchUnitRepository> _unitDispatches;
		private Mock<IUnitStatesRepository> _unitStates;
		private Mock<ICallsService> _calls;
		private Mock<ICallClosureService> _closure;
		private Mock<ICallDispatchStatusService> _dispatchStatuses;
		private Mock<IProtectedWriteService> _protectedWrite;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUnitsRepository> _units;
		private Mock<IEventAggregator> _events;
		private Mock<ICacheProvider> _cache;
		private CallAutoCloseService _service;

		private DateTime _now;
		private Call _call;
		private Call _saved;

		[SetUp]
		public void SetUp()
		{
			_now = DateTime.UtcNow;

			_settings = new Mock<IDepartmentSettingsService>();
			_customStates = new Mock<ICustomStateService>();
			_unitDispatches = new Mock<ICallDispatchUnitRepository>();
			_unitStates = new Mock<IUnitStatesRepository>();
			_calls = new Mock<ICallsService>();
			_closure = new Mock<ICallClosureService>();
			_dispatchStatuses = new Mock<ICallDispatchStatusService>();
			_protectedWrite = new Mock<IProtectedWriteService>();
			_departments = new Mock<IDepartmentsService>();
			_units = new Mock<IUnitsRepository>();
			_events = new Mock<IEventAggregator>();
			_cache = new Mock<ICacheProvider>();

			_settings.Setup(x => x.GetCloseCallWhenUnitsClearAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(true);
			_customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DeptId)).ReturnsAsync(new List<CustomState>());
			_unitDispatches.Setup(x => x.GetOpenCallUnitDispatchesForUnitAsync(DeptId, UnitA)).ReturnsAsync(() => new List<CallDispatchUnit>
			{
				new CallDispatchUnit { CallId = CallId, UnitId = UnitA, DispatchedOn = _now.AddMinutes(-40) }
			});
			_unitDispatches.Setup(x => x.GetCallUnitDispatchesByCallIdAsync(CallId)).ReturnsAsync(() => new List<CallDispatchUnit>
			{
				new CallDispatchUnit { CallId = CallId, UnitId = UnitA, DispatchedOn = _now.AddMinutes(-40) },
				new CallDispatchUnit { CallId = CallId, UnitId = UnitB, DispatchedOn = _now.AddMinutes(-40) }
			});
			// Unit B came back in service ten minutes ago.
			_unitStates.Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitB))
				.ReturnsAsync(new UnitState { UnitStateId = 70, UnitId = UnitB, State = (int)UnitStateTypes.Available, Timestamp = _now.AddMinutes(-10) });

			_call = new Call { CallId = CallId, DepartmentId = DeptId, State = (int)CallStates.Active, HasBeenDispatched = true, UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = UnitA } } };
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool _, bool __, bool ___, bool ____, bool _____, bool ______, bool _______, bool ________, bool _________, bool __________) => c);
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Callback<Call, CancellationToken>((c, _) => _saved = c)
				.ReturnsAsync((Call c, CancellationToken _) => c);

			_protectedWrite.Setup(x => x.PrepareCallWriteAsync(DeptId, It.IsAny<Call>(), null, null, It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_departments.Setup(x => x.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, ManagingUserId = "manager" });
			_units.Setup(x => x.GetByIdAsync(UnitA)).ReturnsAsync(new Unit { UnitId = UnitA, DepartmentId = DeptId, Name = "Ambulance Zottegem" });
			_cache.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);

			_service = new CallAutoCloseService(_settings.Object, _customStates.Object, _unitDispatches.Object, _unitStates.Object, _calls.Object,
				_closure.Object, _dispatchStatuses.Object, new Lazy<IProtectedWriteService>(() => _protectedWrite.Object), _departments.Object,
				_units.Object, _events.Object, _cache.Object);
		}

		private UnitState Saved(int state, int? callId = CallId, DateTime? timestamp = null) => new UnitState
		{
			UnitStateId = 101,
			UnitId = UnitA,
			State = state,
			Timestamp = timestamp ?? _now,
			DestinationId = callId,
			DestinationType = callId.HasValue ? (int)DestinationEntityTypes.Call : (int?)null,
			SetByUserId = "crew-1"
		};

		private UnitState Previous(int state, int? destinationId = CallId, DestinationEntityTypes type = DestinationEntityTypes.Call) => new UnitState
		{
			UnitStateId = 100,
			UnitId = UnitA,
			State = state,
			Timestamp = _now.AddMinutes(-5),
			DestinationId = destinationId,
			DestinationType = destinationId.HasValue ? (int)type : (int?)null
		};

		private void VerifyNotClosed()
		{
			_calls.Verify(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_events.Verify(x => x.SendMessage(It.IsAny<CallClosedEvent>()), Times.Never);
		}

		[Test]
		public async Task closes_the_call_when_its_last_unit_comes_back_in_service()
		{
			var closed = await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene));

			closed.Should().Equal(CallId);
			_saved.State.Should().Be((int)CallStates.Closed);
			_saved.ClosedByUserId.Should().Be("crew-1");
			_saved.ClosedOn.Should().NotBeNull();
			_saved.CompletedNotes.Should().Contain("Ambulance Zottegem");
			_events.Verify(x => x.SendMessage(It.Is<CallClosedEvent>(e => e.DepartmentId == DeptId && e.Call.CallId == CallId)), Times.Once);
			_dispatchStatuses.Verify(x => x.ApplyReleaseStatusesAsync(It.Is<Call>(c => c.CallId == CallId), null, null, It.IsAny<CancellationToken>()), Times.Once);
			_closure.Verify(x => x.NotifyCallClosedAsync(It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task out_of_service_also_finishes_the_call()
		{
			var closed = await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.OutOfService), Previous((int)UnitStateTypes.OnScene));

			closed.Should().Equal(CallId);
		}

		[Test]
		public async Task custom_statuses_count_by_their_base_type()
		{
			_customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DeptId)).ReturnsAsync(new List<CustomState>
			{
				new CustomState
				{
					CustomStateId = 3, DepartmentId = DeptId, Type = (int)CustomStateTypes.Unit,
					Details = new List<CustomStateDetail>
					{
						new CustomStateDetail { CustomStateDetailId = 601, ButtonText = "Arrived Hospital", BaseType = (int)ActionBaseTypes.AtHospital },
						new CustomStateDetail { CustomStateDetailId = 602, ButtonText = "Radio Available", BaseType = (int)ActionBaseTypes.InQuarters },
						new CustomStateDetail { CustomStateDetailId = 603, ButtonText = "Radio Check", BaseType = (int)ActionBaseTypes.None }
					}
				}
			});

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved(603), Previous(601))).Should().BeEmpty("a status with no base type means nothing");
			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved(602), Previous(601))).Should().Equal(CallId);
		}

		[Test]
		public async Task stays_open_while_another_unit_is_still_working()
		{
			_unitStates.Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitB))
				.ReturnsAsync(new UnitState { UnitStateId = 70, UnitId = UnitB, State = (int)UnitStateTypes.Returning, Timestamp = _now.AddMinutes(-2) });

			var closed = await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene));

			closed.Should().BeEmpty();
			VerifyNotClosed();
		}

		[Test]
		public async Task stays_open_while_another_unit_has_not_reported_since_its_dispatch()
		{
			_unitStates.Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitB))
				.ReturnsAsync(new UnitState { UnitStateId = 70, UnitId = UnitB, State = (int)UnitStateTypes.Available, Timestamp = _now.AddHours(-2) });

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			VerifyNotClosed();
		}

		[Test]
		public async Task does_nothing_when_the_department_has_not_turned_it_on()
		{
			_settings.Setup(x => x.GetCloseCallWhenUnitsClearAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(false);

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			_unitDispatches.Verify(x => x.GetOpenCallUnitDispatchesForUnitAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
			VerifyNotClosed();
		}

		[TestCase(UnitStateTypes.Returning, UnitStateTypes.OnScene, Description = "still working the call")]
		[TestCase(UnitStateTypes.OutOfService, UnitStateTypes.Available, Description = "was already finished")]
		[TestCase(UnitStateTypes.Available, UnitStateTypes.OutOfService, Description = "was already finished")]
		public async Task only_the_moment_a_unit_leaves_a_call_counts(UnitStateTypes saved, UnitStateTypes previous)
		{
			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)saved), Previous((int)previous))).Should().BeEmpty();
			VerifyNotClosed();
		}

		[Test]
		public async Task a_replayed_offline_status_does_not_close_anything()
		{
			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available, timestamp: _now.AddMinutes(-30)),
				Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			VerifyNotClosed();
		}

		[Test]
		public async Task an_active_incident_command_keeps_the_call_open()
		{
			_closure.Setup(x => x.GetBlockingIncidentCommandAsync(DeptId, CallId)).ReturnsAsync(new IncidentCommand());

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			VerifyNotClosed();
		}

		[Test]
		public async Task two_units_clearing_together_close_the_call_once()
		{
			_cache.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(2);

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			VerifyNotClosed();
		}

		[Test]
		public async Task a_blocked_protected_write_leaves_the_call_open()
		{
			_protectedWrite.Setup(x => x.PrepareCallWriteAsync(DeptId, It.IsAny<Call>(), null, null, It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			VerifyNotClosed();
			_cache.Verify(x => x.RemoveAsync("CallAutoClose_40"), Times.Once, "the call is still open, so the next status may try again");
		}

		[Test]
		public async Task a_failed_save_releases_the_close_claim()
		{
			_calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			_events.Verify(x => x.SendMessage(It.IsAny<CallClosedEvent>()), Times.Never);
			_cache.Verify(x => x.RemoveAsync("CallAutoClose_40"), Times.Once);
		}

		[Test]
		public async Task a_closed_call_keeps_its_close_claim()
		{
			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().Equal(CallId);
			_cache.Verify(x => x.RemoveAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task a_status_set_for_another_call_does_not_close_this_one()
		{
			// The unit cleared call 55, which it was never dispatched to; its dispatch to 40 stays open.
			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available, callId: 55), Previous((int)UnitStateTypes.OnScene, 55))).Should().BeEmpty();
			VerifyNotClosed();
		}

		[Test]
		public async Task after_a_hospital_leg_the_call_is_the_last_one_the_unit_reported_on()
		{
			// Dispatched to 40 now and to 39 long ago (never closed). On scene at 40, then to the hospital (a POI), then back.
			const int StaleCallId = 39;
			_unitDispatches.Setup(x => x.GetOpenCallUnitDispatchesForUnitAsync(DeptId, UnitA)).ReturnsAsync(new List<CallDispatchUnit>
			{
				new CallDispatchUnit { CallId = StaleCallId, UnitId = UnitA, DispatchedOn = _now.AddDays(-3) },
				new CallDispatchUnit { CallId = CallId, UnitId = UnitA, DispatchedOn = _now.AddMinutes(-40) }
			});
			// The history is read from the earliest open dispatch, the stale one.
			_unitStates.Setup(x => x.GetAllUnitStatesForUnitInDateRangeAsync(UnitA, _now.AddDays(-3), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<UnitState>
				{
					new UnitState { UnitStateId = 80, UnitId = UnitA, State = (int)UnitStateTypes.OnScene, Timestamp = _now.AddDays(-3), DestinationId = StaleCallId, DestinationType = (int)DestinationEntityTypes.Call },
					new UnitState { UnitStateId = 98, UnitId = UnitA, State = (int)UnitStateTypes.OnScene, Timestamp = _now.AddMinutes(-30), DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call },
					new UnitState { UnitStateId = 99, UnitId = UnitA, State = (int)UnitStateTypes.Committed, Timestamp = _now.AddMinutes(-20), DestinationId = 9, DestinationType = (int)DestinationEntityTypes.Poi },
					new UnitState { UnitStateId = 100, UnitId = UnitA, State = (int)UnitStateTypes.Committed, Timestamp = _now.AddMinutes(-5), DestinationId = 9, DestinationType = (int)DestinationEntityTypes.Poi }
				});

			var closed = await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available, callId: null),
				Previous((int)UnitStateTypes.Committed, 9, DestinationEntityTypes.Poi));

			closed.Should().Equal(CallId);
			_calls.Verify(x => x.GetCallByIdAsync(StaleCallId, It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task a_failure_is_logged_and_leaves_the_call_open()
		{
			_unitDispatches.Setup(x => x.GetOpenCallUnitDispatchesForUnitAsync(DeptId, UnitA)).ThrowsAsync(new InvalidOperationException("db down"));

			(await _service.CloseCallsFinishedByUnitStateAsync(DeptId, Saved((int)UnitStateTypes.Available), Previous((int)UnitStateTypes.OnScene))).Should().BeEmpty();
			VerifyNotClosed();
		}
	}

	/// <summary>The status save path runs the auto-close for unit reports only, never for statuses Resgrid sets itself.</summary>
	[TestFixture]
	public class UnitsServiceAutoCloseHookTests
	{
		private Mock<IUnitStatesRepository> _unitStatesRepo;
		private Mock<ICallAutoCloseService> _autoClose;
		private UnitsService _service;

		[SetUp]
		public void SetUp()
		{
			_unitStatesRepo = new Mock<IUnitStatesRepository>();
			_unitStatesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<UnitState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UnitState s, CancellationToken _, bool __) => s);
			_unitStatesRepo.Setup(x => x.GetLastUnitStateByUnitIdAsync(7))
				.ReturnsAsync(new UnitState { UnitStateId = 54, UnitId = 7, State = (int)UnitStateTypes.OnScene, Timestamp = DateTime.UtcNow.AddMinutes(-5) });

			var protectedWrite = new Mock<IProtectedWriteService>();
			protectedWrite.Setup(x => x.PrepareUnitStateWriteAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_autoClose = new Mock<ICallAutoCloseService>();
			_autoClose.Setup(x => x.CloseCallsFinishedByUnitStateAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<int>());

			_service = new UnitsService(
				Mock.Of<IUnitsRepository>(), _unitStatesRepo.Object, Mock.Of<IUnitLogsRepository>(),
				Mock.Of<IUnitTypesRepository>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(),
				Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(),
				new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				Mock.Of<IUnitActiveRolesRepository>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(),
				Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => protectedWrite.Object),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()),
				Mock.Of<ICallStatusAttributionService>(),
				callAutoCloseService: new Lazy<ICallAutoCloseService>(() => _autoClose.Object));
		}

		private static UnitState Available() => new UnitState { UnitId = 7, State = (int)UnitStateTypes.Available, Timestamp = DateTime.UtcNow };

		[Test]
		public async Task a_unit_report_is_checked_with_the_status_it_replaced()
		{
			await _service.SetUnitStateAsync(Available(), 10);

			_autoClose.Verify(x => x.CloseCallsFinishedByUnitStateAsync(10, It.Is<UnitState>(s => s.State == (int)UnitStateTypes.Available),
				It.Is<UnitState>(p => p.UnitStateId == 54), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task statuses_resgrid_sets_itself_never_close_a_call()
		{
			await _service.SetUnitStateAsync(Available(), 10, autoGenerated: true);

			_autoClose.Verify(x => x.CloseCallsFinishedByUnitStateAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task an_auto_close_failure_does_not_fail_the_status_save()
		{
			_autoClose.Setup(x => x.CloseCallsFinishedByUnitStateAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("boom"));

			var saved = await _service.SetUnitStateAsync(Available(), 10);

			saved.Should().NotBeNull();
		}
	}
}
