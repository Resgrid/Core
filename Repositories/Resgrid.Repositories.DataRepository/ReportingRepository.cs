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
	/// Set-based aggregate data access for platform reporting. Entity-less (does not extend
	/// RepositoryBase&lt;T&gt;) because every method returns aggregates, never materialized rows.
	/// A null departmentId means system-wide: passed to SQL as %ALLDEPTS% = 1.
	/// </summary>
	public class ReportingRepository : IReportingRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ReportingRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		#region Scalar totals

		public async Task<long> GetCallsCountAsync(int? departmentId, DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = ScopeParams(departmentId);
				string query;
				if (startUtc.HasValue && endUtc.HasValue)
				{
					p.Add("StartDate", startUtc.Value);
					p.Add("EndDate", endUtc.Value);
					query = _queryFactory.GetQuery<SelectReportCallsCountQuery>();
				}
				else
				{
					query = _queryFactory.GetQuery<SelectReportCallsCountAllTimeQuery>();
				}

				return await conn.ExecuteScalarAsync<long>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<long> GetActiveCallsCountAsync(int? departmentId, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = ScopeParams(departmentId);
				var query = _queryFactory.GetQuery<SelectReportActiveCallsCountQuery>();
				return await conn.ExecuteScalarAsync<long>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<long> GetPersonnelCountAsync(int? departmentId, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = ScopeParams(departmentId);
				var query = _queryFactory.GetQuery<SelectReportPersonnelCountQuery>();
				return await conn.ExecuteScalarAsync<long>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<long> GetUnitsCountAsync(int? departmentId, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = ScopeParams(departmentId);
				var query = _queryFactory.GetQuery<SelectReportUnitsCountQuery>();
				return await conn.ExecuteScalarAsync<long>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<long> GetMessagesCountAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = WindowScopeParams(departmentId, startUtc, endUtc);
				var query = _queryFactory.GetQuery<SelectReportMessagesCountQuery>();
				return await conn.ExecuteScalarAsync<long>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		// No creation-date column exists on DepartmentMember/IdentityUser/UserProfile, so a "new users in
		// window" metric cannot be derived from the current schema. Returns 0 until such a column exists.
		public Task<long> GetNewUsersCountAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
			=> Task.FromResult(0L);

		public async Task<long> GetDepartmentsCountAsync(DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				if (startUtc.HasValue && endUtc.HasValue)
				{
					var p = new DynamicParametersExtension();
					p.Add("StartDate", startUtc.Value);
					p.Add("EndDate", endUtc.Value);
					var newQuery = _queryFactory.GetQuery<SelectReportNewDepartmentsQuery>();
					return await conn.ExecuteScalarAsync<long>(sql: newQuery, param: p, transaction: _unitOfWork.Transaction);
				}

				var totalQuery = _queryFactory.GetQuery<SelectReportDepartmentsTotalQuery>();
				return await conn.ExecuteScalarAsync<long>(sql: totalQuery, param: new DynamicParametersExtension(), transaction: _unitOfWork.Transaction);
			});
		}

		#endregion

		#region Time-bucketed series

		public async Task<IEnumerable<CountByDateBucketResult>> GetCallsByDateBucketAsync(int? departmentId, DateTime startUtc, DateTime endUtc, ReportGranularity granularity, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = WindowScopeParams(departmentId, startUtc, endUtc);
				var query = granularity == ReportGranularity.Month
					? _queryFactory.GetQuery<SelectReportCallsByMonthQuery>()
					: _queryFactory.GetQuery<SelectReportCallsByDayQuery>();
				return await conn.QueryAsync<CountByDateBucketResult>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<IEnumerable<CountByDateBucketResult>> GetMessagesByDateBucketAsync(int? departmentId, DateTime startUtc, DateTime endUtc, ReportGranularity granularity, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = WindowScopeParams(departmentId, startUtc, endUtc);
				var query = granularity == ReportGranularity.Month
					? _queryFactory.GetQuery<SelectReportMessagesByMonthQuery>()
					: _queryFactory.GetQuery<SelectReportMessagesByDayQuery>();
				return await conn.QueryAsync<CountByDateBucketResult>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		// See GetNewUsersCountAsync: no source creation-date column exists, so the series is empty.
		public Task<IEnumerable<CountByDateBucketResult>> GetNewUsersByDateBucketAsync(int? departmentId, DateTime startUtc, DateTime endUtc, ReportGranularity granularity, CancellationToken cancellationToken = default)
			=> Task.FromResult(Enumerable.Empty<CountByDateBucketResult>());

		#endregion

		#region Breakdowns

		public async Task<IEnumerable<CountByStringKeyResult>> GetCallsBreakdownByTypeAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = WindowScopeParams(departmentId, startUtc, endUtc);
				var query = _queryFactory.GetQuery<SelectReportCallsByTypeQuery>();
				return await conn.QueryAsync<CountByStringKeyResult>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<IEnumerable<CountByKeyResult>> GetCallsBreakdownByPriorityAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = WindowScopeParams(departmentId, startUtc, endUtc);
				var query = _queryFactory.GetQuery<SelectReportCallsByPriorityQuery>();
				return await conn.QueryAsync<CountByKeyResult>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<IEnumerable<CountByKeyResult>> GetCallsBreakdownByStateAsync(int? departmentId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = WindowScopeParams(departmentId, startUtc, endUtc);
				var query = _queryFactory.GetQuery<SelectReportCallsByStateQuery>();
				return await conn.QueryAsync<CountByKeyResult>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		#endregion

		#region Latest-state counts

		public async Task<IEnumerable<CountByKeyResult>> GetLatestPersonnelStateCountsAsync(int? departmentId, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = ScopeParams(departmentId);
				var query = _queryFactory.GetQuery<SelectReportLatestPersonnelStatesQuery>();
				return await conn.QueryAsync<CountByKeyResult>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		public async Task<IEnumerable<CountByKeyResult>> GetLatestUnitStateCountsAsync(int? departmentId, CancellationToken cancellationToken = default)
		{
			return await RunAsync(async conn =>
			{
				var p = ScopeParams(departmentId);
				var query = _queryFactory.GetQuery<SelectReportLatestUnitStatesQuery>();
				return await conn.QueryAsync<CountByKeyResult>(sql: query, param: p, transaction: _unitOfWork.Transaction);
			});
		}

		#endregion

		#region Helpers

		// Scope parameters: AllDepts = 1 (system-wide) when departmentId is null; otherwise filter to it.
		private static DynamicParametersExtension ScopeParams(int? departmentId)
		{
			var p = new DynamicParametersExtension();
			p.Add("AllDepts", departmentId.HasValue ? 0 : 1);
			p.Add("DepartmentId", departmentId ?? 0);
			return p;
		}

		private static DynamicParametersExtension WindowScopeParams(int? departmentId, DateTime startUtc, DateTime endUtc)
		{
			var p = ScopeParams(departmentId);
			p.Add("StartDate", startUtc);
			p.Add("EndDate", endUtc);
			return p;
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

		#endregion
	}
}
