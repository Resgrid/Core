using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Counts legacy Logs and UnitLogs for the activation preview and cutover checksum. Reuses the RMS
	/// dialect plumbing; issues COUNT/MAX only, never loads or touches a legacy row.
	/// </summary>
	public class RmsLegacyStatsRepository : RmsRepositoryBase<RmsDepartmentCutover>, IRmsLegacyStatsRepository
	{
		public RmsLegacyStatsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<RmsLegacyStats> GetLegacyStatsAsync(int departmentId)
		{
			var stats = new RmsLegacyStats();
			var parameters = new { DepartmentId = departmentId, EventType = 3 };

			stats.LogCount = await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("Logs")} WHERE {Col("DepartmentId")} = {P}DepartmentId", parameters);
			stats.EventTypeLogCount = await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("Logs")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LogType")} = {P}EventType", parameters);
			stats.LogsWithoutGroupCount = await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("Logs")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("StationGroupId")} IS NULL", parameters);
			stats.MaxLogId = await ScalarAsync<int?>(
				$"SELECT MAX({Col("LogId")}) FROM {Tbl("Logs")} WHERE {Col("DepartmentId")} = {P}DepartmentId", parameters) ?? 0;

			// UnitLogs carry no DepartmentId; scope through the owning Unit.
			stats.UnitLogCount = await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("UnitLogs")} ul INNER JOIN {Tbl("Units")} u ON u.{Col("UnitId")} = ul.{Col("UnitId")} WHERE u.{Col("DepartmentId")} = {P}DepartmentId", parameters);
			stats.MaxUnitLogId = await ScalarAsync<int?>(
				$"SELECT MAX(ul.{Col("UnitLogId")}) FROM {Tbl("UnitLogs")} ul INNER JOIN {Tbl("Units")} u ON u.{Col("UnitId")} = ul.{Col("UnitId")} WHERE u.{Col("DepartmentId")} = {P}DepartmentId", parameters) ?? 0;

			return stats;
		}
	}
}
