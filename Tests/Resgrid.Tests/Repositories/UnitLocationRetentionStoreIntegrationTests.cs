using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Repositories.NoSqlRepository;

namespace Resgrid.Tests.Repositories
{
	[TestFixture]
	[NonParallelizable]
	public class UnitLocationRetentionStoreIntegrationTests
	{
		private const string PostgresConnectionEnvironmentVariable =
			"RESGRID_TRACKING_POSTGRES_DOCUMENT_CONNECTION";
		private const string MongoConnectionEnvironmentVariable =
			"RESGRID_TRACKING_MONGODB_CONNECTION";
		private const string MongoDatabaseEnvironmentVariable =
			"RESGRID_TRACKING_MONGODB_DATABASE";

		private string _originalDocumentConnectionString;
		private string _originalMongoConnectionString;
		private string _originalMongoDatabaseName;

		[SetUp]
		public void SetUp()
		{
			_originalDocumentConnectionString =
				DataConfig.DocumentConnectionString;
			_originalMongoConnectionString =
				DataConfig.NoSqlConnectionString;
			_originalMongoDatabaseName =
				DataConfig.NoSqlDatabaseName;
		}

		[TearDown]
		public void TearDown()
		{
			DataConfig.DocumentConnectionString =
				_originalDocumentConnectionString;
			DataConfig.NoSqlConnectionString =
				_originalMongoConnectionString;
			DataConfig.NoSqlDatabaseName =
				_originalMongoDatabaseName;
		}

		[Test]
		[Explicit(
			"Requires a migrated PostgreSQL document test database.")]
		[Category("LiveTrackingStore")]
		public async Task PostgresRetention_LiveStore_DeletesOnlyBoundedExpiredHardwareRows()
		{
			var connectionString =
				RequireEnvironmentVariable(
					PostgresConnectionEnvironmentVariable);
			var connectionBuilder =
				new NpgsqlConnectionStringBuilder(
					connectionString);
			RequireTestDatabase(connectionBuilder.Database);
			DataConfig.DocumentConnectionString =
				connectionString;

			var fixture = CreateFixture();
			var repository =
				new UnitLocationsDocRepository();
			try
			{
				foreach (var location in fixture.Locations)
					await repository.InsertAsync(location);

				var deleted =
					await repository
						.DeleteHardwareLocationsBeforeAsync(
							fixture.DepartmentId,
							fixture.CutoffUtc,
							batchSize: 1);
				var secondBatch =
					await repository
						.DeleteHardwareLocationsBeforeAsync(
							fixture.DepartmentId,
							fixture.CutoffUtc,
							batchSize: 1);
				var remaining =
					await GetPostgresEventIdsAsync(
						connectionString,
						fixture.EventPrefix);

				deleted.Should().Be(1);
				secondBatch.Should().Be(0);
				remaining.Should().BeEquivalentTo(
					fixture.ExpectedRemainingEventIds);
			}
			finally
			{
				await DeletePostgresFixtureAsync(
					connectionString,
					fixture.EventPrefix);
			}
		}

		[Test]
		[Explicit(
			"Requires a dedicated MongoDB tracking test database.")]
		[Category("LiveTrackingStore")]
		public async Task MongoRetention_LiveStore_DeletesOnlyBoundedExpiredHardwareDocuments()
		{
			var connectionString =
				RequireEnvironmentVariable(
					MongoConnectionEnvironmentVariable);
			var databaseName =
				RequireEnvironmentVariable(
					MongoDatabaseEnvironmentVariable);
			RequireTestDatabase(databaseName);
			DataConfig.NoSqlConnectionString =
				connectionString;
			DataConfig.NoSqlDatabaseName =
				databaseName;

			var fixture = CreateFixture();
			var client = new MongoClient(connectionString);
			var collection = client
				.GetDatabase(databaseName)
				.GetCollection<UnitsLocation>(
					"unitLocations");
			var eventFilter = EventPrefixFilter(
				fixture.EventPrefix);
			var repository =
				new UnitLocationsMongoRepository();
			try
			{
				foreach (var location in fixture.Locations)
					await repository.InsertAsync(location);

				var deleted =
					await repository
						.DeleteHardwareLocationsBeforeAsync(
							fixture.DepartmentId,
							fixture.CutoffUtc,
							batchSize: 1);
				var secondBatch =
					await repository
						.DeleteHardwareLocationsBeforeAsync(
							fixture.DepartmentId,
							fixture.CutoffUtc,
							batchSize: 1);
				var remaining = await collection
					.Find(eventFilter)
					.Project(location => location.EventId)
					.ToListAsync();

				deleted.Should().Be(1);
				secondBatch.Should().Be(0);
				remaining.Should().BeEquivalentTo(
					fixture.ExpectedRemainingEventIds);
			}
			finally
			{
				await collection.DeleteManyAsync(
					eventFilter);
			}
		}

		private static RetentionFixture CreateFixture()
		{
			var eventPrefix =
				"retention-integration-" +
				Guid.NewGuid().ToString("N");
			var departmentId = Random.Shared.Next(
				1000000,
				1500000);
			var otherDepartmentId =
				departmentId + 1500000;
			var unitId = departmentId;
			var otherUnitId = otherDepartmentId;
			var now = DateTime.UtcNow;
			var oldTimestamp = now.AddDays(-60);
			var currentTimestamp = now.AddDays(-5);
			var oldHardwareEvent =
				eventPrefix + "-old-hardware";
			var oldUnitAppEvent =
				eventPrefix + "-old-unit-app";
			var currentHardwareEvent =
				eventPrefix + "-current-hardware";
			var otherDepartmentEvent =
				eventPrefix + "-other-department";

			return new RetentionFixture(
				eventPrefix,
				departmentId,
				now.AddDays(-30),
				new[]
				{
					CreateLocation(
						oldHardwareEvent,
						departmentId,
						unitId,
						oldTimestamp,
						UnitLocationSourceType.HardwareTracker),
					CreateLocation(
						oldUnitAppEvent,
						departmentId,
						unitId,
						oldTimestamp,
						UnitLocationSourceType.UnitApp),
					CreateLocation(
						currentHardwareEvent,
						departmentId,
						unitId,
						currentTimestamp,
						UnitLocationSourceType.HardwareTracker),
					CreateLocation(
						otherDepartmentEvent,
						otherDepartmentId,
						otherUnitId,
						oldTimestamp,
						UnitLocationSourceType.HardwareTracker)
				},
				new[]
				{
					oldUnitAppEvent,
					currentHardwareEvent,
					otherDepartmentEvent
				});
		}

		private static UnitsLocation CreateLocation(
			string eventId,
			int departmentId,
			int unitId,
			DateTime timestampUtc,
			UnitLocationSourceType sourceType)
		{
			return new UnitsLocation
			{
				EventId = eventId,
				DepartmentId = departmentId,
				UnitId = unitId,
				Timestamp = timestampUtc,
				ReceivedOn = timestampUtc,
				SourceType = (int)sourceType,
				SourceId = eventId,
				SourcePriority = 100,
				Latitude = 47.6062m,
				Longitude = -122.3321m
			};
		}

		private static string RequireEnvironmentVariable(
			string name)
		{
			var value =
				Environment.GetEnvironmentVariable(name);
			if (string.IsNullOrWhiteSpace(value))
			{
				Assert.Fail(
					$"Environment variable '{name}' is required.");
			}

			return value;
		}

		private static void RequireTestDatabase(
			string databaseName)
		{
			if (string.IsNullOrWhiteSpace(databaseName) ||
				(!databaseName.EndsWith(
					 "_test",
					 StringComparison.OrdinalIgnoreCase) &&
				 !databaseName.EndsWith(
					 "_integration",
					 StringComparison.OrdinalIgnoreCase)))
			{
				Assert.Fail(
					"Live retention tests require a database name ending in '_test' or '_integration'.");
			}
		}

		private static FilterDefinition<UnitsLocation>
			EventPrefixFilter(string eventPrefix)
		{
			return Builders<UnitsLocation>.Filter.Regex(
				location => location.EventId,
				new BsonRegularExpression(
					"^" + Regex.Escape(eventPrefix)));
		}

		private static async Task<IReadOnlyCollection<string>>
			GetPostgresEventIdsAsync(
				string connectionString,
				string eventPrefix)
		{
			var eventIds = new List<string>();
			await using var connection =
				new NpgsqlConnection(connectionString);
			await connection.OpenAsync();
			await using var command = new NpgsqlCommand(
				@"SELECT eventid
					FROM public.unitlocations
					WHERE eventid LIKE @pattern
					ORDER BY eventid;",
				connection);
			command.Parameters.AddWithValue(
				"pattern",
				eventPrefix + "%");
			await using var reader =
				await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
				eventIds.Add(reader.GetString(0));
			return eventIds;
		}

		private static async Task DeletePostgresFixtureAsync(
			string connectionString,
			string eventPrefix)
		{
			await using var connection =
				new NpgsqlConnection(connectionString);
			await connection.OpenAsync();
			await using var command = new NpgsqlCommand(
				@"DELETE FROM public.unitlocations
					WHERE eventid LIKE @pattern;",
				connection);
			command.Parameters.AddWithValue(
				"pattern",
				eventPrefix + "%");
			await command.ExecuteNonQueryAsync();
		}

		private sealed class RetentionFixture
		{
			public RetentionFixture(
				string eventPrefix,
				int departmentId,
				DateTime cutoffUtc,
				IReadOnlyCollection<UnitsLocation> locations,
				IReadOnlyCollection<string>
					expectedRemainingEventIds)
			{
				EventPrefix = eventPrefix;
				DepartmentId = departmentId;
				CutoffUtc = cutoffUtc;
				Locations = locations;
				ExpectedRemainingEventIds =
					expectedRemainingEventIds;
			}

			public string EventPrefix { get; }
			public int DepartmentId { get; }
			public DateTime CutoffUtc { get; }
			public IReadOnlyCollection<UnitsLocation>
				Locations
			{ get; }
			public IReadOnlyCollection<string>
				ExpectedRemainingEventIds
			{ get; }
		}
	}
}
