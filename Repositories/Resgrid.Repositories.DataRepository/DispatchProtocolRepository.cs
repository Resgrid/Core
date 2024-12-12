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
using Resgrid.Repositories.DataRepository.Queries.Protocols;

namespace Resgrid.Repositories.DataRepository
{
	public class DispatchProtocolRepository : RepositoryBase<DispatchProtocol>, IDispatchProtocolRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DispatchProtocolRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<DispatchProtocol> GetDispatchProtocolByIdAsync(int protocolId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DispatchProtocol>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ProtocolId", protocolId);

					var query = _queryFactory.GetQuery<SelectProtocolByIdQuery>();

					var dictionary = new Dictionary<int, DispatchProtocol>();
					var result = await x.QueryAsync<DispatchProtocol, DispatchProtocolTrigger, DispatchProtocol>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DispatchProtocolTriggerMapping(dictionary),
						splitOn: "DispatchProtocolTriggerId");

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

		public async Task<IEnumerable<DispatchProtocol>> GetDispatchProtocolsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DispatchProtocol>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectProtocolsByDIdQuery>();

					var dictionary = new Dictionary<int, DispatchProtocol>();
					var result = await x.QueryAsync<DispatchProtocol, DispatchProtocolTrigger, DispatchProtocol>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DispatchProtocolTriggerMapping(dictionary),
						splitOn: "DispatchProtocolTriggerId");

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

		private static Func<DispatchProtocol, DispatchProtocolTrigger, DispatchProtocol> DispatchProtocolTriggerMapping(Dictionary<int, DispatchProtocol> dictionary)
		{
			return new Func<DispatchProtocol, DispatchProtocolTrigger, DispatchProtocol>((obj, detail) =>
			{
				var dictObj = default(DispatchProtocol);

				if (detail != null)
				{
					if (dictionary.TryGetValue((int)obj.IdValue, out dictObj))
					{
						if (dictObj.Triggers.All(x => x.DispatchProtocolTriggerId != detail.DispatchProtocolTriggerId))
							dictObj.Triggers.Add(detail);
					}
					else
					{
						if (obj.Triggers == null)
							obj.Triggers = new List<DispatchProtocolTrigger>();

						obj.Triggers.Add(detail);
						dictionary.Add((int)obj.IdValue, obj);

						dictObj = obj;
					}
				}
				else
				{
					obj.Triggers = new List<DispatchProtocolTrigger>();
					dictObj = obj;
					dictionary.Add((int)obj.IdValue, obj);
				}

				return dictObj;
			});
		}
	}
}
