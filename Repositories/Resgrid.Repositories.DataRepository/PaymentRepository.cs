using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Departments;
using Resgrid.Repositories.DataRepository.Queries.Payments;

namespace Resgrid.Repositories.DataRepository
{
	public class PaymentRepository : RepositoryBase<Payment>, IPaymentRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public PaymentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<DepartmentPlanCount> GetDepartmentPlanCountsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentPlanCount>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectGetDepartmentPlanCountsQuery>();

					return await x.QueryFirstOrDefaultAsync<DepartmentPlanCount>(sql: query,
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
				Logging.LogException(ex, extraMessage: $"GetDepartmentPlanCountsByDepartmentIdAsync DepartmentId: {departmentId}");

				throw;
			}
		}

		public async Task<Payment> GetPaymentByTransactionIdAsync(string transactionId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Payment>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("TransactionId", transactionId);

					var query = _queryFactory.GetQuery<SelectPaymentByTransactionIdQuery>();

					return await x.QueryFirstOrDefaultAsync<Payment>(sql: query,
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

		public async Task<IEnumerable<Payment>> GetAllPaymentsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Payment>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectPaymentsByDIdQuery>();

					return await x.QueryAsync<Payment, Plan, Payment>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (pay, pl) => { pay.Plan = pl; return pay; },
						splitOn: "PlanId");
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

		public async Task<Payment> GetPaymentByIdIdAsync(int paymentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Payment>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("PaymentId", paymentId);

					var query = _queryFactory.GetQuery<SelectPaymentByIdQuery>();

					return (await x.QueryAsync<Payment, Plan, Payment>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (pay, pl) => { pay.Plan = pl; return pay; },
						splitOn: "PlanId")).FirstOrDefault();
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
