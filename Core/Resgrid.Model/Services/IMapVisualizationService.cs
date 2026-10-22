using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Why a map visualization save was refused.</summary>
	public enum MapVisualizationSaveErrors
	{
		None = 0,
		NameRequired = 1,
		NotFound = 2,
		/// <summary>The member may not change it (someone else's, or shared and they are not a department admin) or share it.</summary>
		NotAllowed = 3,
		LimitReached = 4,
		DefinitionTooLarge = 5
	}

	public sealed class MapVisualizationSaveRequest
	{
		/// <summary>Null or empty creates a new visualization.</summary>
		public string MapVisualizationId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public bool IsShared { get; set; }

		public string TemplateKey { get; set; }

		public MapVisualizationDefinition Definition { get; set; }
	}

	public sealed class MapVisualizationSaveResult
	{
		public MapVisualization Saved { get; set; }

		public MapVisualizationSaveErrors Error { get; set; }
	}

	/// <summary>
	/// Map visualizations (Mapping → Visualizations): saved definitions, and running a definition into map layers. Every run
	/// reads fresh data under the viewer's permissions — dispatch scope for calls, the visibility matrix for units, members
	/// and their locations — so a shared definition never shows a viewer more than they could already see.
	/// </summary>
	public interface IMapVisualizationService
	{
		/// <summary>The department's shared visualizations plus the member's own, by name.</summary>
		Task<List<MapVisualization>> GetVisibleForUserAsync(int departmentId, string userId);

		/// <summary>The visualization when it belongs to the department and the member may see it (shared or theirs).</summary>
		Task<MapVisualization> GetVisibleByIdAsync(int departmentId, string userId, string mapVisualizationId);

		Task<MapVisualizationSaveResult> SaveAsync(int departmentId, string userId, bool isDepartmentAdmin, MapVisualizationSaveRequest request,
			CancellationToken cancellationToken = default);

		/// <summary>False when it does not exist or the member may not delete it.</summary>
		Task<bool> DeleteAsync(int departmentId, string userId, bool isDepartmentAdmin, string mapVisualizationId, CancellationToken cancellationToken = default);

		/// <summary>Reads and shapes the data for <paramref name="definition"/> as seen by <paramref name="userId"/>.</summary>
		Task<MapVisualizationResult> RunAsync(int departmentId, string userId, MapVisualizationDefinition definition, CancellationToken cancellationToken = default);

		/// <summary>Recent calls (last year) whose number, name, type or address contains <paramref name="term"/>, newest first, for the call picker.</summary>
		Task<List<Call>> SearchCallsAsync(int departmentId, string userId, string term, int max, CancellationToken cancellationToken = default);
	}
}
