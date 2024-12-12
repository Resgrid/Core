using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Units;
using Resgrid.Repositories.DataRepository.Queries.UnitStates;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitStatesRepository : RepositoryBase<UnitState>, IUnitStatesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UnitStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<UnitState>> GetAllStatesByUnitIdAsync(int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitState>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectUnitStatesByUnitIdQuery>();

					return await x.QueryAsync<UnitState, Unit, UnitState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (us, u) => { us.Unit = u; return us; },
						splitOn: "UnitId");
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

		public async Task<IEnumerable<UnitState>> GetAllUnitStatesForUnitInDateRangeAsync(int unitId, DateTime startDate, DateTime endDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitState>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);

					var query = _queryFactory.GetQuery<SelectUnitStatesByUnitInDateRangeQuery>();

					return await x.QueryAsync<UnitState, Unit, UnitState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (us, u) => { us.Unit = u; return us; },
						splitOn: "UnitId");
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

				return null;
			}
		}

		public async Task<UnitState> GetLastUnitStateByUnitIdAsync(int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UnitState>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectLastUnitStateByUnitIdQuery>();

					return (await x.QueryAsync<UnitState, Unit, UnitState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (us, u) => { us.Unit = u; return us; },
						splitOn: "UnitId")).FirstOrDefault();
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

		public async Task<UnitState> GetLastUnitStateBeforeIdAsync(int unitId, int unitStateId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UnitState>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);
					dynamicParameters.Add("UnitStateId", unitStateId);

					var query = _queryFactory.GetQuery<SelectLastUnitStateByUnitIdTimeQuery>();

					return (await x.QueryAsync<UnitState, Unit, UnitState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (us, u) => { us.Unit = u; return us; },
						splitOn: "UnitId")).FirstOrDefault();
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

		public async Task<UnitState> GetUnitStateByUnitStateIdAsync(int unitStateId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UnitState>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitStateId", unitStateId);

					var query = _queryFactory.GetQuery<SelectUnitStateByUnitStateIdQuery>();

					return (await x.QueryAsync<UnitState, Unit, UnitState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (us, u) => { us.Unit = u; return us; },
						splitOn: "UnitId")).FirstOrDefault();
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

		public async Task<IEnumerable<UnitState>> GetAllStatesByCallIdAsync(int callId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitState>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);

					var query = _queryFactory.GetQuery<SelectUnitStatesByCallIdQuery>();

					return await x.QueryAsync<UnitState, Unit, UnitState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (us, u) => { us.Unit = u; return us; },
						splitOn: "UnitId");
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

		public async Task<IEnumerable<UnitState>> GetLatestUnitStatesForDepartmentAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitState>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectLastUnitStatesByDidQuery>();

					return await x.QueryAsync<UnitState, Unit, UnitState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (us, u) => { us.Unit = u; return us; },
						splitOn: "UnitId");
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
