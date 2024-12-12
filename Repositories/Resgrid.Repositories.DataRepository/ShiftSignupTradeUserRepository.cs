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
	public class ShiftSignupTradeUserRepository : RepositoryBase<ShiftSignupTradeUser>, IShiftSignupTradeUserRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ShiftSignupTradeUserRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ShiftSignupTradeUser>> GetShiftSignupTradeUsersByTradeIdAsync(int shiftTradeId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ShiftSignupTradeUser>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ShiftTradeId", shiftTradeId);

					var query = _queryFactory.GetQuery<SelectShiftSignupTradeUsersByTradeIdQuery>();

					var dictionary = new Dictionary<int, ShiftSignupTradeUser>();
					var result = await x.QueryAsync<ShiftSignupTradeUser, ShiftSignupTradeUserShift, ShiftSignupTradeUser>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ShiftGroupMapping(dictionary),
						splitOn: "ShiftGroupRoleId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value);
					
					return result;
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

		private static Func<ShiftSignupTradeUser, ShiftSignupTradeUserShift, ShiftSignupTradeUser> ShiftGroupMapping(Dictionary<int, ShiftSignupTradeUser> dictionary)
		{
			return new Func<ShiftSignupTradeUser, ShiftSignupTradeUserShift, ShiftSignupTradeUser>((tradeUser, tradeUserShift) =>
			{
				var dictionaryTradeUser = default(ShiftSignupTradeUser);

				if (tradeUserShift != null)
				{
					if (dictionary.TryGetValue(tradeUser.ShiftSignupTradeUserId, out dictionaryTradeUser))
					{
						if (dictionaryTradeUser.Shifts.All(x => x.ShiftSignupTradeUserShiftId != tradeUserShift.ShiftSignupTradeUserShiftId))
							dictionaryTradeUser.Shifts.Add(tradeUserShift);
					}
					else
					{
						if (tradeUser.Shifts == null)
							tradeUser.Shifts = new List<ShiftSignupTradeUserShift>();

						tradeUser.Shifts.Add(tradeUserShift);
						dictionary.Add(tradeUser.ShiftSignupTradeUserId, tradeUser);

						dictionaryTradeUser = tradeUser;
					}
				}
				else
				{
					tradeUser.Shifts = new List<ShiftSignupTradeUserShift>();
					dictionaryTradeUser = tradeUser;
					dictionary.Add(tradeUser.ShiftSignupTradeUserId, tradeUser);
				}

				return dictionaryTradeUser;
			});
		}
	}
}
