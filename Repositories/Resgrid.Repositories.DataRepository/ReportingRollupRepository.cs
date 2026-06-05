using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Reporting;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Dapper-backed read/write for the <see cref="ReportingDailyRollup"/> store. Entity-less; uses the
	/// templated reporting queries. Upsert is delete-then-insert per (department, day) for idempotency.
	/// </summary>
	public class ReportingRollupRepository : IReportingRollupRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ReportingRollupRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<int> UpsertDailyRollupAsync(int? departmentId, DateTime bucketDateUtc,
			IEnumerable<ReportingDailyRollup> rows, CancellationToken cancellationToken = default)
		{
			var rowList = (rows ?? Enumerable.Empty<ReportingDailyRollup>()).ToList();
			var now = DateTime.UtcNow;
			foreach (var row in rowList)
			{
				row.DepartmentId = departmentId;
				row.BucketDateUtc = bucketDateUtc;
				if (row.CreatedOnUtc == default)
					row.CreatedOnUtc = now;
			}

			return await RunAsync(async conn =>
			{
				var deleteParams = new DynamicParametersExtension();
				deleteParams.Add("BucketDate", bucketDateUtc);
				deleteParams.Add("HasDept", departmentId.HasValue ? 1 : 0);
				deleteParams.Add("DepartmentId", departmentId ?? 0);

				var deleteQuery = _queryFactory.GetQuery<DeleteReportRollupForDateQuery>();
				var deleteCommand = new CommandDefinition(deleteQuery, deleteParams, _unitOfWork.Transaction, cancellationToken: cancellationToken);
				await conn.ExecuteAsync(deleteCommand);

				if (rowList.Count > 0)
				{
					var insertQuery = _queryFactory.GetQuery<InsertReportRollupQuery>();
					var insertCommand = new CommandDefinition(insertQuery, rowList, _unitOfWork.Transaction, cancellationToken: cancellationToken);
					await conn.ExecuteAsync(insertCommand);
				}

				return rowList.Count;
			});
		}

		public async Task<IEnumerable<ReportingDailyRollup>> GetRollupsAsync(int? departmentId, DateTime startUtc,
			DateTime endUtc, string metric, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = new DynamicParametersExtension();
				p.Add("StartDate", startUtc);
				p.Add("EndDate", endUtc);
				p.Add("Metric", metric);
				p.Add("HasDept", departmentId.HasValue ? 1 : 0);
				p.Add("DepartmentId", departmentId ?? 0);

				var query = _queryFactory.GetQuery<SelectReportRollupsQuery>();
				var command = new CommandDefinition(query, p, _unitOfWork.Transaction, cancellationToken: cancellationToken);
				return await conn.QueryAsync<ReportingDailyRollup>(command);
			});
		}

		private async Task<TResult> RunAsync<TResult>(Func<DbConnection, Task<TResult>> selectFunction)
		{
			try
			{
				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						return await selectFunction(conn);
					}
				}

				conn = _unitOfWork.CreateOrGetConnection();
				return await selectFunction(conn);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
