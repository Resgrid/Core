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
	public class MapLayersDocRepository: IMapLayersDocRepository
	{
		public async Task<List<MapLayer>> GetAllMapLayersByDepartmentIdAsync(int departmentId, MapLayerTypes type)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var mapLayersData = await connection.QueryAsync<MapLayer>($"SELECT data FROM public.maplayers ml WHERE ml.departmentid = {departmentId};");

				if (mapLayersData != null && mapLayersData.Any())
				{
					var mapLayers = mapLayersData.ToList();
					return mapLayers.Where(x => x.Type == (int)type && !x.IsDeleted).ToList();
				}
				else
					return new List<MapLayer>();
			}
		}

		public async Task<MapLayer> GetByIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var mapLayersData = await connection.QueryAsync<MapLayer>($"SELECT data FROM public.maplayers ul WHERE ul.oid = '{id}';");

				if (mapLayersData != null)
					return mapLayersData.FirstOrDefault();
				else
				{
					var mapLayersData2 = await connection.QueryAsync<MapLayer>($"SELECT data FROM public.maplayers ul WHERE ul.id = {id};");

					if (mapLayersData2 != null)
						return mapLayersData2.FirstOrDefault();
					else
						return null;
				}
			}
		}

		public async Task<MapLayer> GetByOldIdAsync(string id)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var mapLayersData = await connection.QueryAsync<MapLayer>($"SELECT data FROM public.maplayers ul WHERE ul.oid = '{id}';");

				if (mapLayersData != null)
					return mapLayersData.FirstOrDefault();
				else
					return null;
			}
		}

		public async Task<MapLayer> InsertAsync(MapLayer mapLayer)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();
				var result = await connection.ExecuteScalarAsync<string>($"INSERT INTO public.maplayers (departmentid, data) VALUES ({mapLayer.DepartmentId}, '{JsonConvert.SerializeObject(mapLayer)}') RETURNING id;");
				mapLayer.PgId = result;

				return mapLayer;
			}
		}

		public async Task<MapLayer> UpdateAsync(MapLayer mapLayer)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();

				if (!string.IsNullOrWhiteSpace(mapLayer.PgId))
					await connection.ExecuteAsync($"UPDATE public.maplayers SET data = '{JsonConvert.SerializeObject(mapLayer)}' WHERE id = {mapLayer.PgId};");


				return mapLayer;
			}
		}
	}
}
