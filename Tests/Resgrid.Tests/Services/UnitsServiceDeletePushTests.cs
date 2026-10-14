using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A deleted unit's devices are never told it is gone (the Unit app just drops the stale selection), so deleting
	/// the unit has to drop its Novu subscriber, or every rig and browser still on it keeps getting its pushes.
	/// </summary>
	[TestFixture]
	public class UnitsServiceDeletePushTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 7;
		private const string Code = "DEPT";

		private Mock<IUnitsRepository> _unitsRepository;
		private Mock<INovuProvider> _novuProvider;
		private Mock<IDepartmentsService> _departmentsService;
		private Unit _unit;
		private UnitsService _service;

		[SetUp]
		public void SetUp()
		{
			_unit = new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 7" };

			_unitsRepository = new Mock<IUnitsRepository>();
			_unitsRepository.Setup(x => x.GetByIdAsync(UnitId)).ReturnsAsync(() => _unit);
			_unitsRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Unit u, CancellationToken _, bool __) => u);

			_novuProvider = new Mock<INovuProvider>();
			_novuProvider.Setup(x => x.DeleteUnitSubscriber(UnitId, Code)).ReturnsAsync(true);

			_departmentsService = new Mock<IDepartmentsService>();
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = Code });

			_service = new UnitsService(
				_unitsRepository.Object, Mock.Of<IUnitStatesRepository>(), Mock.Of<IUnitLogsRepository>(),
				Mock.Of<IUnitTypesRepository>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(),
				Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(),
				new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				Mock.Of<IUnitActiveRolesRepository>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(),
				Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()),
				Mock.Of<ICallStatusAttributionService>(),
				novuProvider: _novuProvider.Object,
				departmentsService: new Lazy<IDepartmentsService>(() => _departmentsService.Object));
		}

		[Test]
		public async Task Deleting_a_unit_should_drop_its_push_subscriber()
		{
			var result = await _service.DeleteUnitAsync(UnitId, "admin");

			result.Should().BeTrue();
			_unit.IsDeleted.Should().BeTrue();
			_novuProvider.Verify(x => x.DeleteUnitSubscriber(UnitId, Code), Times.Once);
		}

		[Test]
		public async Task The_unit_should_stay_deleted_when_the_subscriber_could_not_be_dropped()
		{
			_novuProvider.Setup(x => x.DeleteUnitSubscriber(UnitId, Code)).ReturnsAsync(false);

			var result = await _service.DeleteUnitAsync(UnitId, "admin");

			result.Should().BeTrue();
			_unit.IsDeleted.Should().BeTrue();
		}

		[Test]
		public async Task The_unit_should_stay_deleted_when_novu_throws()
		{
			_novuProvider.Setup(x => x.DeleteUnitSubscriber(UnitId, Code)).ThrowsAsync(new TimeoutException());

			var result = await _service.DeleteUnitAsync(UnitId, "admin");

			result.Should().BeTrue();
			_unit.IsDeleted.Should().BeTrue();
		}

		[Test]
		public async Task A_department_without_a_code_should_not_reach_novu()
		{
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = null });

			await _service.DeleteUnitAsync(UnitId, "admin");

			_novuProvider.VerifyNoOtherCalls();
		}

		[Test]
		public async Task An_already_deleted_unit_should_not_reach_novu_again()
		{
			_unit.IsDeleted = true;

			var result = await _service.DeleteUnitAsync(UnitId, "admin");

			result.Should().BeTrue();
			_novuProvider.VerifyNoOtherCalls();
		}
	}
}
