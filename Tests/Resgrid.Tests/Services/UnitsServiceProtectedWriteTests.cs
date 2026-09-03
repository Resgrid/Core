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
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()));
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
		public async Task The_status_type_overload_is_covered_too()
		{
			await _service.SetUnitStateAsync(7, 2, DeptId);

			_protectedWriteService.Verify(x => x.PrepareUnitStateWriteAsync(DeptId, It.IsAny<UnitState>(), null, null, true, It.IsAny<CancellationToken>()),
				Times.Once, "both SetUnitStateAsync overloads persist a unit state and both must be covered");
		}
	}
}
