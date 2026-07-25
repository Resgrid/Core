using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
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
			var repository = new DocumentDbRepository(
				new Lazy<IUnitLocationsMongoRepository>(() => mongoRepository.Object));

			var result = await repository.UpdateDocumentDatabaseAsync();

			result.Should().BeTrue();
			mongoRepository.Verify(locationRepository => locationRepository.EnsureIndexesAsync(), Times.Once);
		}
	}
}
