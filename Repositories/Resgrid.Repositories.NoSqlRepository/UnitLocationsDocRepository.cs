using Npgsql;
using Resgrid.Model;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

		public async Task<UnitsLocation> InsertAsync(UnitsLocation location)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));

			var dataJson = JsonConvert.SerializeObject(location);

			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var result = await connection.ExecuteScalarAsync<string>(
					"INSERT INTO public.unitlocations (departmentid, unitid, data) VALUES (@departmentId, @unitId, CAST(@dataJson AS jsonb)) RETURNING id::text;",
					new
					{
						departmentId = location.DepartmentId,
						unitId = location.UnitId,
						dataJson
					});
				location.PgId = result;

				return location;
			}
		}

		public async Task<UnitsLocation> UpdateAsync(UnitsLocation location)
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

				await connection.ExecuteAsync(
					"UPDATE public.unitlocations SET departmentid = @departmentId, unitid = @unitId, data = CAST(@dataJson AS jsonb) WHERE id = @id;",
					new
					{
						departmentId = location.DepartmentId,
						unitId = location.UnitId,
						dataJson,
						id = pgId
					});

				return location;
			}
		}
	}
}
