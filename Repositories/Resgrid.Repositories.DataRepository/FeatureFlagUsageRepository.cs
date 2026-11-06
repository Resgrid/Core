using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class FeatureFlagUsageRepository : RepositoryBase<FeatureFlagUsage>, IFeatureFlagUsageRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public FeatureFlagUsageRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task AddUsageAsync(FeatureFlagUsage usage, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				var p = _sqlConfiguration.ParameterNotation;
				string upsertSql;
				if (Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres)
				{
					// Atomic upsert on the unique (featureflagid, COALESCE(departmentid, 0), usagedate) index added by M0310;
					// the COALESCE makes the department-less row conflict too (plain NULLs never do in PostgreSQL).
					upsertSql = $@"INSERT INTO {_sqlConfiguration.SchemaName}.featureflagusages (featureflagid, departmentid, usagedate, evaluationcount, enabledcount, disabledcount)
						VALUES ({p}FeatureFlagId, {p}DepartmentId, {p}UsageDate, {p}EvaluationCount, {p}EnabledCount, {p}DisabledCount)
						ON CONFLICT (featureflagid, (COALESCE(departmentid, 0)), usagedate)
						DO UPDATE SET evaluationcount = featureflagusages.evaluationcount + EXCLUDED.evaluationcount,
							enabledcount = featureflagusages.enabledcount + EXCLUDED.enabledcount,
							disabledcount = featureflagusages.disabledcount + EXCLUDED.disabledcount";
				}
				else
				{
					// HOLDLOCK serializes concurrent flushes of the same key; the unique index added by M0310 backs it up.
					upsertSql = $@"MERGE {_sqlConfiguration.SchemaName}.[FeatureFlagUsages] WITH (HOLDLOCK) AS target
						USING (SELECT {p}FeatureFlagId AS FeatureFlagId, {p}DepartmentId AS DepartmentId, {p}UsageDate AS UsageDate) AS source
						ON target.[FeatureFlagId] = source.FeatureFlagId AND target.[UsageDate] = source.UsageDate
							AND (target.[DepartmentId] = source.DepartmentId OR (target.[DepartmentId] IS NULL AND source.DepartmentId IS NULL))
						WHEN MATCHED THEN
							UPDATE SET [EvaluationCount] = target.[EvaluationCount] + {p}EvaluationCount,
								[EnabledCount] = target.[EnabledCount] + {p}EnabledCount,
								[DisabledCount] = target.[DisabledCount] + {p}DisabledCount
						WHEN NOT MATCHED THEN
							INSERT ([FeatureFlagId], [DepartmentId], [UsageDate], [EvaluationCount], [EnabledCount], [DisabledCount])
							VALUES ({p}FeatureFlagId, {p}DepartmentId, {p}UsageDate, {p}EvaluationCount, {p}EnabledCount, {p}DisabledCount);";
				}

				var dp = new DynamicParametersExtension();
				dp.Add("FeatureFlagId", usage.FeatureFlagId);
				dp.Add("DepartmentId", usage.DepartmentId, DbType.Int32);
				dp.Add("UsageDate", usage.UsageDate.Date);
				dp.Add("EvaluationCount", usage.EvaluationCount);
				dp.Add("EnabledCount", usage.EnabledCount);
				dp.Add("DisabledCount", usage.DisabledCount);

				if (_unitOfWork?.Connection == null)
				{
					using (var conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);
						await conn.ExecuteAsync(new Dapper.CommandDefinition(upsertSql, dp, cancellationToken: cancellationToken));
					}
				}
				else
				{
					var conn = _unitOfWork.CreateOrGetConnection();
					await conn.ExecuteAsync(new Dapper.CommandDefinition(upsertSql, dp, _unitOfWork.Transaction, cancellationToken: cancellationToken));
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<FeatureFlagUsage>> GetUsageForFlagAsync(int featureFlagId, DateTime fromDate, DateTime toDate)
		{
			try
			{
				var p = _sqlConfiguration.ParameterNotation;
				var query = Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.featureflagusages WHERE featureflagid = {p}FeatureFlagId AND usagedate >= {p}FromDate AND usagedate <= {p}ToDate ORDER BY usagedate"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[FeatureFlagUsages] WHERE [FeatureFlagId] = {p}FeatureFlagId AND [UsageDate] >= {p}FromDate AND [UsageDate] <= {p}ToDate ORDER BY [UsageDate]";

				var selectFunction = new Func<DbConnection, Task<IEnumerable<FeatureFlagUsage>>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("FeatureFlagId", featureFlagId);
					dp.Add("FromDate", fromDate.Date);
					dp.Add("ToDate", toDate.Date);
					return await x.QueryAsync<FeatureFlagUsage>(sql: query, param: dp, transaction: _unitOfWork.Transaction);
				});

				if (_unitOfWork?.Connection == null)
				{
					using (var conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						return await selectFunction(conn);
					}
				}

				return await selectFunction(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
