using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.DepartmentGroups;
using Resgrid.Repositories.DataRepository.Queries.Messages;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentGroupsRepository : RepositoryBase<DepartmentGroup>, IDepartmentGroupsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentGroupsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<List<DepartmentGroup>> GetAllStationGroupsForDepartmentAsync(int departmentId)
		{
			Dictionary<int, DepartmentGroup> lookup = new Dictionary<int, DepartmentGroup>();
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					var query = @"SELECT * FROM departmentgroups dg
							LEFT OUTER JOIN addresses a ON dg.addressid = a.addressid
							WHERE dg.departmentid = @departmentId AND dg.type = 2";

					await db.QueryAsync<DepartmentGroup, Address, DepartmentGroup>(query, (dg, a) =>
					{
						DepartmentGroup group;

						if (!lookup.TryGetValue(dg.DepartmentGroupId, out group))
						{
							lookup.Add(dg.DepartmentGroupId, dg);
							group = dg;
						}

						if (a != null && dg.Address == null)
						{
							group.Address = a;
						}

						return dg;

					}, new { departmentId = departmentId }, splitOn: "AddressId");
				}

				return lookup.Values.ToList();
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var query = @"SELECT * FROM DepartmentGroups dg
							LEFT OUTER JOIN Addresses a ON dg.AddressId = a.AddressId
							WHERE dg.DepartmentId = @departmentId AND dg.Type = 2";

					await db.QueryAsync<DepartmentGroup, Address, DepartmentGroup>(query, (dg, a) =>
					{
						DepartmentGroup group;

						if (!lookup.TryGetValue(dg.DepartmentGroupId, out group))
						{
							lookup.Add(dg.DepartmentGroupId, dg);
							group = dg;
						}

						if (a != null && dg.Address == null)
						{
							group.Address = a;
						}

						return dg;

					}, new { departmentId = departmentId }, splitOn: "AddressId");
				}

				return lookup.Values.ToList();
			}
		}

		public async Task<IEnumerable<DepartmentGroup>> GetAllGroupsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentGroup>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectAllGroupsByDidQuery>();

					var dictionary = new Dictionary<int, DepartmentGroup>();
					var result = await x.QueryAsync<DepartmentGroup, DepartmentGroupMember, DepartmentGroup>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: GroupMemberMapping(dictionary),
						splitOn: "DepartmentGroupMemberId");

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

		public async Task<IEnumerable<DepartmentGroup>> GetAllGroupsByParentGroupIdAsync(int parentGroupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentGroup>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("GroupId", parentGroupId);

					var query = _queryFactory.GetQuery<SelectAllGroupsByParentIdQuery>();

					var dictionary = new Dictionary<int, DepartmentGroup>();
					var result = await x.QueryAsync<DepartmentGroup, DepartmentGroupMember, DepartmentGroup>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: GroupMemberMapping(dictionary),
						splitOn: "DepartmentGroupMemberId");

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

		public async Task<DepartmentGroup> GetGroupByGroupIdAsync(int departmentGroupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentGroup>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("GroupId", departmentGroupId);

					var query = _queryFactory.GetQuery<SelectGroupByGroupIdQuery>();

					var dictionary = new Dictionary<int, DepartmentGroup>();
					var result = await x.QueryAsync<DepartmentGroup, DepartmentGroupMember, DepartmentGroup>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: GroupMemberMapping(dictionary),
						splitOn: "DepartmentGroupMemberId");

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

		public async Task<DepartmentGroup> GetGroupByDispatchCodeAsync(string dispatchCode)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentGroup>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DispatchEmail", dispatchCode);

					var query = _queryFactory.GetQuery<SelectGroupByDispatchCodeQuery>();

					var dictionary = new Dictionary<int, DepartmentGroup>();
					var result = await x.QueryAsync<DepartmentGroup, DepartmentGroupMember, DepartmentGroup>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: GroupMemberMapping(dictionary),
						splitOn: "DepartmentGroupMemberId");

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

		public async Task<DepartmentGroup> GetGroupByMessageCodeAsync(string messageCode)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentGroup>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("MessageEmail", messageCode);

					var query = _queryFactory.GetQuery<SelectGroupByMessageCodeQuery>();

					var dictionary = new Dictionary<int, DepartmentGroup>();
					var result = await x.QueryAsync<DepartmentGroup, DepartmentGroupMember, DepartmentGroup>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: GroupMemberMapping(dictionary),
						splitOn: "DepartmentGroupMemberId");

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

		private static Func<DepartmentGroup, DepartmentGroupMember, DepartmentGroup> GroupMemberMapping(Dictionary<int, DepartmentGroup> dictionary)
		{
			return new Func<DepartmentGroup, DepartmentGroupMember, DepartmentGroup>((group, groupMember) =>
			{
				var dictionaryGroup = default(DepartmentGroup);

				if (groupMember != null)
				{
					if (dictionary.TryGetValue(group.DepartmentGroupId, out dictionaryGroup))
					{
						if (dictionaryGroup.Members.All(x => x.DepartmentGroupMemberId != groupMember.DepartmentGroupMemberId))
							dictionaryGroup.Members.Add(groupMember);
					}
					else
					{
						if (group.Members == null)
							group.Members = new List<DepartmentGroupMember>();

						group.Members.Add(groupMember);
						dictionary.Add(group.DepartmentGroupId, group);

						dictionaryGroup = group;
					}
				}
				else
				{
					group.Members = new List<DepartmentGroupMember>();
					dictionaryGroup = group;
					dictionary.Add(group.DepartmentGroupId, group);
				}

				return dictionaryGroup;
			});
		}
	}
}
