using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Offline map packs (maplibre-mapping-routing-navigation-plan.md §19): the department's response area and settings
	/// (setting 123), the manifest the apps sync, the department data bundle, and the pack builder the worker drives.
	/// </summary>
	public interface IOfflineMapPackService
	{
		/// <summary>Offline maps are switched on for the install and a pack store is configured.</summary>
		bool IsInstallEnabled { get; }

		/// <summary>The install allows it and the department has the Maps.Offline flag.</summary>
		Task<bool> IsAvailableForDepartmentAsync(int departmentId);

		Task<OfflineMapPacksConfig> GetConfigAsync(int departmentId, bool bypassCache = false);

		/// <summary>The base-map download size of a response area (GeoJSON), or of the fallback area when empty.</summary>
		Task<OfflineResponseAreaEstimate> EstimateResponseAreaAsync(int departmentId, string responseAreaGeoJson, CancellationToken cancellationToken = default);

		/// <summary>Saves setting 123; refuses a response area over the size limit (the estimate says why).</summary>
		Task<OfflineResponseAreaEstimate> SaveConfigAsync(int departmentId, OfflineMapPacksConfig config, CancellationToken cancellationToken = default);

		/// <summary>The manifest without URLs (packs queued as needed); the API signs URLs per item.</summary>
		Task<OfflineManifest> GetManifestAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<OfflineDepartmentData> GetDepartmentDataAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<OfflineMapPack> GetPackAsync(string offlineMapPackId);

		/// <summary>Builds queued packs (worker); returns how many were processed.</summary>
		Task<int> ProcessQueueAsync(int max, CancellationToken cancellationToken = default);

		/// <summary>Requeues packs whose builder died and deletes packs nobody requested for 60 days (worker).</summary>
		Task<int> CleanupAsync(CancellationToken cancellationToken = default);

		/// <summary>Drops the department's cached manifest and data so the next sync sees a change (custom maps, layers, POIs).</summary>
		void InvalidateDepartment(int departmentId);
	}
}
