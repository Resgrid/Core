using System;
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
	/// Pins the catalog-v2 write safety net in UnitsService.SetUnitStateAsync: every caller — v4 API,
	/// the apps, unit-tracking ingress, workers — goes through this one method, so a protected
	/// department's unit-state note, geolocation and coordinates are enveloped here rather than at
	/// each call site, and a blocked write throws instead of leaving plaintext at rest.
	/// </summary>
	[TestFixture]
	public class UnitsServiceProtectedWriteTests
	{
		private const int DeptId = 10;

		private Mock<IUnitStatesRepository> _unitStatesRepo;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private Mock<IEventAggregator> _eventAggregator;
		private UnitsService _service;

		[SetUp]
		public void SetUp()
		{
			_unitStatesRepo = new Mock<IUnitStatesRepository>();
			_unitStatesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<UnitState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UnitState s, CancellationToken _, bool __) => s);

			_protectedWriteService = new Mock<IProtectedWriteService>();
			_protectedWriteService.Setup(x => x.PrepareUnitStateWriteAsync(It.IsAny<int>(), It.IsAny<UnitState>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_eventAggregator = new Mock<IEventAggregator>();

			_service = new UnitsService(
				Mock.Of<IUnitsRepository>(), _unitStatesRepo.Object, Mock.Of<IUnitLogsRepository>(),
				Mock.Of<IUnitTypesRepository>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(),
				Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(), _eventAggregator.Object,
				Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(),
				new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				Mock.Of<IUnitActiveRolesRepository>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(),
				Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()),
				Mock.Of<ICallStatusAttributionService>());
		}

		private static UnitState BuildState() => new UnitState
		{
			UnitStateId = 55,
			UnitId = 7,
			State = 2,
			Note = "Crew of 3",
			Timestamp = DateTime.UtcNow
		};

		[Test]
		public async Task Unprotected_department_saves_once_and_leaves_the_note_alone()
		{
			var state = BuildState();

			var result = await _service.SetUnitStateAsync(state, DeptId);

			result.Note.Should().Be("Crew of 3");
			_protectedWriteService.Verify(x => x.PrepareUnitStateWriteAsync(DeptId, It.IsAny<UnitState>(), null, null, true, It.IsAny<CancellationToken>()), Times.Once);
			_unitStatesRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<UnitState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Protected_department_repersists_the_enveloped_state()
		{
			_protectedWriteService.Setup(x => x.PrepareUnitStateWriteAsync(DeptId, It.IsAny<UnitState>(), null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true));

			await _service.SetUnitStateAsync(BuildState(), DeptId);

			// Initial save plus the re-save that persists the envelopes.
			_unitStatesRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<UnitState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
		}

		[Test]
		public async Task Blocked_protected_write_throws_rather_than_leaving_plaintext()
		{
			_protectedWriteService.Setup(x => x.PrepareUnitStateWriteAsync(DeptId, It.IsAny<UnitState>(), null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			Func<Task> act = async () => await _service.SetUnitStateAsync(BuildState(), DeptId);

			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*broker_unavailable*");
		}

		[Test]
		public async Task Blocked_protected_write_still_announces_the_saved_status_without_its_sealed_fields()
		{
			// The row is saved before the write can be enveloped, so it is the unit's latest status for every reader. Without the
			// event, boards that refresh only on the push (BigBoard, the Dispatch app) kept the previous status.
			_protectedWriteService.Setup(x => x.PrepareUnitStateWriteAsync(DeptId, It.IsAny<UnitState>(), null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			UnitStatusEvent sent = null;
			_eventAggregator.Setup(x => x.SendMessage(It.IsAny<UnitStatusEvent>())).Callback<UnitStatusEvent>(e => sent = e);

			var state = BuildState();
			state.GeoLocationData = "50.85,4.35";
			state.Latitude = 50.85m;
			state.Longitude = 4.35m;
			state.Speed = 12m;
			state.DestinationId = 900;
			state.Roles = new System.Collections.Generic.List<UnitStateRole> { new UnitStateRole { Role = "Driver", UserId = "u1", UnitState = state } };

			Func<Task> act = async () => await _service.SetUnitStateAsync(state, DeptId, autoGenerated: true);

			await act.Should().ThrowAsync<InvalidOperationException>();

			sent.Should().NotBeNull();
			sent.DepartmentId.Should().Be(DeptId);
			sent.AutoGenerated.Should().BeTrue();
			sent.Status.UnitStateId.Should().Be(55);
			sent.Status.UnitId.Should().Be(7);
			sent.Status.State.Should().Be(2);
			sent.Status.DestinationId.Should().Be(900);
			sent.Status.Note.Should().BeNull("the note is the one protected unit-state field");
			// A unit's position is operational (2026-10-17) and is announced with the status.
			sent.Status.GeoLocationData.Should().Be("50.85,4.35");
			sent.Status.Latitude.Should().Be(50.85m);
			sent.Status.Longitude.Should().Be(4.35m);
			sent.Status.Speed.Should().Be(12m);
			sent.Status.Roles.Should().ContainSingle(r => r.Role == "Driver" && r.UserId == "u1" && r.UnitState == null);
		}

		[Test]
		public async Task Blocked_protected_write_on_the_status_type_overload_announces_too()
		{
			_protectedWriteService.Setup(x => x.PrepareUnitStateWriteAsync(DeptId, It.IsAny<UnitState>(), null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			Func<Task> act = async () => await _service.SetUnitStateAsync(7, 2, DeptId);

			await act.Should().ThrowAsync<InvalidOperationException>();
			_eventAggregator.Verify(x => x.SendMessage(It.Is<UnitStatusEvent>(e => e.Status.UnitId == 7 && e.Status.State == 2)), Times.Once);
		}

		[Test]
		public async Task Telemetry_the_columns_cannot_hold_is_fitted_before_the_insert()
		{
			// Sentry: a desktop browser's IP/Wi-Fi fix (accuracy 15 km) overflowed decimal(6,2) and the status was lost.
			UnitState saved = null;
			_unitStatesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<UnitState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback<UnitState, CancellationToken, bool>((s, _, __) => saved = s)
				.ReturnsAsync((UnitState s, CancellationToken _, bool __) => s);

			var state = BuildState();
			state.Latitude = 44.9778m;
			state.Longitude = -93.265m;
			state.Accuracy = 15000m;
			state.Speed = 5000m;

			await _service.SetUnitStateAsync(state, DeptId);

			saved.Should().NotBeNull();
			saved.Accuracy.Should().Be(9999.99m);
			saved.Speed.Should().BeNull();
			saved.Latitude.Should().Be(44.9778m);
			saved.Longitude.Should().Be(-93.265m);
		}

		[Test]
		public async Task The_status_type_overload_is_covered_too()
		{
			await _service.SetUnitStateAsync(7, 2, DeptId);

			_protectedWriteService.Verify(x => x.PrepareUnitStateWriteAsync(DeptId, It.IsAny<UnitState>(), null, null, true, It.IsAny<CancellationToken>()),
				Times.Once, "both SetUnitStateAsync overloads persist a unit state and both must be covered");
		}

		/// <summary>A service that can close calls, for the resumed-request follow-up (FinishUnitStateAsync).</summary>
		private (UnitsService Service, Mock<ICallAutoCloseService> AutoClose) WithCallAutoClose(UnitState saved, UnitState latest, int unitDepartmentId = DeptId)
		{
			var units = new Mock<IUnitsRepository>();
			units.Setup(x => x.GetByIdAsync(saved.UnitId)).ReturnsAsync(new Unit { UnitId = saved.UnitId, DepartmentId = unitDepartmentId });
			_unitStatesRepo.Setup(x => x.GetUnitStateByUnitStateIdAsync(saved.UnitStateId)).ReturnsAsync(saved);
			_unitStatesRepo.Setup(x => x.GetLastUnitStateByUnitIdAsync(saved.UnitId)).ReturnsAsync(latest);
			_unitStatesRepo.Setup(x => x.GetLastUnitStateBeforeIdAsync(saved.UnitId, saved.UnitStateId)).ReturnsAsync(new UnitState { UnitStateId = 54, UnitId = saved.UnitId });
			var autoClose = new Mock<ICallAutoCloseService>();

			var service = new UnitsService(
				units.Object, _unitStatesRepo.Object, Mock.Of<IUnitLogsRepository>(),
				Mock.Of<IUnitTypesRepository>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(),
				Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(), _eventAggregator.Object,
				Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(),
				new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				Mock.Of<IUnitActiveRolesRepository>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(),
				Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()),
				Mock.Of<ICallStatusAttributionService>(),
				callAutoCloseService: new Lazy<ICallAutoCloseService>(() => autoClose.Object));

			return (service, autoClose);
		}

		[Test]
		public async Task A_resumed_save_runs_the_call_close_out_the_failed_save_never_reached()
		{
			var saved = BuildState();
			var (service, autoClose) = WithCallAutoClose(saved, latest: saved);

			await service.FinishUnitStateAsync(DeptId, saved.UnitStateId);

			autoClose.Verify(x => x.CloseCallsFinishedByUnitStateAsync(DeptId, saved, It.Is<UnitState>(p => p.UnitStateId == 54), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_resumed_save_leaves_calls_alone_once_the_unit_has_set_a_newer_status()
		{
			// Closing calls on the older state could close a call the unit has since gone back to.
			var saved = BuildState();
			var (service, autoClose) = WithCallAutoClose(saved, latest: new UnitState { UnitStateId = 56, UnitId = saved.UnitId });

			await service.FinishUnitStateAsync(DeptId, saved.UnitStateId);

			autoClose.Verify(x => x.CloseCallsFinishedByUnitStateAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_resumed_save_never_runs_for_another_departments_unit()
		{
			var saved = BuildState();
			var (service, autoClose) = WithCallAutoClose(saved, latest: saved, unitDepartmentId: DeptId + 1);

			await service.FinishUnitStateAsync(DeptId, saved.UnitStateId);
			await service.ProtectUnitStateAsync(DeptId, saved.UnitStateId);

			autoClose.Verify(x => x.CloseCallsFinishedByUnitStateAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<UnitState>(), It.IsAny<CancellationToken>()), Times.Never);
			_protectedWriteService.Verify(x => x.PrepareUnitStateWriteAsync(It.IsAny<int>(), It.IsAny<UnitState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
