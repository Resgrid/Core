using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>T0 gap closed in T1: members who leave a unit's crew are queued for worker 83, like members who join it.</summary>
	[TestFixture]
	public class UnitCrewAudienceChangeTests
	{
		[Test]
		public async Task Clearing_a_units_crew_queues_every_former_crew_member()
		{
			var roles = new Mock<IUnitActiveRolesRepository>();
			roles.Setup(r => r.GetActiveRolesByUnitIdAsync(4)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = 4, DepartmentId = 9, UserId = "a" }, new UnitActiveRole { UnitId = 4, DepartmentId = 9, UserId = "A" },
				new UnitActiveRole { UnitId = 4, DepartmentId = 9, UserId = "b" }, new UnitActiveRole { UnitId = 4, DepartmentId = 9 }
			});
			roles.Setup(r => r.DeleteActiveRolesByUnitIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var queue = new Mock<IContentAudienceChangeQueue>();
			var service = new UnitsService(
				Mock.Of<IUnitsRepository>(), Mock.Of<IUnitStatesRepository>(), Mock.Of<IUnitLogsRepository>(), Mock.Of<IUnitTypesRepository>(), Mock.Of<ISubscriptionsService>(),
				Mock.Of<IUnitRolesRepository>(), Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(), Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()), Mock.Of<IUnitLocationsDocRepository>(),
				new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()), roles.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(),
				Mock.Of<IPersonnelRolesService>(), new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()), new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()),
				Mock.Of<ICallStatusAttributionService>(), audienceChanges: queue.Object);

			(await service.DeleteActiveRolesForUnitAsync(4)).Should().BeTrue();

			queue.Verify(q => q.EnqueueManyAsync(9, It.Is<IEnumerable<string>>(ids => ids.Count() == 2), ContentAudienceChangeSources.Unit, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
