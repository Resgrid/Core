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
	/// A crew change (a unit's active roles replaced, or its seats released on delete) is announced as
	/// unitActiveRolesUpdated, so live unit maps reload who rides the unit. The seats are already saved when it is
	/// published, so a broker failure must never fail the save.
	/// </summary>
	[TestFixture]
	public class UnitActiveRolesNotificationTests
	{
		private Mock<IEventAggregator> _events;
		private Mock<IUnitsRepository> _units;
		private Mock<IUnitActiveRolesRepository> _activeRoles;

		[SetUp]
		public void SetUp()
		{
			_events = new Mock<IEventAggregator>();
			_units = new Mock<IUnitsRepository>();
			_activeRoles = new Mock<IUnitActiveRolesRepository>();
		}

		private UnitsService Service() => new UnitsService(_units.Object, Mock.Of<IUnitStatesRepository>(), Mock.Of<IUnitLogsRepository>(), Mock.Of<IUnitTypesRepository>(),
			Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(), Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(),
			_events.Object, Mock.Of<ICustomStateService>(), new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
			Mock.Of<IUnitLocationsDocRepository>(), new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
			_activeRoles.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(), Mock.Of<IPersonnelRolesService>(),
			new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()), new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()), Mock.Of<ICallStatusAttributionService>());

		[Test]
		public async Task A_crew_change_is_announced_for_the_unit()
		{
			await Service().NotifyActiveRolesUpdatedAsync(77, 501);

			_events.Verify(x => x.SendMessageAsync(It.Is<UnitActiveRolesUpdatedEvent>(e => e.DepartmentId == 77 && e.UnitId == 501)), Times.Once);
		}

		[Test]
		public async Task A_broker_failure_does_not_fail_the_saved_seats()
		{
			_events.Setup(x => x.SendMessageAsync(It.IsAny<UnitActiveRolesUpdatedEvent>())).ThrowsAsync(new InvalidOperationException("broker down"));

			await FluentActions.Awaiting(() => Service().NotifyActiveRolesUpdatedAsync(77, 501)).Should().NotThrowAsync();
		}

		[Test]
		public async Task Nothing_is_announced_without_a_department()
		{
			await Service().NotifyActiveRolesUpdatedAsync(0, 501);

			_events.Verify(x => x.SendMessageAsync(It.IsAny<UnitActiveRolesUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async Task Deleting_a_unit_announces_its_released_crew()
		{
			var unit = new Unit { UnitId = 501, DepartmentId = 77, Name = "Engine 1" };
			_units.Setup(u => u.GetByIdAsync(501)).ReturnsAsync(unit);
			_units.Setup(u => u.SaveOrUpdateAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Unit u, CancellationToken _, bool _) => u);
			_activeRoles.Setup(r => r.DeleteActiveRolesByUnitIdAsync(501, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await Service().DeleteUnitAsync(501, "admin")).Should().BeTrue();

			_events.Verify(x => x.SendMessageAsync(It.Is<UnitActiveRolesUpdatedEvent>(e => e.DepartmentId == 77 && e.UnitId == 501)), Times.Once);
		}

		[Test]
		public void The_eventing_type_keeps_its_wire_value()
		{
			((int)EventingTypes.UnitActiveRolesUpdated).Should().Be(15);
		}
	}
}
