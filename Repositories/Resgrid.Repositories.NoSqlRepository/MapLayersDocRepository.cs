using Dapper;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using MongoDB.Bson;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Repositories.NoSqlRepository
{
    public class MapLayersDocRepository : IMapLayersDocRepository
    {
        private const string Projection = "jsonb_set(data, '{id}', to_jsonb(id::text))::text";

        // The Mongo geometry fields deliberately opt out of Newtonsoft serialization.
        // Persist the public GeoJSON shape alongside metadata so a PostgreSQL round trip
        // preserves the coordinates, rings, properties and geometry types.
        internal static string SerializeDocument(MapLayer layer)
        {
            var document = JObject.FromObject(layer);
            document["featureCollection"] = JToken.FromObject(layer.Data.Convert());
            return document.ToString(Formatting.None);
        }

        internal static MapLayer DeserializeDocument(string value)
        {
            var document = JObject.Parse(value);
            var features = document["featureCollection"]?.ToObject<FeatureCollection>();
            if (features == null)
            {
                var oldFeatures = document["data"]?["Features"] as JArray;
                if (oldFeatures?.Count > 0)
                    throw new InvalidOperationException("This legacy PostgreSQL map layer has no stored coordinates. Restore or recreate its geometry before use.");
                features = new FeatureCollection();
            }
            document.Remove("Id");
            document.Remove("CreatedAt");
            document.Remove("data");
            document.Remove("featureCollection");
            var layer = document.ToObject<MapLayer>();
            layer.Data = new MapLayerData();
            layer.Data.Hydrate(features);
            return layer;
        }

        public async Task<List<MapLayer>> GetAllMapLayersByDepartmentIdAsync(int departmentId, MapLayerTypes type)
        {
            using var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString);
            await connection.OpenAsync();
            var documents = await connection.QueryAsync<string>(
                $"SELECT {Projection} FROM public.maplayers WHERE departmentid = @departmentId;", new { departmentId });
            return DeserializeIncludedDocuments(documents, type, departmentId);
        }

        /// <summary>
        /// The department's layers of one type. A legacy layer whose geometry was lost is left out (and logged with its id)
        /// rather than failing the whole list, so the department's other layers still load.
        /// </summary>
        internal static List<MapLayer> DeserializeIncludedDocuments(IEnumerable<string> documents, MapLayerTypes type, int departmentId)
        {
            var layers = new List<MapLayer>();
            foreach (var value in documents.Where(value => IsIncludedDocument(value, type)))
            {
                try
                {
                    layers.Add(DeserializeDocument(value));
                }
                catch (InvalidOperationException ex)
                {
                    // The id and department find the row to repair; the document itself is not logged.
                    var layerId = JObject.Parse(value).Value<string>("id");
                    Resgrid.Framework.Logging.LogException(ex,
                        $"Skipping legacy PostgreSQL map layer {layerId} of department {departmentId}: it has no stored geometry, so the department's layer list leaves it out.");
                }
            }
            return layers;
        }

        internal static bool IsIncludedDocument(string value, MapLayerTypes type)
        {
            var document = JObject.Parse(value);
            return document.Value<int>("type") == (int)type && !document.Value<bool>("isDeleted");
        }

        public async Task<MapLayer> GetByIdAsync(string id)
        {
            using var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString);
            await connection.OpenAsync();
            var value = await connection.QueryFirstOrDefaultAsync<string>(
                $"SELECT {Projection} FROM public.maplayers WHERE oid = @id OR id::text = @id;", new { id });
            return value == null ? null : DeserializeDocument(value);
        }

        public async Task<MapLayer> GetByOldIdAsync(string id)
        {
            using var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString);
            await connection.OpenAsync();
            var value = await connection.QueryFirstOrDefaultAsync<string>(
                $"SELECT {Projection} FROM public.maplayers WHERE oid = @id;", new { id });
            return value == null ? null : DeserializeDocument(value);
        }

        public async Task<MapLayer> InsertAsync(MapLayer mapLayer)
        {
            using var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString);
            await connection.OpenAsync();
            mapLayer.PgId = await connection.ExecuteScalarAsync<string>(
                "INSERT INTO public.maplayers (departmentid, oid, data) VALUES (@departmentId, @oldId, CAST(@data AS jsonb)) RETURNING id::text;",
                new { departmentId = mapLayer.DepartmentId, oldId = mapLayer.Id == ObjectId.Empty ? null : mapLayer.Id.ToString(), data = SerializeDocument(mapLayer) });
            return mapLayer;
        }

        public async Task<MapLayer> UpdateAsync(MapLayer mapLayer)
        {
            if (!int.TryParse(mapLayer.PgId, out var pgId))
                throw new ArgumentException("A saved PostgreSQL map layer ID is required.", nameof(mapLayer));
            using var connection = new NpgsqlConnection(Config.DataConfig.DocumentConnectionString);
            await connection.OpenAsync();
            var affected = await connection.ExecuteAsync(
                "UPDATE public.maplayers SET data = CAST(@data AS jsonb) WHERE id = @pgId AND departmentid = @departmentId;",
                new { data = SerializeDocument(mapLayer), pgId, departmentId = mapLayer.DepartmentId });
            if (affected != 1)
                throw new InvalidOperationException("The expected department map layer was not updated.");
            return mapLayer;
        }
    }
}
