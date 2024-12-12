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
	public class ShiftGroupsRepository : RepositoryBase<ShiftGroup>, IShiftGroupsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ShiftGroupsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ShiftGroup>> GetShiftGroupsByGroupIdAsync(int departmentGroupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ShiftGroup>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("GroupId", departmentGroupId);

					var query = _queryFactory.GetQuery<SelectShiftGroupByGroupQuery>();

					var dictionary = new Dictionary<int, ShiftGroup>();
					var result = await x.QueryAsync<ShiftGroup, ShiftGroupRole, ShiftGroup>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ShiftGroupMapping(dictionary),
						splitOn: "ShiftGroupRoleId");

					List<ShiftGroup> shiftGroups = null;
					if (dictionary.Count > 0)
						shiftGroups = dictionary.Select(y => y.Value).ToList();
					else
						shiftGroups = result.ToList();

					return shiftGroups.AsEnumerable();
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

		public async Task<IEnumerable<ShiftGroup>> GetShiftGroupsByShiftIdAsync(int shiftId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ShiftGroup>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ShiftId", shiftId);

					var query = _queryFactory.GetQuery<SelectShiftGroupByShiftIdQuery>();

					var dictionary = new Dictionary<int, ShiftGroup>();
					var result = await x.QueryAsync<ShiftGroup, ShiftGroupRole, ShiftGroup>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: ShiftGroupMapping(dictionary),
						splitOn: "ShiftGroupRoleId");

					List<ShiftGroup> shiftGroups = null;
					if (dictionary.Count > 0)
						shiftGroups = dictionary.Select(y => y.Value).ToList();
					else
						shiftGroups = result.ToList();

					return shiftGroups.AsEnumerable();
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

		private static Func<ShiftGroup, ShiftGroupRole, ShiftGroup> ShiftGroupMapping(Dictionary<int, ShiftGroup> dictionary)
		{
			return new Func<ShiftGroup, ShiftGroupRole, ShiftGroup>((shiftGroup, shiftGroupRole) =>
			{
				var dictionaryShiftGroup = default(ShiftGroup);

				if (shiftGroupRole != null)
				{
					if (dictionary.TryGetValue(shiftGroup.ShiftGroupId, out dictionaryShiftGroup))
					{
						if (dictionaryShiftGroup.Roles.All(x => x.ShiftGroupRoleId != shiftGroupRole.ShiftGroupRoleId))
							dictionaryShiftGroup.Roles.Add(shiftGroupRole);
					}
					else
					{
						if (shiftGroup.Roles == null)
							shiftGroup.Roles = new List<ShiftGroupRole>();

						shiftGroup.Roles.Add(shiftGroupRole);
						dictionary.Add(shiftGroup.ShiftGroupId, shiftGroup);

						dictionaryShiftGroup = shiftGroup;
					}
				}
				else
				{
					shiftGroup.Roles = new List<ShiftGroupRole>();
					dictionaryShiftGroup = shiftGroup;
					dictionary.Add(shiftGroup.ShiftGroupId, shiftGroup);
				}

				return dictionaryShiftGroup;
			});
		}
	}
}
