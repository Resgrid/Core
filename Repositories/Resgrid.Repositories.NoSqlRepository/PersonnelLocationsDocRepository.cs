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
				var personLocationsData = await connection.QueryAsync<PersonnelLocation>($"SELECT data FROM public.personnellocations ul WHERE ul.userid = '{userId}' ORDER BY timestamp DESC;");

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
				var unitLocationsData = await connection.QueryAsync<PersonnelLocation>($"SELECT data FROM public.personnellocations ul WHERE ul.userid = '{userId}' ORDER BY timestamp DESC LIMIT 1;");

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
				var unitLocationsData = await connection.QueryAsync<PersonnelLocation>($"SELECT DISTINCT ON (userid) data FROM public.personnellocations ul WHERE ul.departmentid = {departmentId} ORDER BY timestamp DESC;");

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
				var unitLocationsData = await connection.QueryAsync<PersonnelLocation>($"SELECT data FROM public.personnellocations ul WHERE ul.oid = '{id}';");

				if (unitLocationsData != null)
					return unitLocationsData.FirstOrDefault();
				else
				{
					var unitLocationsData2 = await connection.QueryAsync<PersonnelLocation>($"SELECT data FROM public.personnellocations ul WHERE ul.id = {id};");

					if (unitLocationsData2 != null)
						return unitLocationsData2.FirstOrDefault();
					else
						return null;
				}
			}
		}

		public async Task<PersonnelLocation> GetByOldIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var personnelLocationsData = await connection.QueryAsync<PersonnelLocation>($"SELECT data FROM public.personnellocations ul WHERE ul.oid = '{id}';");

				if (personnelLocationsData != null)
					return personnelLocationsData.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<PersonnelLocation> InsertAsync(PersonnelLocation location)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var result = await connection.ExecuteScalarAsync<string>($"INSERT INTO public.personnellocations (departmentid, userid, data) VALUES ({location.DepartmentId}, '{location.UserId}', '{JsonConvert.SerializeObject(location)}') RETURNING id;");
				location.PgId = result;

				return location;
			}
		}
	}
}
