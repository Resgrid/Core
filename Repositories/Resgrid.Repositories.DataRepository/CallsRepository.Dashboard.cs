using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository
{
	public partial class CallsRepository
	{
		public async Task<IEnumerable<CallDispatchCounts>> GetActiveCallDispatchCountsAsync(int departmentId, DateTime nowUtc,
			CancellationToken cancellationToken = default)
		{
			// The same "open call" rule as SelectAllOpenCallsByDidDateQuery / SelectOpenCallUnitDispatchesForDepartmentQuery: active,
			// not deleted, and dispatched (a scheduled call waiting for its dispatch time is not on the board yet).
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var schema = _sqlConfiguration.SchemaName;
			var sql = postgres
				? "SELECT c.callid AS CallId, " +
				  $"(SELECT COUNT(DISTINCT cdu.unitid) FROM {schema}.calldispatchunits cdu WHERE cdu.callid = c.callid) AS UnitCount, " +
				  $"(SELECT COUNT(DISTINCT cd.userid) FROM {schema}.calldispatches cd WHERE cd.callid = c.callid) AS PersonnelCount " +
				  $"FROM {schema}.calls c " +
				  "WHERE c.departmentid = @DepartmentId AND c.state = 0 AND c.isdeleted = false " +
				  "AND (c.hasbeendispatched = true OR c.dispatchon IS NULL OR c.dispatchon <= @Now)"
				: "SELECT c.[CallId] AS [CallId], " +
				  $"(SELECT COUNT(DISTINCT cdu.[UnitId]) FROM {schema}.[CallDispatchUnits] cdu WHERE cdu.[CallId] = c.[CallId]) AS [UnitCount], " +
				  $"(SELECT COUNT(DISTINCT cd.[UserId]) FROM {schema}.[CallDispatches] cd WHERE cd.[CallId] = c.[CallId]) AS [PersonnelCount] " +
				  $"FROM {schema}.[Calls] c " +
				  "WHERE c.[DepartmentId] = @DepartmentId AND c.[State] = 0 AND c.[IsDeleted] = 0 " +
				  "AND (c.[HasBeenDispatched] = 1 OR c.[DispatchOn] IS NULL OR c.[DispatchOn] <= @Now)";

			// PostgreSQL timestamp columns carry no zone; Npgsql binds an Unspecified kind as timestamp.
			var parameters = new { DepartmentId = departmentId, Now = postgres ? DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified) : nowUtc };

			try
			{
				if (_unitOfWork?.Connection != null)
					return await _unitOfWork.CreateOrGetConnection().QueryAsync<CallDispatchCounts>(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction,
						cancellationToken: cancellationToken));

				await using var connection = _connectionProvider.Create();
				await connection.OpenAsync(cancellationToken);
				return await connection.QueryAsync<CallDispatchCounts>(new Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				throw;
			}
		}
	}
}
