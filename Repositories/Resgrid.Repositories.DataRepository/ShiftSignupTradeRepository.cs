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
using Resgrid.Repositories.DataRepository.Queries.Shifts;

namespace Resgrid.Repositories.DataRepository
{
	public class ShiftSignupTradeRepository : RepositoryBase<ShiftSignupTrade>, IShiftSignupTradeRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ShiftSignupTradeRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<ShiftSignupTrade> GetShiftSignupTradeBySourceShiftSignupIdAsync(int shiftSignupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<ShiftSignupTrade>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ShiftSignupId", shiftSignupId);

					var query = _queryFactory.GetQuery<SelectShiftSignupTradeBySourceIdQuery>();

					var dictionary = new Dictionary<int, ShiftSignupTrade>();
					var result = await x.QueryAsync<ShiftSignupTrade, ShiftSignupTradeUser, ShiftSignupTrade>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ShiftSignupTradeMapping(dictionary),
						splitOn: "ShiftSignupTradeUserId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value).FirstOrDefault();
					
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

				return null;
			}
		}

		public async Task<ShiftSignupTrade> GetShiftSignupTradeByTargetShiftSignupIdAsync(int shiftSignupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<ShiftSignupTrade>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ShiftSignupId", shiftSignupId);

					var query = _queryFactory.GetQuery<SelectShiftSignupTradeByTargetIdQuery>();

					var dictionary = new Dictionary<int, ShiftSignupTrade>();
					var result = await x.QueryAsync<ShiftSignupTrade, ShiftSignupTradeUser, ShiftSignupTrade>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ShiftSignupTradeMapping(dictionary),
						splitOn: "ShiftSignupTradeUserId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value).FirstOrDefault();
					
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

				return null;
			}
		}

		public async Task<ShiftSignupTrade> GetShiftSignupTradeByUserIdAsync(int shiftSignupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<ShiftSignupTrade>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ShiftSignupId", shiftSignupId);

					var query = _queryFactory.GetQuery<SelectShiftSignupTradeByTargetIdQuery>();

					var dictionary = new Dictionary<int, ShiftSignupTrade>();
					var result = await x.QueryAsync<ShiftSignupTrade, ShiftSignupTradeUser, ShiftSignupTrade>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ShiftSignupTradeMapping(dictionary),
						splitOn: "ShiftSignupTradeUserId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value).FirstOrDefault();
					
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

				return null;
			}
		}

		public async Task<IEnumerable<ShiftSignupTrade>> GetAllOpenTradeRequestsByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ShiftSignupTrade>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectOpenShiftSignupTradesByUserIdQuery>();

					return await x.QueryAsync<ShiftSignupTrade>(sql: query,
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

		public async Task<IEnumerable<ShiftSignupTrade>> GetTradeRequestsAndSourceShiftsByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ShiftSignupTrade>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectShiftTradeAndSourceByUserIdQuery>();

					return await x.QueryAsync<ShiftSignupTrade, ShiftSignup, ShiftSignupTrade>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (sst, ss) => { sst.SourceShiftSignup = ss; return sst; },
						splitOn: "ShiftSignupId");

					return await x.QueryAsync<ShiftSignupTrade>(sql: query,
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

		private static Func<ShiftSignupTrade, ShiftSignupTradeUser, ShiftSignupTrade> ShiftSignupTradeMapping(Dictionary<int, ShiftSignupTrade> dictionary)
		{
			return new Func<ShiftSignupTrade, ShiftSignupTradeUser, ShiftSignupTrade>((shiftSignupTrade, shiftSignupTradeUser) =>
			{
				var dictionaryShiftSignupTrade = default(ShiftSignupTrade);

				if (shiftSignupTradeUser != null)
				{
					if (dictionary.TryGetValue(shiftSignupTrade.ShiftSignupTradeId, out shiftSignupTrade))
					{
						if (dictionaryShiftSignupTrade.Users.All(x => x.ShiftSignupTradeUserId != shiftSignupTradeUser.ShiftSignupTradeUserId))
							dictionaryShiftSignupTrade.Users.Add(shiftSignupTradeUser);
					}
					else
					{
						if (shiftSignupTrade.Users == null)
							shiftSignupTrade.Users = new List<ShiftSignupTradeUser>();

						shiftSignupTrade.Users.Add(shiftSignupTradeUser);
						dictionary.Add(shiftSignupTrade.ShiftSignupTradeId, shiftSignupTrade);

						dictionaryShiftSignupTrade = shiftSignupTrade;
					}
				}
				else
				{
					shiftSignupTrade.Users = new List<ShiftSignupTradeUser>();
					dictionaryShiftSignupTrade = shiftSignupTrade;
					dictionary.Add(shiftSignupTrade.ShiftSignupTradeId, shiftSignupTrade);
				}

				return dictionaryShiftSignupTrade;
			});
		}
	}
}
