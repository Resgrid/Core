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
using Resgrid.Repositories.DataRepository.Queries.CustomStates;

namespace Resgrid.Repositories.DataRepository
{
	public class CustomStateRepository : RepositoryBase<CustomState>, ICustomStateRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CustomStateRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CustomState>> GetCustomStatesByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CustomState>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectStatesByDidUserQuery>();

					var dictionary = new Dictionary<int, CustomState>();
					var result = await x.QueryAsync<CustomState, CustomStateDetail, CustomState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: CustomStateDetailMapping(dictionary),
						splitOn: "CustomStateDetailId");

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

		public async Task<CustomState> GetCustomStatesByIdAsync(int customStateId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<CustomState>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomStateId", customStateId);

					var query = _queryFactory.GetQuery<SelectStatesByIdQuery>();

					var dictionary = new Dictionary<int, CustomState>();
					var result = await x.QueryAsync<CustomState, CustomStateDetail, CustomState>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: CustomStateDetailMapping(dictionary),
						splitOn: "CustomStateDetailId");

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

		private static Func<CustomState, CustomStateDetail, CustomState> CustomStateDetailMapping(Dictionary<int, CustomState> dictionary)
		{
			return new Func<CustomState, CustomStateDetail, CustomState>((obj, detail) =>
			{
				var dictObj = default(CustomState);

				if (detail != null)
				{
					if (dictionary.TryGetValue((int)obj.IdValue, out dictObj))
					{
						if (dictObj.Details.All(x => x.CustomStateDetailId != detail.CustomStateDetailId))
							dictObj.Details.Add(detail);
					}
					else
					{
						if (obj.Details == null)
							obj.Details = new List<CustomStateDetail>();

						obj.Details.Add(detail);
						dictionary.Add((int)obj.IdValue, obj);

						dictObj = obj;
					}
				}
				else
				{
					obj.Details = new List<CustomStateDetail>();
					dictObj = obj;
					dictionary.Add((int)obj.IdValue, obj);
				}

				return dictObj;
			});
		}
	}
}
