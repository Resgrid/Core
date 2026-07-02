using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DocumentDatabaseProviderSelectionTests
	{
		private DatabaseTypes _originalDocDatabaseType;

		[SetUp]
		public void SetUp()
		{
			_originalDocDatabaseType = DataConfig.DocDatabaseType;
		}

		[TearDown]
		public void TearDown()
		{
			DataConfig.DocDatabaseType = _originalDocDatabaseType;
		}

		[Test]
		public async Task GetLatestUnitLocationsAsync_should_use_postgres_doc_repository_when_doc_database_is_postgres()
		{
			DataConfig.DocDatabaseType = DatabaseTypes.Postgres;

			var expected = new List<UnitsLocation>
			{
				new UnitsLocation { DepartmentId = 7, UnitId = 12, PgId = "42" }
			};

			var eventAggregator = new Mock<IEventAggregator>();
			var unitLocationsDocRepository = new Mock<IUnitLocationsDocRepository>();
			unitLocationsDocRepository.Setup(x => x.GetLatestLocationsByDepartmentIdAsync(7)).ReturnsAsync(expected);

			var service = CreateUnitsService(
				eventAggregator.Object,
				new Lazy<IMongoRepository<UnitsLocation>>(() => throw new InvalidOperationException("Mongo repository should not be resolved in Postgres mode.")),
				unitLocationsDocRepository.Object);

			var result = await service.GetLatestUnitLocationsAsync(7);

			result.Should().BeEquivalentTo(expected);
			unitLocationsDocRepository.Verify(x => x.GetLatestLocationsByDepartmentIdAsync(7), Times.Once);
		}

		[Test]
		public async Task AddUnitLocationAsync_should_publish_postgres_record_id_when_doc_database_is_postgres()
		{
			DataConfig.DocDatabaseType = DatabaseTypes.Postgres;

			var eventAggregator = new Mock<IEventAggregator>();
			var unitLocationsDocRepository = new Mock<IUnitLocationsDocRepository>();
			unitLocationsDocRepository
				.Setup(x => x.InsertAsync(It.IsAny<UnitsLocation>()))
				.ReturnsAsync((UnitsLocation location) =>
				{
					location.PgId = "314";
					return location;
				});

			var service = CreateUnitsService(
				eventAggregator.Object,
				new Lazy<IMongoRepository<UnitsLocation>>(() => throw new InvalidOperationException("Mongo repository should not be resolved in Postgres mode.")),
				unitLocationsDocRepository.Object);

			var location = new UnitsLocation
			{
				DepartmentId = 7,
				UnitId = 12,
				Latitude = 39.7392m,
				Longitude = -104.9903m,
				Timestamp = DateTime.UtcNow
			};

			var result = await service.AddUnitLocationAsync(location, 7);

			result.PgId.Should().Be("314");
			unitLocationsDocRepository.Verify(x => x.InsertAsync(location), Times.Once);
			eventAggregator.Verify(
				x => x.SendMessage<UnitLocationUpdatedEvent>(It.Is<UnitLocationUpdatedEvent>(e => e.RecordId == "314" && e.UnitId == "12")),
				Times.Once);
		}

		[Test]
		public async Task GetLatestLocationsForDepartmentPersonnelAsync_should_use_postgres_doc_repository_when_doc_database_is_postgres()
		{
			DataConfig.DocDatabaseType = DatabaseTypes.Postgres;

			var expected = new List<PersonnelLocation>
			{
				new PersonnelLocation { DepartmentId = 7, UserId = "user-1", PgId = "77" }
			};

			var personnelLocationsDocRepository = new Mock<IPersonnelLocationsDocRepository>();
			personnelLocationsDocRepository.Setup(x => x.GetLatestLocationsByDepartmentIdAsync(7)).ReturnsAsync(expected);

			var service = CreateUsersService(
				new Mock<IEventAggregator>().Object,
				new Lazy<IMongoRepository<PersonnelLocation>>(() => throw new InvalidOperationException("Mongo repository should not be resolved in Postgres mode.")),
				personnelLocationsDocRepository.Object);

			var result = await service.GetLatestLocationsForDepartmentPersonnelAsync(7);

			result.Should().BeEquivalentTo(expected);
			personnelLocationsDocRepository.Verify(x => x.GetLatestLocationsByDepartmentIdAsync(7), Times.Once);
		}

		[Test]
		public async Task SavePersonnelLocationAsync_should_publish_postgres_record_id_when_doc_database_is_postgres()
		{
			DataConfig.DocDatabaseType = DatabaseTypes.Postgres;

			var eventAggregator = new Mock<IEventAggregator>();
			var personnelLocationsDocRepository = new Mock<IPersonnelLocationsDocRepository>();
			personnelLocationsDocRepository
				.Setup(x => x.InsertAsync(It.IsAny<PersonnelLocation>()))
				.ReturnsAsync((PersonnelLocation location) =>
				{
					location.PgId = "512";
					return location;
				});

			var service = CreateUsersService(
				eventAggregator.Object,
				new Lazy<IMongoRepository<PersonnelLocation>>(() => throw new InvalidOperationException("Mongo repository should not be resolved in Postgres mode.")),
				personnelLocationsDocRepository.Object);

			var location = new PersonnelLocation
			{
				DepartmentId = 7,
				UserId = "user-1",
				Latitude = 39.7392m,
				Longitude = -104.9903m,
				Timestamp = DateTime.UtcNow
			};

			var result = await service.SavePersonnelLocationAsync(location);

			result.PgId.Should().Be("512");
			personnelLocationsDocRepository.Verify(x => x.InsertAsync(location), Times.Once);
			eventAggregator.Verify(
				x => x.SendMessage<PersonnelLocationUpdatedEvent>(It.Is<PersonnelLocationUpdatedEvent>(e => e.RecordId == "512" && e.UserId == "user-1")),
				Times.Once);
		}

		[Test]
		public async Task GetMapLayersForTypeDepartmentAsync_should_not_resolve_mongo_repository_when_doc_database_is_postgres()
		{
			DataConfig.DocDatabaseType = DatabaseTypes.Postgres;

			var expected = new List<MapLayer>
			{
				new MapLayer { DepartmentId = 7, PgId = "9001", Type = (int)MapLayerTypes.TopLevel }
			};

			var mapLayersDocRepository = new Mock<IMapLayersDocRepository>();
			mapLayersDocRepository.Setup(x => x.GetAllMapLayersByDepartmentIdAsync(7, MapLayerTypes.TopLevel)).ReturnsAsync(expected);

			var service = new MappingService(
				new Mock<IPoiTypesRepository>().Object,
				new Mock<IPoisRepository>().Object,
				new Lazy<IMongoRepository<MapLayer>>(() => throw new InvalidOperationException("Mongo repository should not be resolved in Postgres mode.")),
				mapLayersDocRepository.Object);

			var result = await service.GetMapLayersForTypeDepartmentAsync(7, MapLayerTypes.TopLevel);

			result.Should().BeEquivalentTo(expected);
			mapLayersDocRepository.Verify(x => x.GetAllMapLayersByDepartmentIdAsync(7, MapLayerTypes.TopLevel), Times.Once);
		}

		private static UnitsService CreateUnitsService(IEventAggregator eventAggregator, Lazy<IMongoRepository<UnitsLocation>> mongoRepository, IUnitLocationsDocRepository unitLocationsDocRepository)
		{
			return new UnitsService(
				new Mock<IUnitsRepository>().Object,
				new Mock<IUnitStatesRepository>().Object,
				new Mock<IUnitLogsRepository>().Object,
				new Mock<IUnitTypesRepository>().Object,
				new Mock<ISubscriptionsService>().Object,
				new Mock<IUnitRolesRepository>().Object,
				new Mock<IUnitStateRoleRepository>().Object,
				new Mock<IUserStateService>().Object,
				eventAggregator,
				new Mock<ICustomStateService>().Object,
				mongoRepository,
				unitLocationsDocRepository,
				new Mock<IUnitActiveRolesRepository>().Object,
				new Mock<IDepartmentGroupsService>().Object,
				new Mock<ILimitsService>().Object,
				new Mock<IPersonnelRolesService>().Object);
		}

		private static UsersService CreateUsersService(IEventAggregator eventAggregator, Lazy<IMongoRepository<PersonnelLocation>> mongoRepository, IPersonnelLocationsDocRepository personnelLocationsDocRepository)
		{
			return new UsersService(
				new Mock<IDepartmentMembersRepository>().Object,
				new Mock<ICacheProvider>().Object,
				new Mock<IIdentityRepository>().Object,
				new Mock<IDepartmentSettingsService>().Object,
				mongoRepository,
				personnelLocationsDocRepository,
				eventAggregator,
				new Mock<ILimitsService>().Object);
		}
	}
}
