using Npgsql;
using Resgrid.Model;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Repositories;

namespace Resgrid.Repositories.NoSqlRepository
{
	public class UnitLocationsDocRepository: IUnitLocationsDocRepository
	{
		public async Task<List<UnitsLocation>> GetAllLocationsByUnitIdAsync(int unitId)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>(
					"SELECT data FROM public.unitlocations ul WHERE ul.unitid = @unitId ORDER BY timestamp DESC;",
					new { unitId });

				if (unitLocationsData != null)
					return unitLocationsData.ToList();
				else
					return new List<UnitsLocation>();
			}
		}

		public async Task<UnitsLocation> GetLatestLocationsByUnitIdAsync(int unitId)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>(
					"SELECT data FROM public.unitlocations ul WHERE ul.unitid = @unitId ORDER BY timestamp DESC LIMIT 1;",
					new { unitId });

				if (unitLocationsData != null)
					return unitLocationsData.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<List<UnitsLocation>> GetLatestLocationsByDepartmentIdAsync(int departmentId)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>(
					"SELECT DISTINCT ON (unitid) data FROM public.unitlocations ul WHERE ul.departmentid = @departmentId ORDER BY ul.unitid, ul.timestamp DESC;",
					new { departmentId });

				if (unitLocationsData != null)
					return unitLocationsData.ToList();
				else
					return new List<UnitsLocation>();
			}
		}

		public async Task<UnitsLocation> GetByIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>(
					"SELECT data FROM public.unitlocations ul WHERE ul.oid = @id;",
					new { id });

				if (unitLocationsData != null && unitLocationsData.Any())
					return unitLocationsData.FirstOrDefault();

				if (!int.TryParse(id, out var numericId))
					return null;

				var unitLocationsData2 = await connection.QueryAsync<UnitsLocation>(
					"SELECT data FROM public.unitlocations ul WHERE ul.id = @id;",
					new { id = numericId });

				if (unitLocationsData2 != null && unitLocationsData2.Any())
					return unitLocationsData2.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<UnitsLocation> GetByOldIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>(
					"SELECT data FROM public.unitlocations ul WHERE ul.oid = @id;",
					new { id });

				if (unitLocationsData != null)
					return unitLocationsData.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<UnitLocationWriteResult> InsertAsync(UnitsLocation location)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));

			var dataJson = JsonConvert.SerializeObject(location);

			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var result = await connection.ExecuteScalarAsync<string>(
					@"INSERT INTO public.unitlocations
						(departmentid, unitid, ""timestamp"", eventid, receivedon, sourcetype, sourceid, sourcepriority, data)
					VALUES
						(@departmentId, @unitId, @timestamp, @eventId, @receivedOn, @sourceType, @sourceId, @sourcePriority, CAST(@dataJson AS jsonb))
					ON CONFLICT (eventid) WHERE eventid IS NOT NULL DO NOTHING
					RETURNING id::text;",
					new
					{
						departmentId = location.DepartmentId,
						unitId = location.UnitId,
						timestamp = ToPostgresTimestamp(location.Timestamp),
						eventId = NullIfWhiteSpace(location.EventId),
						receivedOn = location.ReceivedOn.HasValue ? ToPostgresTimestamp(location.ReceivedOn.Value) : (DateTime?)null,
						sourceType = location.SourceType,
						sourceId = NullIfWhiteSpace(location.SourceId),
						sourcePriority = location.SourcePriority,
						dataJson
					});

				if (string.IsNullOrWhiteSpace(result))
					return UnitLocationWriteResult.Duplicate(location);

				location.PgId = result;

				return UnitLocationWriteResult.Inserted(location);
			}
		}

		public async Task<UnitLocationWriteResult> UpdateAsync(UnitsLocation location)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));

			if (string.IsNullOrWhiteSpace(location.PgId))
				throw new InvalidOperationException("Unit location PgId is required for updates.");

			if (!int.TryParse(location.PgId, out var pgId))
				throw new ArgumentException("Unit location PgId must be a valid integer.", nameof(location));

			var dataJson = JsonConvert.SerializeObject(location);

			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();

				var affectedRows = await connection.ExecuteAsync(
					@"UPDATE public.unitlocations
					SET departmentid = @departmentId,
						unitid = @unitId,
						""timestamp"" = @timestamp,
						eventid = @eventId,
						receivedon = @receivedOn,
						sourcetype = @sourceType,
						sourceid = @sourceId,
						sourcepriority = @sourcePriority,
						data = CAST(@dataJson AS jsonb)
					WHERE id = @id;",
					new
					{
						departmentId = location.DepartmentId,
						unitId = location.UnitId,
						timestamp = ToPostgresTimestamp(location.Timestamp),
						eventId = NullIfWhiteSpace(location.EventId),
						receivedOn = location.ReceivedOn.HasValue ? ToPostgresTimestamp(location.ReceivedOn.Value) : (DateTime?)null,
						sourceType = location.SourceType,
						sourceId = NullIfWhiteSpace(location.SourceId),
						sourcePriority = location.SourcePriority,
						dataJson,
						id = pgId
					});

				if (affectedRows != 1)
					throw new InvalidOperationException($"Unit location '{location.PgId}' was not found for update.");

				return UnitLocationWriteResult.Inserted(location);
			}
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

			using var connection = new NpgsqlConnection(
				Config.DataConfig.DocumentConnectionString);
			await connection.OpenAsync(cancellationToken);
			var command = new Dapper.CommandDefinition(
				@"WITH expired AS
					(
						SELECT id
						FROM public.unitlocations
						WHERE departmentid = @departmentId
							AND sourcetype = @sourceType
							AND ""timestamp"" < @cutoff
						ORDER BY ""timestamp"", id
						LIMIT @batchSize
						FOR UPDATE SKIP LOCKED
					)
					DELETE FROM public.unitlocations AS locations
					USING expired
					WHERE locations.id = expired.id;",
				new
				{
					departmentId,
					sourceType =
						(int)UnitLocationSourceType
							.HardwareTracker,
					cutoff =
						ToPostgresTimestamp(cutoffUtc),
					batchSize
				},
				cancellationToken: cancellationToken);
			return await connection.ExecuteAsync(command);
		}

		private static DateTime ToPostgresTimestamp(DateTime value)
		{
			return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
		}

		private static string NullIfWhiteSpace(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}
	}
}
