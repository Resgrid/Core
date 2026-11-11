using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.GeoLocationProvider.Offline;
using Resgrid.Services.Offline;

namespace Resgrid.Services
{
	/// <summary>
	/// Offline map packs (maplibre-mapping-routing-navigation-plan.md §19). The response area (setting 123, or a fallback
	/// circle) is covered by z10 cells; each cell is a shared base-map pack cut from the configured PMTiles archive, plus
	/// one overview pack with z0–9 over the area. Department packs carry georeferenced custom maps as raster PMTiles and
	/// the sprite of uploaded icons; the department data bundle (layers, stations, POIs, custom map regions, indoor zones)
	/// is generated on request. The manifest lists everything without URLs: the API signs each one. Packs nobody asked for
	/// in 60 days are deleted.
	/// </summary>
	public class OfflineMapPackService : IOfflineMapPackService
	{
		private const int OverviewMaxZoom = OfflineResponseArea.CellZoom - 1;
		private const int MaxCells = 1500;
		private const int MaxAttempts = 3;
		private static readonly TimeSpan Retention = TimeSpan.FromDays(60);
		private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);
		private static readonly TimeSpan ManifestCacheLength = TimeSpan.FromMinutes(2);
		private static readonly TimeSpan DataCacheLength = TimeSpan.FromMinutes(10);
		private static readonly TimeSpan TouchInterval = TimeSpan.FromDays(1);

		private static readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions());
		private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

		private readonly IOfflineMapPacksRepository _packsRepository;
		private readonly IOfflineMapPackStore _store;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IGeoService _geoService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly ICustomMapService _customMapService;
		private readonly ICustomMapMercatorTileRenderer _mercatorTileRenderer;
		private readonly IMappingService _mappingService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;
		private readonly ITypeIconsService _typeIconsService;
		private readonly IRouteService _routeService;

		public OfflineMapPackService(IOfflineMapPacksRepository packsRepository, IOfflineMapPackStore store, IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService, IGeoService geoService, IFeatureToggleService featureToggleService,
			ICustomMapService customMapService, ICustomMapMercatorTileRenderer mercatorTileRenderer, IMappingService mappingService, IUnitsService unitsService,
			ICallsService callsService, ITypeIconsService typeIconsService, IRouteService routeService)
		{
			_routeService = routeService;
			_packsRepository = packsRepository;
			_store = store;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_geoService = geoService;
			_featureToggleService = featureToggleService;
			_customMapService = customMapService;
			_mercatorTileRenderer = mercatorTileRenderer;
			_mappingService = mappingService;
			_unitsService = unitsService;
			_callsService = callsService;
			_typeIconsService = typeIconsService;
		}

		public bool IsInstallEnabled => MappingConfig.OfflineMapsEnabled && _store.IsConfigured;

		private static bool HasBasemapArchive => !string.IsNullOrWhiteSpace(MappingConfig.OfflineBasemapArchiveUrl);

		private static string BasemapVersion => string.IsNullOrWhiteSpace(MappingConfig.OfflineBasemapVersion) ? "0" : MappingConfig.OfflineBasemapVersion.Trim();

		private static int BasemapMaxZoom => Math.Clamp(MappingConfig.OfflineBasemapMaxZoom, OfflineResponseArea.CellZoom, 16);

		private static long ResponseAreaMaxBytes => Math.Clamp(MappingConfig.ResponseAreaMaxBytes, 1024L * 1024, 1024L * 1024 * 1024);

		public async Task<bool> IsAvailableForDepartmentAsync(int departmentId)
		{
			return IsInstallEnabled && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.MapsOffline, departmentId);
		}

		public Task<OfflineMapPacksConfig> GetConfigAsync(int departmentId, bool bypassCache = false)
		{
			return _departmentSettingsService.GetOfflineMapPacksConfigAsync(departmentId, bypassCache);
		}

		#region Response area

		public async Task<OfflineResponseAreaEstimate> EstimateResponseAreaAsync(int departmentId, string responseAreaGeoJson, CancellationToken cancellationToken = default)
		{
			List<List<(double Lon, double Lat)>> rings;

			if (string.IsNullOrWhiteSpace(responseAreaGeoJson))
			{
				rings = await FallbackAreaAsync(departmentId);
				if (rings == null)
					return new OfflineResponseAreaEstimate { Error = "no-location", MaxBytes = ResponseAreaMaxBytes };
			}
			else
			{
				rings = OfflineResponseArea.Parse(responseAreaGeoJson, out var error);
				if (rings == null)
					return new OfflineResponseAreaEstimate { Error = error, MaxBytes = ResponseAreaMaxBytes };
			}

			return await MeasureAsync(rings, BasemapMaxZoom, cancellationToken);
		}

		private async Task<OfflineResponseAreaEstimate> MeasureAsync(List<List<(double Lon, double Lat)>> rings, int maxZoom, CancellationToken cancellationToken)
		{
			var estimate = new OfflineResponseAreaEstimate { MaxBytes = ResponseAreaMaxBytes, MaxZoom = maxZoom, AreaSquareKm = Math.Round(OfflineResponseArea.AreaSquareKm(rings), 1) };

			if (estimate.AreaSquareKm > OfflineResponseArea.MaxAreaSquareKm)
			{
				estimate.Error = "too-large-area";
				return estimate;
			}

			var cells = OfflineResponseArea.CoveringCells(rings);
			estimate.CellCount = cells.Count;
			if (cells.Count == 0 || cells.Count > MaxCells)
			{
				estimate.Error = cells.Count == 0 ? "invalid" : "too-large-area";
				return estimate;
			}

			estimate.Valid = true;

			if (!HasBasemapArchive)
			{
				// No archive: nothing to measure, and no base map will be downloaded.
				estimate.Fits = true;
				return estimate;
			}

			using var source = OpenArchive();
			var extractor = new PmtilesRegionExtractor(new PmtilesArchiveReader(source));

			long total = 0;
			foreach (var (x, y) in cells)
			{
				total += await extractor.EstimateBytesAsync(Pmtiles.CellRanges(OfflineResponseArea.CellZoom, (ulong)x, (ulong)y, OfflineResponseArea.CellZoom, maxZoom), cancellationToken);
				if (total > ResponseAreaMaxBytes * 4)
					break;
			}

			total += await extractor.EstimateBytesAsync(OverviewRanges(CellsBounds(cells)), cancellationToken);

			estimate.Measured = true;
			estimate.SizeBytes = total;
			estimate.Fits = total <= ResponseAreaMaxBytes;
			if (!estimate.Fits)
				estimate.Error = "too-large";

			return estimate;
		}

		public async Task<OfflineResponseAreaEstimate> SaveConfigAsync(int departmentId, OfflineMapPacksConfig config, CancellationToken cancellationToken = default)
		{
			config ??= new OfflineMapPacksConfig();
			OfflineResponseAreaEstimate estimate;

			if (string.IsNullOrWhiteSpace(config.ResponseAreaGeoJson))
			{
				estimate = new OfflineResponseAreaEstimate { Valid = true, Fits = true, MaxBytes = ResponseAreaMaxBytes, MaxZoom = BasemapMaxZoom };
				config.AcceptedMaxZoom = 0;
			}
			else
			{
				var stored = await _departmentSettingsService.GetOfflineMapPacksConfigAsync(departmentId, true);
				if (stored != null && stored.AcceptedMaxZoom > 0 &&
				    string.Equals(stored.ResponseAreaGeoJson?.Trim(), config.ResponseAreaGeoJson.Trim(), StringComparison.Ordinal))
				{
					// An unchanged area keeps the zoom it was accepted at: a new archive that grew it past the limit must not
					// block saving the other offline settings (§19.3).
					config.AcceptedMaxZoom = stored.AcceptedMaxZoom;
					estimate = new OfflineResponseAreaEstimate { Valid = true, Fits = true, MaxBytes = ResponseAreaMaxBytes, MaxZoom = Math.Min(stored.AcceptedMaxZoom, BasemapMaxZoom) };
				}
				else
				{
					estimate = await EstimateResponseAreaAsync(departmentId, config.ResponseAreaGeoJson, cancellationToken);
					if (!estimate.Valid || !estimate.Fits)
						return estimate;

					config.AcceptedMaxZoom = BasemapMaxZoom;
				}
			}

			await _departmentSettingsService.SetOfflineMapPacksConfigAsync(departmentId, config, cancellationToken);
			InvalidateDepartment(departmentId);
			return estimate;
		}

		/// <summary>A circle around the map centre (which falls back to the department address), else the first station.</summary>
		private async Task<List<List<(double Lon, double Lat)>>> FallbackAreaAsync(int departmentId)
		{
			var radius = Math.Clamp(MappingConfig.ResponseAreaFallbackRadiusKm, 1, 100);

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department != null)
			{
				var center = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);
				if (center?.Latitude != null && center.Longitude != null && !(center.Latitude == 0 && center.Longitude == 0))
					return new List<List<(double, double)>> { OfflineResponseArea.Circle(center.Latitude.Value, center.Longitude.Value, radius) };
			}

			foreach (var station in await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
			{
				var point = await _geoService.GetStationCoordinatesAsync(station);
				if (point.HasValue)
					return new List<List<(double, double)>> { OfflineResponseArea.Circle(point.Value.Latitude, point.Value.Longitude, radius) };
			}

			return null;
		}

		/// <summary>The department's area rings and the max zoom its base map downloads at.</summary>
		private async Task<(List<List<(double Lon, double Lat)>> Rings, int MaxZoom)> ResolveAreaAsync(int departmentId, OfflineMapPacksConfig config, CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(config.ResponseAreaGeoJson))
			{
				var rings = OfflineResponseArea.Parse(config.ResponseAreaGeoJson, out _);
				if (rings != null)
				{
					// A new archive can grow a saved area past the limit; it keeps the zoom it was accepted at (§19.3).
					var accepted = config.AcceptedMaxZoom > 0 ? Math.Min(config.AcceptedMaxZoom, BasemapMaxZoom) : BasemapMaxZoom;
					return (rings, accepted);
				}
			}

			var fallback = await FallbackAreaAsync(departmentId);
			if (fallback == null)
				return (null, BasemapMaxZoom);

			// A fallback circle over a dense metro steps the zoom down until it fits.
			var cacheKey = $"offline-fallback-zoom|{departmentId}|{BasemapVersion}";
			if (Cache.TryGetValue(cacheKey, out int known))
				return (fallback, known);

			var zoom = BasemapMaxZoom;
			if (HasBasemapArchive)
			{
				for (; zoom > 12; zoom--)
				{
					var estimate = await MeasureAsync(fallback, zoom, cancellationToken);
					if (estimate.Fits)
						break;
				}
			}

			Cache.Set(cacheKey, zoom, TimeSpan.FromHours(12));
			return (fallback, zoom);
		}

		private static (double West, double South, double East, double North) CellsBounds(IEnumerable<(int X, int Y)> cells)
		{
			var bounds = cells.Select(c => OfflineResponseArea.TileBounds(OfflineResponseArea.CellZoom, c.X, c.Y)).ToList();
			return (bounds.Min(b => b.West), bounds.Min(b => b.South), bounds.Max(b => b.East), bounds.Max(b => b.North));
		}

		/// <summary>The z0–9 tiles over a bounding box (the overview pack).</summary>
		private static List<TileIdRange> OverviewRanges((double West, double South, double East, double North) bounds)
		{
			var ids = new SortedSet<ulong>();
			for (var z = 0; z <= OverviewMaxZoom; z++)
			{
				var minX = OfflineResponseArea.TileX(bounds.West, z);
				var maxX = OfflineResponseArea.TileX(bounds.East, z);
				var minY = OfflineResponseArea.TileY(bounds.North, z);
				var maxY = OfflineResponseArea.TileY(bounds.South, z);

				for (var x = minX; x <= maxX; x++)
				for (var y = minY; y <= maxY; y++)
					ids.Add(Pmtiles.ZxyToTileId(z, (ulong)x, (ulong)y));
			}

			// Adjacent ids collapse into one range.
			var ranges = new List<TileIdRange>();
			foreach (var id in ids)
			{
				if (ranges.Count > 0 && ranges[^1].Last + 1 == id)
					ranges[^1] = new TileIdRange(ranges[^1].First, id);
				else
					ranges.Add(new TileIdRange(id, id));
			}

			return ranges;
		}

		#endregion Response area

		#region Manifest

		public async Task<OfflineManifest> GetManifestAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var cacheKey = "offline-manifest|" + departmentId;
			if (Cache.TryGetValue(cacheKey, out OfflineManifest cached))
				return cached;

			var config = await GetConfigAsync(departmentId);
			var manifest = new OfflineManifest
			{
				Attribution = "Protomaps © OpenStreetMap",
				Policy = new OfflineManifestPolicy
				{
					WifiOnly = config.DownloadPolicy == (int)OfflineDownloadPolicy.WifiOnly,
					ManualOnly = config.DownloadPolicy == (int)OfflineDownloadPolicy.Manual,
					RefreshHours = config.EffectiveRefreshHours(),
					StaleDays = config.EffectiveStaleDays(),
					PreferLocalInAo = config.PreferLocalInArea,
					AmbientCacheMaxBytes = MappingConfig.AmbientCacheMaxBytes,
					MaxPackBytes = MappingConfig.OfflinePackMaxBytes,
					ResponseAreaMaxBytes = ResponseAreaMaxBytes
				}
			};

			var wanted = new List<OfflineMapPack>();
			var outsideByKey = new Dictionary<string, OutsideAreaCell>(StringComparer.OrdinalIgnoreCase);

			if (HasBasemapArchive)
			{
				var (rings, maxZoom) = await ResolveAreaAsync(departmentId, config, cancellationToken);
				if (rings != null)
				{
					var cells = OfflineResponseArea.CoveringCells(rings).Take(MaxCells).ToList();
					if (cells.Count > 0)
					{
						var bounds = CellsBounds(cells);
						wanted.Add(new OfflineMapPack
						{
							Kind = (int)OfflineMapPackKind.Basemap,
							PackKey = $"basemap-overview:{Round(bounds.West)},{Round(bounds.South)},{Round(bounds.East)},{Round(bounds.North)}:{BasemapVersion}",
							Name = "Overview",
							Bounds = BoundsText(bounds),
							MinZoom = 0,
							MaxZoom = OverviewMaxZoom,
							Version = BasemapVersion
						});

						for (var i = 0; i < cells.Count; i++)
						{
							var (x, y) = cells[i];
							wanted.Add(CellPack(x, y, maxZoom, $"Response area {i + 1}/{cells.Count}"));
						}

						// Calls and routes that leave the area get its cells too (shared cells: a neighbour's area reuses them).
						var covered = new HashSet<(int, int)>(cells);
						foreach (var extra in await OutsideAreaCellsAsync(departmentId, config, covered))
						{
							var cellPack = CellPack(extra.X, extra.Y, BasemapMaxZoom, extra.Name, extra.Kind);
							wanted.Add(cellPack);
							outsideByKey[cellPack.PackKey] = extra;
						}
					}
				}
			}

			var maps = await _customMapService.GetCustomMapsForDepartmentAsync(departmentId) ?? new List<IndoorMap>();
			foreach (var map in maps.Where(m => !m.IsDeleted))
			{
				var indoor = map.MapType == (int)CustomMapType.Indoor;
				if (indoor ? config.ExcludeIndoorMaps : config.ExcludeCustomMaps)
					continue;

				foreach (var layer in (await _customMapService.GetLayersForMapAsync(map.IndoorMapId) ?? new List<IndoorMapFloor>()).Where(l => !l.IsDeleted))
				{
					var hasImage = layer.ImageData != null && layer.ImageData.Length > 0;
					var bounds = await _mercatorTileRenderer.GetBoundsAsync(layer);

					if (!indoor && bounds != null && (hasImage || layer.IsTiled))
					{
						wanted.Add(new OfflineMapPack
						{
							DepartmentId = departmentId,
							Kind = (int)OfflineMapPackKind.CustomMapRaster,
							PackKey = $"custommap:{layer.IndoorMapFloorId}:{LayerStamp(layer)}",
							Name = string.IsNullOrWhiteSpace(layer.Name) ? map.Name : $"{map.Name} – {layer.Name}",
							SourceId = layer.IndoorMapFloorId,
							Bounds = BoundsText(LayerBounds(layer, map)),
							Version = LayerStamp(layer)
						});
					}
					else if (hasImage)
					{
						var corners = LayerCorners(layer, map);
						manifest.IndoorFloors.Add(new OfflineManifestFloor
						{
							FloorId = layer.IndoorMapFloorId,
							Name = string.IsNullOrWhiteSpace(layer.Name) ? map.Name : $"{map.Name} – {layer.Name}",
							Corners = corners,
							Sha256 = Sha256(layer.ImageData),
							SizeBytes = layer.ImageData.Length,
							Version = Sha256(layer.ImageData).Substring(0, 12),
							IsCustomMapLayer = !indoor
						});
					}
				}
			}

			// The sprite of uploaded icons, keyed by its content so a new icon builds a new sprite.
			var icons = await IconKeysAsync(departmentId);
			if (icons.Count > 0)
			{
				wanted.Add(new OfflineMapPack
				{
					DepartmentId = departmentId,
					Kind = (int)OfflineMapPackKind.Sprite,
					PackKey = $"sprite:{departmentId}:{Sha256(Encoding.UTF8.GetBytes(string.Join("|", icons))).Substring(0, 16)}",
					Name = "Map icons",
					Version = Sha256(Encoding.UTF8.GetBytes(string.Join("|", icons))).Substring(0, 16)
				});
			}

			var packs = await EnsurePacksAsync(wanted, cancellationToken);
			var wantedByKey = wanted.GroupBy(w => w.PackKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			foreach (var pack in packs.Where(p => p.Kind != (int)OfflineMapPackKind.Sprite))
			{
				wantedByKey.TryGetValue(pack.PackKey, out var want);
				outsideByKey.TryGetValue(pack.PackKey, out var outside);
				manifest.Packs.Add(new OfflineManifestPack
				{
					PackId = pack.OfflineMapPackId,
					PackKey = pack.PackKey,
					Kind = want?.Kind ?? pack.Kind,
					Name = want?.Name ?? pack.Name,
					Bounds = ParseBounds(pack.Bounds),
					MinZoom = pack.MinZoom,
					MaxZoom = pack.MaxZoom,
					SizeBytes = pack.Status == (int)OfflineMapPackStatus.Ready ? pack.SizeBytes : (long?)null,
					Sha256 = pack.Status == (int)OfflineMapPackStatus.Ready ? pack.Sha256 : null,
					Version = pack.Version,
					Status = pack.Status,
					CustomMapLayerId = pack.Kind == (int)OfflineMapPackKind.CustomMapRaster ? pack.SourceId : null,
					CallIds = outside?.CallIds.ToList(),
					RoutePlanIds = outside?.RoutePlanIds.ToList()
				});
			}

			var sprite = packs.FirstOrDefault(p => p.Kind == (int)OfflineMapPackKind.Sprite && p.Status == (int)OfflineMapPackStatus.Ready);
			if (sprite != null && !string.IsNullOrWhiteSpace(sprite.FilesJson))
			{
				var files = JsonConvert.DeserializeObject<List<OfflineMapPackFile>>(sprite.FilesJson) ?? new List<OfflineMapPackFile>();
				manifest.Sprite = new OfflineManifestSprite
				{
					Version = sprite.Version,
					PackId = sprite.OfflineMapPackId,
					Files = files.Select(f => new OfflineManifestSpriteFile { Name = f.Name, Sha256 = f.Sha256, SizeBytes = f.SizeBytes, Version = sprite.Version, ResourceKey = f.Name }).ToList()
				};
			}

			var data = await GetDepartmentDataAsync(departmentId, cancellationToken);
			manifest.DepartmentData = new OfflineManifestFile { Version = data.Version, Sha256 = data.Sha256, SizeBytes = data.Content.Length };

			manifest.Styles = await StylesAsync(cancellationToken);

			manifest.ManifestVersion = "v1#" + Sha256(Encoding.UTF8.GetBytes(string.Join("|",
				// The call and plan ids are in it: a new call touching an already listed cell changes what a device keeps.
				manifest.Packs.Select(p => $"{p.PackKey}:{p.Status}:{p.Sha256}:{string.Join(",", p.CallIds ?? new List<int>())}:{string.Join(",", p.RoutePlanIds ?? new List<string>())}")
					.Concat(manifest.IndoorFloors.Select(f => f.FloorId + ":" + f.Sha256))
					.Append(manifest.Sprite?.Version ?? "-")
					.Append(data.Version)
					.Append(manifest.Styles?.Day?.Sha256 ?? "-")
					.Append(JsonConvert.SerializeObject(manifest.Policy))))).Substring(0, 16);

			// Building packs change status soon; a short cache keeps the poll cheap without hiding a finished build long.
			Cache.Set(cacheKey, manifest, packs.Any(p => p.Status != (int)OfflineMapPackStatus.Ready) ? TimeSpan.FromSeconds(30) : ManifestCacheLength);
			return manifest;
		}

		private static OfflineMapPack CellPack(int x, int y, int maxZoom, string name, OfflineMapPackKind kind = OfflineMapPackKind.Basemap)
		{
			return new OfflineMapPack
			{
				Kind = (int)kind,
				PackKey = $"basemap:z{OfflineResponseArea.CellZoom}/{x}/{y}:{maxZoom}:{BasemapVersion}",
				Name = name,
				Bounds = BoundsText(OfflineResponseArea.TileBounds(OfflineResponseArea.CellZoom, x, y)),
				MinZoom = OfflineResponseArea.CellZoom,
				MaxZoom = maxZoom,
				Version = BasemapVersion
			};
		}

		/// <summary>An incident or corridor cell: named and kinded by the first call or plan that touched it, listing them all.</summary>
		private sealed class OutsideAreaCell
		{
			public int X { get; set; }
			public int Y { get; set; }
			public string Name { get; set; }
			public OfflineMapPackKind Kind { get; set; }
			public SortedSet<int> CallIds { get; } = new SortedSet<int>();
			public SortedSet<string> RoutePlanIds { get; } = new SortedSet<string>(StringComparer.Ordinal);
		}

		/// <summary>
		/// Cells outside the response area that the department's active calls (radius) and active route plans (buffer
		/// along the line) touch: incident and corridor packs (§19.2). They are base-map cells, so the rows are shared;
		/// only the manifest entry carries the kind and the call and plan ids.
		/// </summary>
		private async Task<List<OutsideAreaCell>> OutsideAreaCellsAsync(int departmentId, OfflineMapPacksConfig config, HashSet<(int, int)> covered)
		{
			var extra = new List<OutsideAreaCell>();
			var byCell = new Dictionary<(int, int), OutsideAreaCell>();

			void AddRing(List<(double Lon, double Lat)> ring, string name, OfflineMapPackKind kind, int? callId, string routePlanId)
			{
				foreach (var cell in OfflineResponseArea.CoveringCells(new List<List<(double, double)>> { ring }))
				{
					if (covered.Contains(cell))
						continue;

					if (!byCell.TryGetValue(cell, out var entry))
					{
						// Past the cap no new cells, but a listed cell still gains the ids of every call or plan near it.
						if (extra.Count >= 200)
							continue;

						entry = new OutsideAreaCell { X = cell.X, Y = cell.Y, Name = name, Kind = kind };
						byCell.Add(cell, entry);
						extra.Add(entry);
					}

					if (callId.HasValue)
						entry.CallIds.Add(callId.Value);
					if (!string.IsNullOrWhiteSpace(routePlanId))
						entry.RoutePlanIds.Add(routePlanId);
				}
			}

			try
			{
				if (!config.IncidentPacksOff)
				{
					foreach (var call in await _callsService.GetActiveCallsByDepartmentAsync(departmentId) ?? new List<Call>())
					{
						var point = GeoMath.ParseLatLonString(call.GeoLocationData);
						if (point.HasValue)
							AddRing(OfflineResponseArea.Circle(point.Value.Latitude, point.Value.Longitude, config.EffectiveIncidentRadiusKm(), 16),
								$"Call {call.Number ?? call.CallId.ToString(CultureInfo.InvariantCulture)}", OfflineMapPackKind.IncidentDetail, call.CallId, null);
					}
				}

				if (!config.CorridorPacksOff)
				{
					var bufferKm = config.EffectiveCorridorBufferMeters() / 1000.0;
					foreach (var plan in (await _routeService.GetRoutePlansForDepartmentAsync(departmentId)).Where(p => p.RouteStatus == (int)RouteStatus.Active && !string.IsNullOrWhiteSpace(p.MapboxRouteGeometry)))
					{
						foreach (var point in SamplePoints(plan.MapboxRouteGeometry, 2.0))
							AddRing(OfflineResponseArea.Circle(point.Lat, point.Lon, bufferKm, 8), $"Route {plan.Name}", OfflineMapPackKind.RouteCorridor, null, plan.RoutePlanId);
					}
				}
			}
			catch (Exception ex)
			{
				// Extra coverage is a bonus; the response area is listed regardless.
				Logging.LogException(ex, $"Listing incident and corridor cells for department {departmentId}");
			}

			return extra;
		}

		/// <summary>Points along a GeoJSON LineString at most <paramref name="stepKm"/> apart.</summary>
		private static IEnumerable<(double Lon, double Lat)> SamplePoints(string lineGeoJson, double stepKm)
		{
			JArray coordinates;
			try
			{
				coordinates = JObject.Parse(lineGeoJson)["coordinates"] as JArray;
			}
			catch (Exception)
			{
				yield break;
			}

			if (coordinates == null)
				yield break;

			(double Lon, double Lat)? last = null;
			foreach (var position in coordinates.OfType<JArray>().Where(p => p.Count >= 2))
			{
				var current = ((double)position[0], (double)position[1]);
				if (last.HasValue)
				{
					var km = GeoMath.HaversineMeters(last.Value.Lat, last.Value.Lon, current.Item2, current.Item1) / 1000.0;
					var steps = (int)Math.Ceiling(km / stepKm);
					for (var s = 1; s < steps; s++)
					{
						var t = (double)s / steps;
						yield return (last.Value.Lon + (current.Item1 - last.Value.Lon) * t, last.Value.Lat + (current.Item2 - last.Value.Lat) * t);
					}
				}

				yield return current;
				last = current;
			}
		}

		/// <summary>Existing rows for the wanted keys, new Queued rows for the rest; retention clocks touched once a day.</summary>
		private async Task<List<OfflineMapPack>> EnsurePacksAsync(List<OfflineMapPack> wanted, CancellationToken cancellationToken)
		{
			var existing = (await _packsRepository.GetByPackKeysAsync(wanted.Select(w => w.PackKey).ToList()))
				.GroupBy(p => p.PackKey, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var result = new List<OfflineMapPack>();
			var touch = new List<string>();

			foreach (var want in wanted)
			{
				if (existing.TryGetValue(want.PackKey, out var row))
				{
					// A failed pack is retried later; the manifest lists it as building until it gives up.
					if (row.Status == (int)OfflineMapPackStatus.Failed && row.Attempts < MaxAttempts)
					{
						row.Status = (int)OfflineMapPackStatus.Queued;
						await _packsRepository.SaveOrUpdateAsync(row, CancellationToken.None);
					}

					if (!row.LastRequestedOn.HasValue || DateTime.UtcNow - row.LastRequestedOn.Value > TouchInterval)
						touch.Add(row.OfflineMapPackId);

					result.Add(row);
					continue;
				}

				var created = new OfflineMapPack
				{
					DepartmentId = want.DepartmentId,
					Kind = want.PackKey.StartsWith("basemap", StringComparison.Ordinal) ? (int)OfflineMapPackKind.Basemap : want.Kind,
					PackKey = want.PackKey,
					Name = want.PackKey.StartsWith("basemap:", StringComparison.Ordinal) ? "Base map cell" : want.Name,
					SourceId = want.SourceId,
					Version = want.Version,
					Bounds = want.Bounds,
					MinZoom = want.MinZoom,
					MaxZoom = want.MaxZoom,
					Status = (int)OfflineMapPackStatus.Queued,
					AddedOn = DateTime.UtcNow,
					LastRequestedOn = DateTime.UtcNow
				};

				try
				{
					result.Add(await _packsRepository.SaveOrUpdateAsync(created, CancellationToken.None));
				}
				catch (Exception)
				{
					// Another request inserted the same key first (unique index); use its row.
					var raced = await _packsRepository.GetByPackKeyAsync(want.PackKey);
					if (raced != null)
						result.Add(raced);
				}
			}

			if (touch.Count > 0)
				await _packsRepository.TouchAsync(touch, CancellationToken.None);

			return result;
		}

		private async Task<OfflineManifestStyles> StylesAsync(CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(MappingConfig.OfflineStyleUrlDay))
				return null;

			var index = await ReadStyleIndexAsync(cancellationToken);
			if (index == null)
				return null;

			OfflineManifestFile Listed(string name) =>
				index.TryGetValue(name, out var entry) ? new OfflineManifestFile { Version = MappingConfig.OfflineStyleVersion, Sha256 = entry.Sha256, SizeBytes = entry.SizeBytes, ResourceKey = name } : null;

			return new OfflineManifestStyles { Day = Listed("day.json"), Night = Listed("night.json"), Glyphs = Listed("glyphs.zip") };
		}

		private static string StyleKey(string name) => $"styles/{SafeSegment(MappingConfig.OfflineStyleVersion)}/{name}";

		private async Task<Dictionary<string, OfflineMapPackFile>> ReadStyleIndexAsync(CancellationToken cancellationToken)
		{
			var cacheKey = "offline-style-index|" + MappingConfig.OfflineStyleVersion;
			if (Cache.TryGetValue(cacheKey, out Dictionary<string, OfflineMapPackFile> cached))
				return cached;

			try
			{
				if (await _store.GetSizeAsync(StyleKey("index.json"), cancellationToken) == null)
					return null;

				await using var stream = await _store.OpenReadAsync(StyleKey("index.json"), 0, null, cancellationToken);
				using var reader = new StreamReader(stream);
				var files = JsonConvert.DeserializeObject<List<OfflineMapPackFile>>(await reader.ReadToEndAsync(cancellationToken)) ?? new List<OfflineMapPackFile>();
				var index = files.ToDictionary(f => f.Name, StringComparer.Ordinal);
				Cache.Set(cacheKey, index, TimeSpan.FromMinutes(10));
				return index;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Reading the offline style index");
				return null;
			}
		}

		/// <summary>The store key of a style file the manifest lists (day.json, night.json, glyphs.zip); null for any other name.</summary>
		public static string StyleStoreKey(string name) =>
			name == "day.json" || name == "night.json" || name == "glyphs.zip" ? StyleKey(name) : null;

		#endregion Manifest

		#region Department data

		public async Task<OfflineDepartmentData> GetDepartmentDataAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var cacheKey = "offline-data|" + departmentId;
			if (Cache.TryGetValue(cacheKey, out OfflineDepartmentData cached))
				return cached;

			var config = await GetConfigAsync(departmentId);
			var sections = new JObject();

			if (!config.ExcludeLayers)
				sections["layers"] = await LayersAsync(departmentId);

			sections["stations"] = await StationsAsync(departmentId);

			if (!config.ExcludePois)
				sections["pois"] = await PoisAsync(departmentId);

			var (regions, zones) = await RegionsAsync(departmentId, config);
			if (!config.ExcludeCustomMaps)
				sections["customMapRegions"] = regions;
			if (!config.ExcludeIndoorMaps)
				sections["indoorZones"] = zones;

			var json = Encoding.UTF8.GetBytes(sections.ToString(Formatting.None));
			var version = Sha256(json).Substring(0, 16);

			using var output = new MemoryStream();
			using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
				gzip.Write(json, 0, json.Length);

			var data = new OfflineDepartmentData { Content = output.ToArray(), Version = version };
			data.Sha256 = Sha256(data.Content);

			Cache.Set(cacheKey, data, DataCacheLength);
			return data;
		}

		private async Task<JObject> LayersAsync(int departmentId)
		{
			var features = new JArray();

			foreach (MapLayerTypes type in Enum.GetValues(typeof(MapLayerTypes)))
			{
				foreach (var layer in (await _mappingService.GetMapLayersForTypeDepartmentAsync(departmentId, type) ?? new List<MapLayer>()).Where(l => !l.IsDeleted))
				{
					var collection = layer.Data?.Convert();
					if (collection == null)
						continue;

					foreach (var feature in JObject.FromObject(collection, JsonSerializer.Create(new JsonSerializerSettings()))["features"] as JArray ?? new JArray())
					{
						var properties = feature["properties"] as JObject ?? new JObject();
						properties["layerId"] = layer.GetId();
						properties["layerName"] = layer.Name;
						properties["layerColor"] = layer.Color;
						properties["onByDefault"] = layer.IsOnByDefault;
						feature["properties"] = properties;
						features.Add(feature);
					}
				}
			}

			return FeatureCollection(features);
		}

		private async Task<JObject> StationsAsync(int departmentId)
		{
			var features = new JArray();

			foreach (var station in await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
			{
				var point = await _geoService.GetStationCoordinatesAsync(station);
				if (point.HasValue)
				{
					features.Add(Feature(new JObject { ["type"] = "Point", ["coordinates"] = new JArray(point.Value.Longitude, point.Value.Latitude) },
						new JObject { ["groupId"] = station.DepartmentGroupId, ["name"] = station.Name, ["kind"] = "station" }));
				}

				var fence = GeoMath.ParseGeofence(station.Geofence);
				if (fence != null && fence.Count >= 3)
				{
					var ring = new JArray(fence.Select(p => new JArray(p.Longitude, p.Latitude)));
					ring.Add(new JArray(fence[0].Longitude, fence[0].Latitude));
					features.Add(Feature(new JObject { ["type"] = "Polygon", ["coordinates"] = new JArray(ring) },
						new JObject { ["groupId"] = station.DepartmentGroupId, ["name"] = station.Name, ["color"] = station.GeofenceColor, ["kind"] = "boundary" }));
				}
			}

			return FeatureCollection(features);
		}

		private async Task<JObject> PoisAsync(int departmentId)
		{
			var types = (await _mappingService.GetPOITypesForDepartmentAsync(departmentId) ?? new List<PoiType>()).ToDictionary(t => t.PoiTypeId);
			var features = new JArray();

			foreach (var poi in await _mappingService.GetPOIsForDepartmentAsync(departmentId) ?? new List<Poi>())
			{
				if (poi.Latitude == 0 && poi.Longitude == 0)
					continue;

				types.TryGetValue(poi.PoiTypeId, out var type);
				features.Add(Feature(new JObject { ["type"] = "Point", ["coordinates"] = new JArray(poi.Longitude, poi.Latitude) }, new JObject
				{
					["poiId"] = poi.PoiId,
					["name"] = poi.Name,
					["address"] = poi.Address,
					["note"] = poi.Note,
					["poiTypeId"] = poi.PoiTypeId,
					["typeName"] = type?.Name,
					["color"] = type?.Color,
					["marker"] = type?.Marker,
					["iconKey"] = type?.IconKey,
					["isDestination"] = type?.IsDestination ?? false
				}));
			}

			return FeatureCollection(features);
		}

		private async Task<(JObject Regions, JObject Zones)> RegionsAsync(int departmentId, OfflineMapPacksConfig config)
		{
			var regions = new JArray();
			var zones = new JArray();

			foreach (var map in (await _customMapService.GetCustomMapsForDepartmentAsync(departmentId) ?? new List<IndoorMap>()).Where(m => !m.IsDeleted))
			{
				var indoor = map.MapType == (int)CustomMapType.Indoor;
				if (indoor ? config.ExcludeIndoorMaps : config.ExcludeCustomMaps)
					continue;

				foreach (var layer in (await _customMapService.GetLayersForMapAsync(map.IndoorMapId) ?? new List<IndoorMapFloor>()).Where(l => !l.IsDeleted))
				{
					foreach (var zone in (await _customMapService.GetRegionsForLayerAsync(layer.IndoorMapFloorId) ?? new List<IndoorMapZone>()).Where(z => !z.IsDeleted && !string.IsNullOrWhiteSpace(z.GeoGeometry)))
					{
						JToken geometry;
						try
						{
							geometry = JToken.Parse(zone.GeoGeometry);
						}
						catch (Exception)
						{
							continue;
						}

						(indoor ? zones : regions).Add(Feature(geometry, new JObject
						{
							["id"] = zone.IndoorMapZoneId,
							["mapId"] = map.IndoorMapId,
							["layerId"] = layer.IndoorMapFloorId,
							["name"] = zone.Name,
							["description"] = zone.Description,
							["zoneType"] = zone.ZoneType,
							["color"] = zone.Color,
							["isSearchable"] = zone.IsSearchable,
							["isDispatchable"] = zone.IsDispatchable
						}));
					}
				}
			}

			return (FeatureCollection(regions), FeatureCollection(zones));
		}

		private static JObject Feature(JToken geometry, JObject properties) =>
			new JObject { ["type"] = "Feature", ["geometry"] = geometry, ["properties"] = properties };

		private static JObject FeatureCollection(JArray features) =>
			new JObject { ["type"] = "FeatureCollection", ["features"] = features };

		public void InvalidateDepartment(int departmentId)
		{
			Cache.Remove("offline-manifest|" + departmentId);
			Cache.Remove("offline-data|" + departmentId);
		}

		#endregion Department data

		#region Builder

		public Task<OfflineMapPack> GetPackAsync(string offlineMapPackId)
		{
			return _packsRepository.GetByIdAsync(offlineMapPackId);
		}

		public async Task<int> ProcessQueueAsync(int max, CancellationToken cancellationToken = default)
		{
			if (!IsInstallEnabled)
				return 0;

			await MirrorStylesAsync(cancellationToken);

			var processed = 0;
			foreach (var pack in await _packsRepository.GetQueuedAsync(max))
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				if (!await _packsRepository.TryClaimAsync(pack.OfflineMapPackId, cancellationToken))
					continue;

				pack.Attempts += 1;
				var workPath = Path.Combine(Path.GetTempPath(), "rg-offline-" + Guid.NewGuid().ToString("N") + ".pmtiles");

				try
				{
					switch ((OfflineMapPackKind)pack.Kind)
					{
						case OfflineMapPackKind.Basemap:
							await BuildBasemapAsync(pack, workPath, cancellationToken);
							break;
						case OfflineMapPackKind.CustomMapRaster:
							await BuildCustomMapAsync(pack, workPath, cancellationToken);
							break;
						case OfflineMapPackKind.Sprite:
							await BuildSpriteAsync(pack, cancellationToken);
							break;
						default:
							throw new NotSupportedException($"Offline pack kind {pack.Kind} is not built by the worker");
					}

					pack.Status = (int)OfflineMapPackStatus.Ready;
					pack.BuiltOn = DateTime.UtcNow;
					pack.Error = null;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
				{
					Logging.LogException(ex, $"Building offline map pack {pack.PackKey}");
					pack.Status = (int)OfflineMapPackStatus.Failed;
					pack.Error = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message;
				}
				finally
				{
					TryDelete(workPath);
				}

				await _packsRepository.SaveOrUpdateAsync(pack, CancellationToken.None);
				if (pack.DepartmentId.HasValue)
					InvalidateDepartment(pack.DepartmentId.Value);

				processed++;
			}

			return processed;
		}

		private async Task BuildBasemapAsync(OfflineMapPack pack, string workPath, CancellationToken cancellationToken)
		{
			if (!HasBasemapArchive)
				throw new InvalidOperationException("No base map archive is configured (MappingConfig.OfflineBasemapArchiveUrl)");

			var bounds = ParseBounds(pack.Bounds) ?? throw new InvalidDataException("Base map pack has no bounds");
			var box = (bounds[0], bounds[1], bounds[2], bounds[3]);

			List<TileIdRange> ranges;
			if (pack.PackKey.StartsWith("basemap-overview:", StringComparison.Ordinal))
			{
				ranges = OverviewRanges(box);
			}
			else
			{
				var cell = pack.PackKey.Split(':')[1].Substring(1).Split('/');
				ranges = Pmtiles.CellRanges(int.Parse(cell[0], CultureInfo.InvariantCulture), ulong.Parse(cell[1], CultureInfo.InvariantCulture),
					ulong.Parse(cell[2], CultureInfo.InvariantCulture), pack.MinZoom, pack.MaxZoom);
			}

			using var source = OpenArchive();
			var extractor = new PmtilesRegionExtractor(new PmtilesArchiveReader(source));

			// Over the cap: drop the deepest zoom until it fits.
			while (await extractor.EstimateBytesAsync(ranges, cancellationToken) > MappingConfig.OfflinePackMaxBytes && ranges.Count > 1)
			{
				ranges.RemoveAt(ranges.Count - 1);
				pack.MaxZoom = Math.Max(pack.MinZoom, pack.MaxZoom - 1);
			}

			await using (var output = new FileStream(workPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				await extractor.ExtractAsync(output, ranges, bounds[0], bounds[1], bounds[2], bounds[3], pack.MinZoom, pack.MaxZoom, cancellationToken);

			await StoreFileAsync(pack, workPath, $"basemap/{SafeSegment(pack.Version)}/{SafeSegment(pack.PackKey)}.pmtiles", cancellationToken);
		}

		private async Task BuildCustomMapAsync(OfflineMapPack pack, string workPath, CancellationToken cancellationToken)
		{
			var layer = await _customMapService.GetLayerByIdAsync(pack.SourceId) ?? throw new InvalidOperationException("Custom map layer not found");
			var bounds = await _mercatorTileRenderer.GetBoundsAsync(layer) ?? throw new InvalidOperationException("Custom map layer has no bounds");

			// Native zoom: where one tile pixel matches one image pixel; four levels above it for context.
			var imageWidth = layer.ImageData != null && layer.ImageData.Length > 0
				? SixLabors.ImageSharp.Image.Identify(layer.ImageData)?.Width ?? 1024
				: (1 << Math.Clamp(layer.TileMaxZoom ?? 2, 0, 6)) * 256;
			var spanX = Math.Max(1e-9, bounds.East - bounds.West);
			var nativeZoom = Math.Clamp((int)Math.Ceiling(Math.Log2(imageWidth / (spanX * 256))), 1, 22);
			var minZoom = Math.Max(0, nativeZoom - 4);

			var tiles = new List<PmtilesWriterTile>();
			var bodies = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			long total = 0;

			for (var z = minZoom; z <= nativeZoom && total <= MappingConfig.OfflinePackMaxBytes; z++)
			{
				var n = 1 << z;
				var minX = Math.Clamp((int)Math.Floor(bounds.West * n), 0, n - 1);
				var maxX = Math.Clamp((int)Math.Floor(bounds.East * n), 0, n - 1);
				var minY = Math.Clamp((int)Math.Floor(bounds.North * n), 0, n - 1);
				var maxY = Math.Clamp((int)Math.Floor(bounds.South * n), 0, n - 1);

				for (var x = minX; x <= maxX; x++)
				for (var y = minY; y <= maxY; y++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var png = await _mercatorTileRenderer.RenderTileAsync(layer, z, x, y);
					if (png == null)
						continue;

					var key = $"{z}/{x}/{y}";
					bodies[key] = png;
					total += png.Length;
					tiles.Add(new PmtilesWriterTile { TileId = Pmtiles.ZxyToTileId(z, (ulong)x, (ulong)y), DataKey = key });
				}

				pack.MaxZoom = z;
			}

			pack.MinZoom = minZoom;
			var box = LayerBounds(layer, null);
			var metadata = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { name = pack.Name, format = "png", attribution = "" }));

			await using (var output = new FileStream(workPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				await PmtilesWriter.WriteAsync(output, tiles, key => Task.FromResult(bodies[key]), metadata, 2, Pmtiles.CompressionNone,
					box.West, box.South, box.East, box.North, pack.MinZoom, pack.MaxZoom, cancellationToken);

			await StoreFileAsync(pack, workPath, $"custommap/{pack.DepartmentId}/{SafeSegment(pack.PackKey)}.pmtiles", cancellationToken);
		}

		private async Task BuildSpriteAsync(OfflineMapPack pack, CancellationToken cancellationToken)
		{
			var icons = new List<(string, byte[])>();
			foreach (var key in await IconKeysAsync(pack.DepartmentId ?? 0))
			{
				var icon = await _typeIconsService.GetIconByKeyAsync(key);
				if (icon?.Data != null && icon.DepartmentId == pack.DepartmentId)
					icons.Add((key, icon.Data));
			}

			var files = DepartmentSpriteBuilder.Build(icons);
			var prefix = $"sprite/{pack.DepartmentId}/{SafeSegment(pack.Version)}";
			var listed = files.Select(f => new OfflineMapPackFile { Name = f.Key, Sha256 = Sha256(f.Value), SizeBytes = f.Value.Length }).ToList();

			await RecordStorageHandleAsync(pack, prefix, JsonConvert.SerializeObject(listed));
			foreach (var (name, bytes) in files)
				await _store.PutAsync(prefix + "/" + name, bytes, name.EndsWith(".png") ? "image/png" : "application/json", cancellationToken);

			pack.SizeBytes = listed.Sum(f => f.SizeBytes);
			pack.Sha256 = Sha256(Encoding.UTF8.GetBytes(string.Join("|", listed.Select(f => f.Sha256))));
		}

		/// <summary>The uploaded icons the department's map data names: unit, call and POI type icons and their status/priority variants.</summary>
		private async Task<List<string>> IconKeysAsync(int departmentId)
		{
			var keys = new SortedSet<string>(StringComparer.Ordinal);

			foreach (var type in await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>())
				if (!string.IsNullOrWhiteSpace(type.IconKey)) keys.Add(type.IconKey);

			foreach (var type in await _callsService.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>())
				if (!string.IsNullOrWhiteSpace(type.IconKey)) keys.Add(type.IconKey);

			foreach (var type in await _mappingService.GetPOITypesForDepartmentAsync(departmentId) ?? new List<PoiType>())
				if (!string.IsNullOrWhiteSpace(type.IconKey)) keys.Add(type.IconKey);

			foreach (var variant in await _typeIconsService.GetVariantsForDepartmentAsync(departmentId) ?? new List<TypeIconVariant>())
				if (!string.IsNullOrWhiteSpace(variant.IconKey)) keys.Add(variant.IconKey);

			return keys.ToList();
		}

		private async Task StoreFileAsync(OfflineMapPack pack, string workPath, string key, CancellationToken cancellationToken)
		{
			var info = new FileInfo(workPath);
			if (info.Length > MappingConfig.OfflinePackMaxBytes)
				throw new InvalidOperationException($"Pack is {info.Length} bytes, over the {MappingConfig.OfflinePackMaxBytes} byte limit");

			string sha;
			await using (var stream = new FileStream(workPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
			using (var hash = SHA256.Create())
				sha = Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();

			await RecordStorageHandleAsync(pack, key, pack.FilesJson);
			await _store.PutFileAsync(key, workPath, "application/vnd.pmtiles", cancellationToken);

			pack.SizeBytes = info.Length;
			pack.Sha256 = sha;
		}

		/// <summary>
		/// Records where the pack's files go before any is uploaded. The row is the only handle to them: set afterwards, a build
		/// that failed or was stopped part-way left retention a row with no path, and it deleted the row and orphaned the files.
		/// The pack object carries it too, so the builder's final save (Ready or Failed) keeps it. Not cancelled with the build.
		/// </summary>
		private async Task RecordStorageHandleAsync(OfflineMapPack pack, string storagePath, string filesJson)
		{
			pack.StoragePath = storagePath;
			pack.FilesJson = filesJson;
			await _packsRepository.SetStorageHandleAsync(pack.OfflineMapPackId, storagePath, filesJson, CancellationToken.None);
		}

		/// <summary>Copies the configured offline styles and glyphs into the store once per OfflineStyleVersion.</summary>
		private async Task MirrorStylesAsync(CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(MappingConfig.OfflineStyleUrlDay))
				return;

			var doneKey = "offline-style-mirrored|" + MappingConfig.OfflineStyleVersion;
			if (Cache.TryGetValue(doneKey, out bool _))
				return;

			try
			{
				if (await _store.GetSizeAsync(StyleKey("index.json"), cancellationToken) == null)
				{
					var files = new List<OfflineMapPackFile>();
					foreach (var (name, source) in new[] { ("day.json", MappingConfig.OfflineStyleUrlDay), ("night.json", MappingConfig.OfflineStyleUrlNight), ("glyphs.zip", MappingConfig.OfflineGlyphsArchiveUrl) })
					{
						if (string.IsNullOrWhiteSpace(source))
							continue;

						var bytes = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
							? await Http.GetByteArrayAsync(source, cancellationToken)
							: await System.IO.File.ReadAllBytesAsync(source, cancellationToken);

						await _store.PutAsync(StyleKey(name), bytes, name.EndsWith(".zip") ? "application/zip" : "application/json", cancellationToken);
						files.Add(new OfflineMapPackFile { Name = name, Sha256 = Sha256(bytes), SizeBytes = bytes.Length });
					}

					await _store.PutAsync(StyleKey("index.json"), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(files)), "application/json", cancellationToken);
				}

				Cache.Set(doneKey, true, TimeSpan.FromHours(6));
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Logging.LogException(ex, "Mirroring the offline map styles");
			}
		}

		public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
		{
			// Retention runs whenever the store is configured, offline installs switched off included: department deletion keeps the
			// pack rows (the only handle to their files) and leaves them to this sweep, which must not stop when installs do.
			if (!_store.IsConfigured)
				return 0;

			var changed = 0;

			// Stuck builds are recovered with installs switched off too: retention skips Building rows, so a build the worker
			// stopped (a shutdown mid-build) would otherwise keep its row and files for good. Requeued, it is built again once
			// installs are back on, or ages out here like any other unrequested pack.
			foreach (var stuck in await _packsRepository.GetStuckBuildingAsync(DateTime.UtcNow - StuckAfter))
			{
				stuck.Status = stuck.Attempts >= MaxAttempts ? (int)OfflineMapPackStatus.Failed : (int)OfflineMapPackStatus.Queued;
				stuck.Error = "The builder stopped";
				await _packsRepository.SaveOrUpdateAsync(stuck, CancellationToken.None);
				changed++;
			}

			foreach (var old in await _packsRepository.GetNotRequestedSinceAsync(DateTime.UtcNow - Retention, 200))
			{
				try
				{
					if (old.Kind == (int)OfflineMapPackKind.Sprite && !string.IsNullOrWhiteSpace(old.FilesJson))
					{
						foreach (var file in JsonConvert.DeserializeObject<List<OfflineMapPackFile>>(old.FilesJson) ?? new List<OfflineMapPackFile>())
							await _store.DeleteAsync(old.StoragePath + "/" + file.Name, cancellationToken);
					}
					else if (!string.IsNullOrWhiteSpace(old.StoragePath))
					{
						await _store.DeleteAsync(old.StoragePath, cancellationToken);
					}

					await _packsRepository.DeleteAsync(old, CancellationToken.None);
					changed++;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
				{
					Logging.LogException(ex, $"Deleting offline map pack {old.PackKey}");
				}
			}

			return changed;
		}

		#endregion Builder

		#region Helpers

		private static IPmtilesSource OpenArchive()
		{
			var url = MappingConfig.OfflineBasemapArchiveUrl.Trim();
			return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
				? new HttpPmtilesSource(url)
				: new FilePmtilesSource(url);
		}

		private static string LayerStamp(IndoorMapFloor layer) =>
			$"{layer.AddedOn.Ticks:x}-{layer.SourceFileSize ?? 0}-{layer.ImageData?.Length ?? 0}-{layer.TileMaxZoom ?? 0}-{Round((double)(layer.BoundsNELat ?? 0))}";

		private static (double West, double South, double East, double North) LayerBounds(IndoorMapFloor layer, IndoorMap map)
		{
			if (layer.BoundsNELat.HasValue && layer.BoundsNELon.HasValue && layer.BoundsSWLat.HasValue && layer.BoundsSWLon.HasValue)
				return ((double)layer.BoundsSWLon.Value, (double)layer.BoundsSWLat.Value, (double)layer.BoundsNELon.Value, (double)layer.BoundsNELat.Value);

			return map == null ? (0, 0, 0, 0) : ((double)map.BoundsSWLon, (double)map.BoundsSWLat, (double)map.BoundsNELon, (double)map.BoundsNELat);
		}

		/// <summary>MapLibre image source corners (top-left, top-right, bottom-right, bottom-left); null without a position.</summary>
		private static double[][] LayerCorners(IndoorMapFloor layer, IndoorMap map)
		{
			var (west, south, east, north) = LayerBounds(layer, map);
			if (west == 0 && south == 0 && east == 0 && north == 0 || east <= west || north <= south)
				return null;

			return new[] { new[] { west, north }, new[] { east, north }, new[] { east, south }, new[] { west, south } };
		}

		private static string BoundsText((double West, double South, double East, double North) bounds) =>
			string.Join(",", new[] { bounds.West, bounds.South, bounds.East, bounds.North }.Select(v => Math.Round(v, 6).ToString(CultureInfo.InvariantCulture)));

		private static double[] ParseBounds(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var parts = text.Split(',');
			if (parts.Length != 4)
				return null;

			var values = new double[4];
			for (var i = 0; i < 4; i++)
			{
				if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
					return null;
			}

			return values;
		}

		private static string Round(double value) => Math.Round(value, 2).ToString("0.00", CultureInfo.InvariantCulture);

		private static string SafeSegment(string value) =>
			new string((value ?? "0").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_').ToArray());

		private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes ?? Array.Empty<byte>())).ToLowerInvariant();

		private static void TryDelete(string path)
		{
			try
			{
				if (System.IO.File.Exists(path))
					System.IO.File.Delete(path);
			}
			catch (IOException)
			{
				// Left for the OS temp cleaner.
			}
		}

		#endregion Helpers
	}
}
