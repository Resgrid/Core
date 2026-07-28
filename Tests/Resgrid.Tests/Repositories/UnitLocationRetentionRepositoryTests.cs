using System;
using System.Threading;
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
	[NonParallelizable]
	public class UnitLocationRetentionRepositoryTests
	{
		private DatabaseTypes _originalDocumentDatabaseType;

		[SetUp]
		public void SetUp()
		{
			_originalDocumentDatabaseType =
				DataConfig.DocDatabaseType;
		}

		[TearDown]
		public void TearDown()
		{
			DataConfig.DocDatabaseType =
				_originalDocumentDatabaseType;
		}

		[Test]
		public async Task DeleteHardwareLocationsBeforeAsync_WithPostgres_RoutesOnlyToPostgres()
		{
			// Arrange
			DataConfig.DocDatabaseType =
				DatabaseTypes.Postgres;
			var cutoffUtc = new DateTime(
				2026,
				6,
				1,
				0,
				0,
				0,
				DateTimeKind.Utc);
			var postgres =
				new Mock<IUnitLocationsDocRepository>();
			postgres
				.Setup(repository =>
					repository
						.DeleteHardwareLocationsBeforeAsync(
							7,
							cutoffUtc,
							100,
							It.IsAny<CancellationToken>()))
				.ReturnsAsync(12);
			var mongo =
				new Mock<IUnitLocationsMongoRepository>(
					MockBehavior.Strict);
			var repository =
				new UnitLocationRetentionRepository(
					new Lazy<IUnitLocationsDocRepository>(
						() => postgres.Object),
					new Lazy<IUnitLocationsMongoRepository>(
						() => mongo.Object));

			// Act
			var deleted =
				await repository
					.DeleteHardwareLocationsBeforeAsync(
						7,
						cutoffUtc,
						100);

			// Assert
			deleted.Should().Be(12);
			mongo.VerifyNoOtherCalls();
		}

		[Test]
		public async Task DeleteHardwareLocationsBeforeAsync_WithMongo_RoutesOnlyToMongo()
		{
			// Arrange
			DataConfig.DocDatabaseType =
				DatabaseTypes.MongoDb;
			var cutoffUtc = new DateTime(
				2026,
				6,
				1,
				0,
				0,
				0,
				DateTimeKind.Utc);
			var postgres =
				new Mock<IUnitLocationsDocRepository>(
					MockBehavior.Strict);
			var mongo =
				new Mock<IUnitLocationsMongoRepository>();
			mongo
				.Setup(repository =>
					repository
						.DeleteHardwareLocationsBeforeAsync(
							7,
							cutoffUtc,
							100,
							It.IsAny<CancellationToken>()))
				.ReturnsAsync(9);
			var repository =
				new UnitLocationRetentionRepository(
					new Lazy<IUnitLocationsDocRepository>(
						() => postgres.Object),
					new Lazy<IUnitLocationsMongoRepository>(
						() => mongo.Object));

			// Act
			var deleted =
				await repository
					.DeleteHardwareLocationsBeforeAsync(
						7,
						cutoffUtc,
						100);

			// Assert
			deleted.Should().Be(9);
			postgres.VerifyNoOtherCalls();
		}
	}
}
