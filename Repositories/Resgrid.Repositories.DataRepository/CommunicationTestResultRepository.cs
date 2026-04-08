using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.CommunicationTests;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class CommunicationTestResultRepository : RepositoryBase<CommunicationTestResult>, ICommunicationTestResultRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CommunicationTestResultRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CommunicationTestResult>> GetResultsByRunIdAsync(Guid communicationTestRunId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CommunicationTestResult>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CommunicationTestRunId", communicationTestRunId);

					var query = _queryFactory.GetQuery<SelectCommTestResultsByRunIdQuery>();

					return await x.QueryAsync<CommunicationTestResult>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<CommunicationTestResult> GetResultByResponseTokenAsync(string responseToken)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<CommunicationTestResult>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ResponseToken", responseToken);

					var query = _queryFactory.GetQuery<SelectCommTestResultByResponseTokenQuery>();

					var result = await x.QueryAsync<CommunicationTestResult>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					return result.FirstOrDefault();
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
