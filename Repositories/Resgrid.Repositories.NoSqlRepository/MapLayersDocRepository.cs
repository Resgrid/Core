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
				var mapLayersData = await connection.QueryAsync<MapLayer>("SELECT data FROM public.maplayers ml WHERE ml.departmentid = @departmentId;", new { departmentId });

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
				var mapLayersData = await connection.QueryAsync<MapLayer>("SELECT data FROM public.maplayers ul WHERE ul.oid = @id;", new { id });

				if (mapLayersData != null && mapLayersData.Any())
					return mapLayersData.FirstOrDefault();
				else
				{
					if (!int.TryParse(id, out var numericId))
						return null;

					var mapLayersData2 = await connection.QueryAsync<MapLayer>("SELECT data FROM public.maplayers ul WHERE ul.id = @numericId;", new { numericId });

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
				var mapLayersData = await connection.QueryAsync<MapLayer>("SELECT data FROM public.maplayers ul WHERE ul.oid = @id;", new { id });

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
				var result = await connection.ExecuteScalarAsync<string>("INSERT INTO public.maplayers (departmentid, data) VALUES (@departmentId, CAST(@data AS jsonb)) RETURNING id;",
					new { departmentId = mapLayer.DepartmentId, data = JsonConvert.SerializeObject(mapLayer) });
				mapLayer.PgId = result;

				return mapLayer;
			}
		}

		public async Task<MapLayer> UpdateAsync(MapLayer mapLayer)
		{
			using (var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString))
			{
				await connection.OpenAsync();

				if (!string.IsNullOrWhiteSpace(mapLayer.PgId) && int.TryParse(mapLayer.PgId, out var pgId))
					await connection.ExecuteAsync("UPDATE public.maplayers SET data = CAST(@data AS jsonb) WHERE id = @pgId;",
						new { data = JsonConvert.SerializeObject(mapLayer), pgId });


				return mapLayer;
			}
		}
	}
}
