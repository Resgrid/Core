using Dapper;
using Newtonsoft.Json;
using Npgsql;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Repositories.NoSqlRepository
{
	public class PersonnelLocationsDocRepository : IPersonnelLocationsDocRepository
	{
		public async Task<List<PersonnelLocation>> GetAllLocationsByUnitIdAsync(string userId)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var personLocationsData = await connection.QueryAsync<PersonnelLocation>(
					"SELECT data FROM public.personnellocations ul WHERE ul.userid = @userId ORDER BY timestamp DESC;",
					new { userId });

				if (personLocationsData != null)
					return personLocationsData.ToList();
				else
					return new List<PersonnelLocation>();
			}
		}

		public async Task<PersonnelLocation> GetLatestLocationsByUnitIdAsync(string userId)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<PersonnelLocation>(
					"SELECT data FROM public.personnellocations ul WHERE ul.userid = @userId ORDER BY timestamp DESC LIMIT 1;",
					new { userId });

				if (unitLocationsData != null)
					return unitLocationsData.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<List<PersonnelLocation>> GetLatestLocationsByDepartmentIdAsync(int departmentId)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<PersonnelLocation>(
					"SELECT DISTINCT ON (userid) data FROM public.personnellocations ul WHERE ul.departmentid = @departmentId ORDER BY ul.userid, ul.timestamp DESC;",
					new { departmentId });

				if (unitLocationsData != null)
					return unitLocationsData.ToList();
				else
					return new List<PersonnelLocation>();
			}
		}

		public async Task<PersonnelLocation> GetByIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var unitLocationsData = await connection.QueryAsync<PersonnelLocation>(
					"SELECT data FROM public.personnellocations ul WHERE ul.oid = @id;",
					new { id });

				if (unitLocationsData != null && unitLocationsData.Any())
					return unitLocationsData.FirstOrDefault();

				if (!int.TryParse(id, out var numericId))
					return null;

				var unitLocationsData2 = await connection.QueryAsync<PersonnelLocation>(
					"SELECT data FROM public.personnellocations ul WHERE ul.id = @id;",
					new { id = numericId });

				if (unitLocationsData2 != null && unitLocationsData2.Any())
					return unitLocationsData2.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<PersonnelLocation> GetByOldIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var personnelLocationsData = await connection.QueryAsync<PersonnelLocation>(
					"SELECT data FROM public.personnellocations ul WHERE ul.oid = @id;",
					new { id });

				if (personnelLocationsData != null)
					return personnelLocationsData.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<PersonnelLocation> InsertAsync(PersonnelLocation location)
		{
			var dataJson = JsonConvert.SerializeObject(location);

			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var result = await connection.ExecuteScalarAsync<string>(
					"INSERT INTO public.personnellocations (departmentid, userid, data) VALUES (@departmentId, @userId, CAST(@dataJson AS jsonb)) RETURNING id::text;",
					new
					{
						departmentId = location.DepartmentId,
						userId = location.UserId,
						dataJson
					});
				location.PgId = result;

				return location;
			}
		}

		public async Task<PersonnelLocation> UpdateAsync(PersonnelLocation location)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));

			if (string.IsNullOrWhiteSpace(location.PgId))
				throw new InvalidOperationException("Personnel location PgId is required for updates.");

			if (!int.TryParse(location.PgId, out var pgId))
				throw new ArgumentException("Personnel location PgId must be a valid integer.", nameof(location));

			var dataJson = JsonConvert.SerializeObject(location);

			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();

				await connection.ExecuteAsync(
					"UPDATE public.personnellocations SET departmentid = @departmentId, userid = @userId, data = CAST(@dataJson AS jsonb) WHERE id = @id;",
					new
					{
						departmentId = location.DepartmentId,
						userId = location.UserId,
						dataJson,
						id = pgId
					});

				return location;
			}
		}
	}
}
