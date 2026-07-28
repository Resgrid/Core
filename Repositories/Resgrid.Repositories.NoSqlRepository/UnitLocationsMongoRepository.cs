using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Repositories.NoSqlRepository
{
	public class UnitLocationsMongoRepository : IUnitLocationsMongoRepository
	{
		private readonly IMongoCollection<UnitsLocation> _collection;
		private readonly object _indexLock = new object();
		private Task? _ensureIndexesTask;

		public UnitLocationsMongoRepository()
		{
			var database = new MongoClient(DataConfig.NoSqlConnectionString).GetDatabase(DataConfig.NoSqlDatabaseName);
			_collection = database.GetCollection<UnitsLocation>("unitLocations");
		}

		public async Task<UnitLocationWriteResult> InsertAsync(UnitsLocation location)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));

			await EnsureIndexesAsync();

			try
			{
				await _collection.InsertOneAsync(location);
				return UnitLocationWriteResult.Inserted(location);
			}
			catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
			{
				return UnitLocationWriteResult.Duplicate(location);
			}
		}

		public async Task<UnitLocationWriteResult> UpdateAsync(UnitsLocation location)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));

			await EnsureIndexesAsync();

			var filter = Builders<UnitsLocation>.Filter.Eq(document => document.Id, location.Id);
			var result = await _collection.ReplaceOneAsync(filter, location);

			if (result.MatchedCount != 1)
				throw new InvalidOperationException($"Unit location '{location.Id}' was not found for update.");

			return UnitLocationWriteResult.Inserted(location);
		}

		public async Task<int>
			DeleteHardwareLocationsBeforeAsync(
				int departmentId,
				DateTime cutoffUtc,
				int batchSize,
				CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0)
				throw new ArgumentOutOfRangeException(
					nameof(departmentId));
			if (cutoffUtc.Kind != DateTimeKind.Utc)
			{
				throw new ArgumentException(
					"The retention cutoff must be UTC.",
					nameof(cutoffUtc));
			}
			if (batchSize <= 0)
				throw new ArgumentOutOfRangeException(
					nameof(batchSize));

			await EnsureIndexesAsync().WaitAsync(
				cancellationToken);
			var filter =
				Builders<UnitsLocation>.Filter.And(
					Builders<UnitsLocation>.Filter.Eq(
						location => location.DepartmentId,
						departmentId),
					Builders<UnitsLocation>.Filter.Eq(
						location => location.SourceType,
						(int)UnitLocationSourceType
							.HardwareTracker),
					Builders<UnitsLocation>.Filter.Lt(
						location => location.Timestamp,
						cutoffUtc));
			var ids = await _collection
				.Find(filter)
				.SortBy(location => location.Timestamp)
				.ThenBy(location => location.Id)
				.Limit(batchSize)
				.Project(location => location.Id)
				.ToListAsync(cancellationToken);
			if (ids.Count == 0)
				return 0;

			var result = await _collection.DeleteManyAsync(
				Builders<UnitsLocation>.Filter.In(
					location => location.Id,
					ids),
				cancellationToken);
			return checked((int)result.DeletedCount);
		}

		public Task EnsureIndexesAsync()
		{
			lock (_indexLock)
			{
				return _ensureIndexesTask ??= CreateIndexesAndClearOnFailureAsync();
			}
		}

		private async Task CreateIndexesAndClearOnFailureAsync()
		{
			try
			{
				await CreateIndexesAsync();
			}
			catch
			{
				lock (_indexLock)
				{
					_ensureIndexesTask = null;
				}

				throw;
			}
		}

		private async Task CreateIndexesAsync()
		{
			var indexes = new List<CreateIndexModel<UnitsLocation>>
			{
				new CreateIndexModel<UnitsLocation>(
					Builders<UnitsLocation>.IndexKeys.Ascending(location => location.EventId),
					new CreateIndexOptions { Name = "ux_unitlocations_eventid", Unique = true, Sparse = true }),
				new CreateIndexModel<UnitsLocation>(
					Builders<UnitsLocation>.IndexKeys
						.Ascending(location => location.DepartmentId)
						.Ascending(location => location.UnitId)
						.Descending(location => location.Timestamp),
					new CreateIndexOptions { Name = "ix_unitlocations_department_unit_timestamp" }),
				new CreateIndexModel<UnitsLocation>(
					Builders<UnitsLocation>.IndexKeys
						.Ascending(location => location.DepartmentId)
						.Ascending(location => location.UnitId)
						.Ascending(location => location.SourceType)
						.Ascending(location => location.SourceId)
						.Descending(location => location.Timestamp),
					new CreateIndexOptions { Name = "ix_unitlocations_department_unit_source_timestamp" }),
				new CreateIndexModel<UnitsLocation>(
					Builders<UnitsLocation>.IndexKeys
						.Ascending(location => location.DepartmentId)
						.Ascending(location => location.SourceType)
						.Ascending(location => location.Timestamp),
					new CreateIndexOptions { Name = "ix_unitlocations_retention" })
			};

			await _collection.Indexes.CreateManyAsync(indexes);
		}
	}
}
