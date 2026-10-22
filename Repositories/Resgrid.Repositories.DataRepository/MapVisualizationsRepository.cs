using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Saved map visualizations (M0288) and the visualizer's thin, capped reads.</summary>
	public class MapVisualizationsRepository : RmsRepositoryBase<MapVisualization>, IMapVisualizationsRepository
	{
		public MapVisualizationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<MapVisualization>> GetForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<MapVisualization>(
				$"SELECT * FROM {Tbl("MapVisualizations")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("Name")}",
				new { DepartmentId = departmentId });

			return rows.ToList();
		}

		public Task<MapVisualization> GetByIdForDepartmentAsync(string mapVisualizationId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<MapVisualization>(
				$"SELECT * FROM {Tbl("MapVisualizations")} WHERE {Col("MapVisualizationId")} = {P}MapVisualizationId AND {Col("DepartmentId")} = {P}DepartmentId",
				new { MapVisualizationId = mapVisualizationId, DepartmentId = departmentId });
		}

		public async Task<List<Call>> GetCallsForMapAsync(int departmentId, DateTime startUtc, DateTime endUtc, int maxRows,
			IReadOnlyCollection<string> callTypes = null, IReadOnlyCollection<int> priorities = null, IReadOnlyCollection<int> states = null,
			CancellationToken cancellationToken = default)
		{
			var columns = Cols("CallId", "DepartmentId", "Number", "Name", "Type", "CallTypeId", "Priority", "State", "Address", "GeoLocationData",
				"ReportingUserId", "LoggedOn", "ClosedOn", "DispatchOn", "IsDeleted");
			var where = $"{Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {P}NotDeleted AND {Col("LoggedOn")} >= {P}StartUtc AND {Col("LoggedOn")} < {P}EndUtc";

			where += states != null && states.Count > 0
				? $" AND {InList("State", "States")}"
				: $" AND {Col("State")} <> {P}Pending";

			if (callTypes != null && callTypes.Count > 0)
				where += $" AND {CaseInsensitiveInList("Type", "CallTypes")}";

			if (priorities != null && priorities.Count > 0)
				where += $" AND {InList("Priority", "Priorities")}";

			var sql = IsPostgres
				? $"SELECT {columns} FROM {Tbl("Calls")} WHERE {where} ORDER BY {Col("LoggedOn")} DESC LIMIT {P}MaxRows"
				: $"SELECT TOP ({P}MaxRows) {columns} FROM {Tbl("Calls")} WHERE {where} ORDER BY {Col("LoggedOn")} DESC";

			var rows = await QueryAsync<Call>(sql, new
			{
				DepartmentId = departmentId,
				NotDeleted = false,
				StartUtc = DatabaseTimestamp(startUtc),
				EndUtc = DatabaseTimestamp(endUtc),
				MaxRows = maxRows,
				Pending = (int)CallStates.Pending,
				States = InListValue(states),
				CallTypes = InListValue(callTypes),
				Priorities = InListValue(priorities)
			}, cancellationToken);

			return rows.ToList();
		}

		public async Task<List<ActionLog>> GetActionLogsWithLocationForMapAsync(int departmentId, DateTime startUtc, DateTime endUtc, int maxRows,
			CancellationToken cancellationToken = default)
		{
			var columns = Cols("ActionLogId", "DepartmentId", "UserId", "ActionTypeId", "Timestamp", "GeoLocationData", "DestinationId", "DestinationType");
			var where = $"{Col("DepartmentId")} = {P}DepartmentId AND {Col("Timestamp")} >= {P}StartUtc AND {Col("Timestamp")} < {P}EndUtc" +
				$" AND {Col("GeoLocationData")} IS NOT NULL AND {Col("GeoLocationData")} <> ''";

			var sql = IsPostgres
				? $"SELECT {columns} FROM {Tbl("ActionLogs")} WHERE {where} ORDER BY {Col("Timestamp")} DESC LIMIT {P}MaxRows"
				: $"SELECT TOP ({P}MaxRows) {columns} FROM {Tbl("ActionLogs")} WHERE {where} ORDER BY {Col("Timestamp")} DESC";

			var rows = await QueryAsync<ActionLog>(sql, new
			{
				DepartmentId = departmentId,
				StartUtc = DatabaseTimestamp(startUtc),
				EndUtc = DatabaseTimestamp(endUtc),
				MaxRows = maxRows
			}, cancellationToken);

			return rows.ToList();
		}

		public async Task<List<UnitState>> GetUnitStatesWithLocationForMapAsync(int departmentId, DateTime startUtc, DateTime endUtc, IReadOnlyCollection<int> unitIds,
			int maxRows, CancellationToken cancellationToken = default)
		{
			var columns = string.Join(", ", new[] { "UnitStateId", "UnitId", "State", "Timestamp", "GeoLocationData", "Latitude", "Longitude", "DestinationId", "DestinationType", "IsProtected" }
				.Select(c => "s." + Col(c)));
			var where = $"u.{Col("DepartmentId")} = {P}DepartmentId AND s.{Col("Timestamp")} >= {P}StartUtc AND s.{Col("Timestamp")} < {P}EndUtc" +
				$" AND s.{Col("IsProtected")} = {P}NotProtected" +
				$" AND ((s.{Col("Latitude")} IS NOT NULL AND s.{Col("Longitude")} IS NOT NULL) OR (s.{Col("GeoLocationData")} IS NOT NULL AND s.{Col("GeoLocationData")} <> ''))";

			if (unitIds != null && unitIds.Count > 0)
				where += $" AND {InList("UnitId", "UnitIds", "s")}";

			var from = $"{Tbl("UnitStates")} s INNER JOIN {Tbl("Units")} u ON u.{Col("UnitId")} = s.{Col("UnitId")}";
			var sql = IsPostgres
				? $"SELECT {columns} FROM {from} WHERE {where} ORDER BY s.{Col("Timestamp")} DESC LIMIT {P}MaxRows"
				: $"SELECT TOP ({P}MaxRows) {columns} FROM {from} WHERE {where} ORDER BY s.{Col("Timestamp")} DESC";

			var rows = await QueryAsync<UnitState>(sql, new
			{
				DepartmentId = departmentId,
				StartUtc = DatabaseTimestamp(startUtc),
				EndUtc = DatabaseTimestamp(endUtc),
				NotProtected = false,
				UnitIds = InListValue(unitIds),
				MaxRows = maxRows
			}, cancellationToken);

			return rows.ToList();
		}
	}
}
