using System;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.NoSqlRepository;

namespace Resgrid.Tests.Repositories
{
	[TestFixture]
	public class DocumentDbRepositoryTests
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
		public async Task UpdateDocumentDatabaseAsync_should_ensure_unit_location_indexes_for_mongodb()
		{
			DataConfig.DocDatabaseType = DatabaseTypes.MongoDb;
			var mongoRepository = new Mock<IUnitLocationsMongoRepository>();
			mongoRepository
				.Setup(repository => repository.EnsureIndexesAsync())
				.Returns(Task.CompletedTask);
			var personnelIndexes = new Mock<IMongoIndexManager<PersonnelLocation>>();
			var personnelCollection = new Mock<IMongoCollection<PersonnelLocation>>();
			personnelCollection.SetupGet(collection => collection.Indexes).Returns(personnelIndexes.Object);
			var personnelRepository = new Mock<IMongoRepository<PersonnelLocation>>();
			personnelRepository.Setup(repository => repository.GetCollection()).Returns(personnelCollection.Object);
			var repository = new DocumentDbRepository(
				new Lazy<IUnitLocationsMongoRepository>(() => mongoRepository.Object),
				new Lazy<IMongoRepository<PersonnelLocation>>(() => personnelRepository.Object));

			var result = await repository.UpdateDocumentDatabaseAsync();

			result.Should().BeTrue();
			mongoRepository.Verify(locationRepository => locationRepository.EnsureIndexesAsync(), Times.Once);
			personnelIndexes.Verify(indexes => indexes.CreateOneAsync(
				It.Is<CreateIndexModel<PersonnelLocation>>(model => model.Options.Name == "ix_personnellocations_department_timestamp"),
				It.IsAny<CreateOneIndexOptions>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
		}
	}
}
