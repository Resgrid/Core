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
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>($"SELECT data FROM public.unitlocations ul WHERE ul.unitid = {unitId} ORDER BY timestamp DESC;");

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
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>($"SELECT data FROM public.unitlocations ul WHERE ul.unitid = {unitId} ORDER BY timestamp DESC LIMIT 1;");

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
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>($"SELECT DISTINCT ON (unitid) data FROM public.unitlocations ul WHERE ul.departmentid = {departmentId} ORDER BY ul.unitid, ul.timestamp DESC;");

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
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>($"SELECT data FROM public.unitlocations ul WHERE ul.oid = '{id}';");

				if (unitLocationsData != null && unitLocationsData.Any())
					return unitLocationsData.FirstOrDefault();
				else
				{
					var unitLocationsData2 = await connection.QueryAsync<UnitsLocation>($"SELECT data FROM public.unitlocations ul WHERE ul.id = {id};");

					if (unitLocationsData2 != null)
						return unitLocationsData2.FirstOrDefault();
					else
						return null;
				}
			}
		}

		public async Task<UnitsLocation> GetByOldIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<UnitsLocation>($"SELECT data FROM public.unitlocations ul WHERE ul.oid = '{id}';");

				if (unitLocationsData != null)
					return unitLocationsData.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<UnitsLocation> InsertAsync(UnitsLocation location)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var result = await connection.ExecuteScalarAsync<string>($"INSERT INTO public.unitlocations (departmentid, unitid, data) VALUES ({location.DepartmentId}, {location.UnitId}, '{JsonConvert.SerializeObject(location)}') RETURNING id;");
				location.PgId = result;

				return location;
			}
		}

		public async Task<UnitsLocation> UpdateAsync(UnitsLocation location)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();

				if (!string.IsNullOrWhiteSpace(location.PgId))
					await connection.ExecuteAsync($"UPDATE public.unitlocations SET departmentid = {location.DepartmentId}, unitid = {location.UnitId}, data = '{JsonConvert.SerializeObject(location)}' WHERE id = {location.PgId};");

				return location;
			}
		}
	}
}
