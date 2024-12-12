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
using Resgrid.Repositories.DataRepository.Queries.DistributionLists;

namespace Resgrid.Repositories.DataRepository
{
	public class DistributionListRepository : RepositoryBase<DistributionList>, IDistributionListRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DistributionListRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<DistributionList> GetDistributionListByEmailAddressAsync(string email)
		{
			try
			{

				var selectFunction = new Func<DbConnection, Task<DistributionList>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("EmailAddress", email);

					var query = _queryFactory.GetQuery<SelectDListByEmailQuery>();

					return await x.QueryFirstOrDefaultAsync<DistributionList>(sql: query,
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

		public async Task<IEnumerable<DistributionList>> GetAllActiveDistributionListsAsync()
		{
			try
			{

				var selectFunction = new Func<DbConnection, Task<IEnumerable<DistributionList>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();


					var query = _queryFactory.GetQuery<SelectAllEnabledDListsQuery>();

					return await x.QueryAsync<DistributionList>(sql: query,
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

		public async Task<IEnumerable<DistributionList>> GetDispatchProtocolsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DistributionList>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectDListsByDIdQuery>();

					var dictionary = new Dictionary<int, DistributionList>();
					var result = await x.QueryAsync<DistributionList, DistributionListMember, DistributionList>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DistributionListMemberMapping(dictionary),
						splitOn: "DistributionListMemberId");

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

		public async Task<DistributionList> GetDistributionListByIdAsync(int distributionListId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DistributionList>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ListId", distributionListId);

					var query = _queryFactory.GetQuery<SelectDListByIdQuery>();

					var dictionary = new Dictionary<int, DistributionList>();
					var result = await x.QueryAsync<DistributionList, DistributionListMember, DistributionList>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DistributionListMemberMapping(dictionary),
						splitOn: "DistributionListMemberId");

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

		private static Func<DistributionList, DistributionListMember, DistributionList> DistributionListMemberMapping(Dictionary<int, DistributionList> dictionary)
		{
			return new Func<DistributionList, DistributionListMember, DistributionList>((obj, detail) =>
			{
				var dictObj = default(DistributionList);

				if (detail != null)
				{
					if (dictionary.TryGetValue((int)obj.IdValue, out dictObj))
					{
						if (dictObj.Members.All(x => x.IdValue != detail.IdValue))
							dictObj.Members.Add(detail);
					}
					else
					{
						if (obj.Members == null)
							obj.Members = new List<DistributionListMember>();

						obj.Members.Add(detail);
						dictionary.Add((int)obj.IdValue, obj);

						dictObj = obj;
					}
				}
				else
				{
					obj.Members = new List<DistributionListMember>();
					dictObj = obj;
					dictionary.Add((int)obj.IdValue, obj);
				}

				return dictObj;
			});
		}
	}
}
