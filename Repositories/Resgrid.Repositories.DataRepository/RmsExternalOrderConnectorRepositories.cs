using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>External ordering-system connectors (RMS-1C completion, registry M0181). Dapper over RmsRepositoryBase.</summary>
	public class RmsExternalOrderConnectorsRepository : RmsRepositoryBase<RmsExternalOrderConnector>, IRmsExternalOrderConnectorsRepository
	{
		public RmsExternalOrderConnectorsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsExternalOrderConnector> GetByIdForDepartmentAsync(int departmentId, string connectorId)
		{
			return QueryFirstOrDefaultAsync<RmsExternalOrderConnector>(
				$"SELECT * FROM {Tbl("RmsExternalOrderConnectors")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderConnectorId")} = {P}Id",
				new { DepartmentId = departmentId, Id = connectorId });
		}

		public Task<RmsExternalOrderConnector> GetByIdAsync(string connectorId)
		{
			return QueryFirstOrDefaultAsync<RmsExternalOrderConnector>(
				$"SELECT * FROM {Tbl("RmsExternalOrderConnectors")} WHERE {Col("RmsExternalOrderConnectorId")} = {P}Id",
				new { Id = connectorId });
		}

		public Task<IEnumerable<RmsExternalOrderConnector>> GetForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RmsExternalOrderConnector>(
				$"SELECT * FROM {Tbl("RmsExternalOrderConnectors")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Name")}",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<RmsExternalOrderConnector>> GetDueAsync(DateTime utcNow, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", utcNow);
			parameters.Add("Skip", 0);
			parameters.Add("Take", take <= 0 ? 50 : Math.Min(take, 500));
			// Due = enabled, read authority, terms acknowledged, never polled or polled longer ago than its own interval.
			var elapsed = IsPostgres
				? $"{Col("LastPolledOn")} + ({Col("PollIntervalMinutes")} * INTERVAL '1 minute') <= {P}Now"
				: $"DATEADD(MINUTE, {Col("PollIntervalMinutes")}, {Col("LastPolledOn")}) <= {P}Now";
			return QueryAsync<RmsExternalOrderConnector>(
				$"SELECT * FROM {Tbl("RmsExternalOrderConnectors")} WHERE {Col("DeletedOn")} IS NULL AND {Col("IsEnabled")} = {True} AND {Col("ReadEnabled")} = {True} AND {Col("TermsAcknowledgedOn")} IS NOT NULL AND ({Col("LastPolledOn")} IS NULL OR {elapsed}) ORDER BY {Col("LastPolledOn")}, {Col("RmsExternalOrderConnectorId")} {Paging()}",
				parameters);
		}

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string connectorId, long expectedVersion, CancellationToken cancellationToken = default)
		{
			var rows = await ExecuteAsync(
				$"UPDATE {Tbl("RmsExternalOrderConnectors")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderConnectorId")} = {P}Id AND {Col("RowVersion")} = {P}Expected",
				new { DepartmentId = departmentId, Id = connectorId, Expected = expectedVersion }, cancellationToken);
			return rows == 1;
		}

		private static string True => IsPostgres ? "TRUE" : "1";
	}

	public class RmsExternalOrderConnectorRunsRepository : RmsRepositoryBase<RmsExternalOrderConnectorRun>, IRmsExternalOrderConnectorRunsRepository
	{
		public RmsExternalOrderConnectorRunsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsExternalOrderConnectorRun>> GetForConnectorAsync(int departmentId, string connectorId, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("ConnectorId", connectorId);
			parameters.Add("Skip", 0);
			parameters.Add("Take", take <= 0 ? 50 : Math.Min(take, 500));
			return QueryAsync<RmsExternalOrderConnectorRun>(
				$"SELECT * FROM {Tbl("RmsExternalOrderConnectorRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderConnectorId")} = {P}ConnectorId ORDER BY {Col("StartedOn")} DESC, {Col("RmsExternalOrderConnectorRunId")} {Paging()}",
				parameters);
		}

		public Task<int> TrimAsync(int departmentId, string connectorId, int keep, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("ConnectorId", connectorId);
			parameters.Add("Skip", Math.Max(1, keep));
			parameters.Add("Take", 100000);
			var newestBeyondKeep = $"SELECT {Col("RmsExternalOrderConnectorRunId")} FROM {Tbl("RmsExternalOrderConnectorRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderConnectorId")} = {P}ConnectorId ORDER BY {Col("StartedOn")} DESC, {Col("RmsExternalOrderConnectorRunId")} {Paging()}";
			// Both engines refuse a DELETE whose subquery reads the same table with paging; a derived table keeps them happy.
			return ExecuteAsync(
				$"DELETE FROM {Tbl("RmsExternalOrderConnectorRuns")} WHERE {Col("RmsExternalOrderConnectorRunId")} IN (SELECT {Col("RmsExternalOrderConnectorRunId")} FROM ({newestBeyondKeep}) AS stale)",
				parameters, cancellationToken);
		}
	}
}
