using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Saved map visualizations (MapVisualizations, M0288) and the thin, capped reads the visualizer runs on.</summary>
	public interface IMapVisualizationsRepository : IRepository<MapVisualization>
	{
		Task<List<MapVisualization>> GetForDepartmentAsync(int departmentId);

		Task<MapVisualization> GetByIdForDepartmentAsync(string mapVisualizationId, int departmentId);

		/// <summary>
		/// The department's non-deleted calls logged in [startUtc, endUtc), newest first, at most <paramref name="maxRows"/>.
		/// Only the columns a map needs are read (id, number, name, type, priority, state, address, location, reporter and
		/// times), so a multi-year window does not drag every call's notes along. Type, priority and state filters run in
		/// SQL so the row cap applies after them; empty <paramref name="states"/> means every state except Pending.
		/// </summary>
		Task<List<Call>> GetCallsForMapAsync(int departmentId, DateTime startUtc, DateTime endUtc, int maxRows,
			IReadOnlyCollection<string> callTypes = null, IReadOnlyCollection<int> priorities = null, IReadOnlyCollection<int> states = null,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The department's personnel statuses with a location in [startUtc, endUtc), newest first, at most
		/// <paramref name="maxRows"/>; only the columns a map needs.
		/// </summary>
		Task<List<ActionLog>> GetActionLogsWithLocationForMapAsync(int departmentId, DateTime startUtc, DateTime endUtc, int maxRows,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Unit states with a location in [startUtc, endUtc) for the department's units (all units, or <paramref name="unitIds"/>),
		/// newest first, at most <paramref name="maxRows"/>; protected (sealed) rows are left out.
		/// </summary>
		Task<List<UnitState>> GetUnitStatesWithLocationForMapAsync(int departmentId, DateTime startUtc, DateTime endUtc, IReadOnlyCollection<int> unitIds,
			int maxRows, CancellationToken cancellationToken = default);
	}
}
