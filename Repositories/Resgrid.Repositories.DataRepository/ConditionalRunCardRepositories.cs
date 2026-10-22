using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Shared connection handling for the conditional run card repositories' few custom statements: on the request's unit
	/// of work when one is open (so a card's graph is read and written in one transaction), otherwise on its own
	/// connection. The statements are written per dialect inline, like CallsRepository.DispatchClaim.
	/// </summary>
	internal static class ConditionalRunCardSql
	{
		public static bool IsPostgres => DataConfig.DatabaseType == DatabaseTypes.Postgres;

		public static async Task<TResult> RunAsync<TResult>(IConnectionProvider connectionProvider, IUnitOfWork unitOfWork,
			Func<DbConnection, IDbTransaction, Task<TResult>> work)
		{
			try
			{
				if (unitOfWork?.Connection == null)
				{
					using (var conn = connectionProvider.Create())
					{
						await conn.OpenAsync();
						return await work(conn, null);
					}
				}

				var connection = unitOfWork.CreateOrGetConnection();
				return await work(connection, unitOfWork.Transaction);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		/// <summary>SQL Server compares against datetime2 columns; Postgres stores timestamp without time zone.</summary>
		public static void AddTimestamp(DynamicParameters parameters, string name, DateTime? value)
		{
			if (IsPostgres)
				parameters.Add(name, value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : (DateTime?)null);
			else
				parameters.Add(name, value, DbType.DateTime2);
		}
	}

	public class RunCardDecisionNodesRepository : AuditedConfigurationRepository<RunCardDecisionNode>, IRunCardDecisionNodesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public RunCardDecisionNodesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public Task<IEnumerable<RunCardDecisionNode>> GetNodesByRunCardIdAsync(int runCardId)
		{
			var sql = ConditionalRunCardSql.IsPostgres
				? $"SELECT n.* FROM {_sqlConfiguration.SchemaName}.runcarddecisionnodes n INNER JOIN {_sqlConfiguration.SchemaName}.runcardalarmlevels l ON n.runcardalarmlevelid = l.runcardalarmlevelid WHERE l.runcardid = @RunCardId ORDER BY n.sortorder, n.runcarddecisionnodeid"
				: $"SELECT n.* FROM {_sqlConfiguration.SchemaName}.[RunCardDecisionNodes] n INNER JOIN {_sqlConfiguration.SchemaName}.[RunCardAlarmLevels] l ON n.[RunCardAlarmLevelId] = l.[RunCardAlarmLevelId] WHERE l.[RunCardId] = @RunCardId ORDER BY n.[SortOrder], n.[RunCardDecisionNodeId]";

			return ConditionalRunCardSql.RunAsync(_connectionProvider, _unitOfWork,
				(conn, tx) => conn.QueryAsync<RunCardDecisionNode>(sql, new { RunCardId = runCardId }, tx));
		}
	}

	public class ResponsePackagesRepository : AuditedConfigurationRepository<ResponsePackage>, IResponsePackagesRepository
	{
		public ResponsePackagesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
		}
	}

	public class ResponsePackageRowsRepository : AuditedConfigurationRepository<ResponsePackageRow>, IResponsePackageRowsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ResponsePackageRowsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public Task<IEnumerable<ResponsePackageRow>> GetRowsByDepartmentIdAsync(int departmentId)
		{
			var sql = ConditionalRunCardSql.IsPostgres
				? $"SELECT r.* FROM {_sqlConfiguration.SchemaName}.responsepackagerows r INNER JOIN {_sqlConfiguration.SchemaName}.responsepackages p ON r.responsepackageid = p.responsepackageid WHERE p.departmentid = @DepartmentId ORDER BY r.sortorder, r.responsepackagerowid"
				: $"SELECT r.* FROM {_sqlConfiguration.SchemaName}.[ResponsePackageRows] r INNER JOIN {_sqlConfiguration.SchemaName}.[ResponsePackages] p ON r.[ResponsePackageId] = p.[ResponsePackageId] WHERE p.[DepartmentId] = @DepartmentId ORDER BY r.[SortOrder], r.[ResponsePackageRowId]";

			return ConditionalRunCardSql.RunAsync(_connectionProvider, _unitOfWork,
				(conn, tx) => conn.QueryAsync<ResponsePackageRow>(sql, new { DepartmentId = departmentId }, tx));
		}
	}

	public class RunCardTargetsRepository : AuditedConfigurationRepository<RunCardTarget>, IRunCardTargetsRepository
	{
		public RunCardTargetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
		}
	}

	public class RunCardDecisionsRepository : RepositoryBase<RunCardDecision>, IRunCardDecisionsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public RunCardDecisionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public Task<IEnumerable<RunCardDecision>> GetDecisionsByCallIdAsync(int callId)
		{
			var sql = ConditionalRunCardSql.IsPostgres
				? $"SELECT * FROM {_sqlConfiguration.SchemaName}.runcarddecisions WHERE callid = @CallId ORDER BY createdon DESC, runcarddecisionid DESC"
				: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[RunCardDecisions] WHERE [CallId] = @CallId ORDER BY [CreatedOn] DESC, [RunCardDecisionId] DESC";

			return ConditionalRunCardSql.RunAsync(_connectionProvider, _unitOfWork,
				(conn, tx) => conn.QueryAsync<RunCardDecision>(sql, new { CallId = callId }, tx));
		}
	}

	public class CallOutstandingRequirementsRepository : RepositoryBase<CallOutstandingRequirement>, ICallOutstandingRequirementsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public CallOutstandingRequirementsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public Task<IEnumerable<CallOutstandingRequirement>> GetOpenByDepartmentIdAsync(int departmentId)
		{
			var sql = ConditionalRunCardSql.IsPostgres
				? $"SELECT * FROM {_sqlConfiguration.SchemaName}.calloutstandingrequirements WHERE departmentid = @DepartmentId AND resolvedon IS NULL ORDER BY createdon"
				: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[CallOutstandingRequirements] WHERE [DepartmentId] = @DepartmentId AND [ResolvedOn] IS NULL ORDER BY [CreatedOn]";

			return ConditionalRunCardSql.RunAsync(_connectionProvider, _unitOfWork,
				(conn, tx) => conn.QueryAsync<CallOutstandingRequirement>(sql, new { DepartmentId = departmentId }, tx));
		}

		public Task<IEnumerable<CallOutstandingRequirement>> GetByCallIdAsync(int callId)
		{
			var sql = ConditionalRunCardSql.IsPostgres
				? $"SELECT * FROM {_sqlConfiguration.SchemaName}.calloutstandingrequirements WHERE callid = @CallId ORDER BY createdon"
				: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[CallOutstandingRequirements] WHERE [CallId] = @CallId ORDER BY [CreatedOn]";

			return ConditionalRunCardSql.RunAsync(_connectionProvider, _unitOfWork,
				(conn, tx) => conn.QueryAsync<CallOutstandingRequirement>(sql, new { CallId = callId }, tx));
		}

		public Task<bool> TryClaimAsync(int callOutstandingRequirementId, int departmentId, int unitId, DateTime claimedOn, CancellationToken cancellationToken = default)
		{
			var sql = ConditionalRunCardSql.IsPostgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calloutstandingrequirements SET claimedon = @ClaimedOn, claimedbyunitid = @UnitId WHERE calloutstandingrequirementid = @Id AND departmentid = @DepartmentId AND claimedon IS NULL AND resolvedon IS NULL"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[CallOutstandingRequirements] SET [ClaimedOn] = @ClaimedOn, [ClaimedByUnitId] = @UnitId WHERE [CallOutstandingRequirementId] = @Id AND [DepartmentId] = @DepartmentId AND [ClaimedOn] IS NULL AND [ResolvedOn] IS NULL";

			var parameters = new DynamicParameters(new { Id = callOutstandingRequirementId, DepartmentId = departmentId, UnitId = unitId });
			ConditionalRunCardSql.AddTimestamp(parameters, "ClaimedOn", claimedOn);

			return ConditionalRunCardSql.RunAsync(_connectionProvider, _unitOfWork, async (conn, tx) =>
				await conn.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, tx, cancellationToken: cancellationToken)) == 1);
		}

		public Task<bool> ReleaseClaimAsync(int callOutstandingRequirementId, int departmentId, int unitId, CancellationToken cancellationToken = default)
		{
			var sql = ConditionalRunCardSql.IsPostgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calloutstandingrequirements SET claimedon = NULL, claimedbyunitid = NULL WHERE calloutstandingrequirementid = @Id AND departmentid = @DepartmentId AND claimedbyunitid = @UnitId AND resolvedon IS NULL"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[CallOutstandingRequirements] SET [ClaimedOn] = NULL, [ClaimedByUnitId] = NULL WHERE [CallOutstandingRequirementId] = @Id AND [DepartmentId] = @DepartmentId AND [ClaimedByUnitId] = @UnitId AND [ResolvedOn] IS NULL";

			return ConditionalRunCardSql.RunAsync(_connectionProvider, _unitOfWork, async (conn, tx) =>
				await conn.ExecuteAsync(new Dapper.CommandDefinition(sql, new { Id = callOutstandingRequirementId, DepartmentId = departmentId, UnitId = unitId }, tx, cancellationToken: cancellationToken)) == 1);
		}
	}
}
