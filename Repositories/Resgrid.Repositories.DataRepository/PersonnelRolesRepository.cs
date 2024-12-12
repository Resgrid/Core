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
using Resgrid.Repositories.DataRepository.Queries.PersonnelRoles;

namespace Resgrid.Repositories.DataRepository
{
	public class PersonnelRolesRepository : RepositoryBase<PersonnelRole>, IPersonnelRolesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public PersonnelRolesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<PersonnelRole> GetRoleByDepartmentAndNameAsync(int departmentId, string name)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<PersonnelRole>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("Name", name);

					var query = _queryFactory.GetQuery<SelectRoleByDidAndNameQuery>();

					return await x.QueryFirstOrDefaultAsync<PersonnelRole>(sql: query,
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

		public async Task<IEnumerable<PersonnelRole>> GetRolesForUserAsync(int departmentId, string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<PersonnelRole>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectRolesByDidAndUserQuery>();

					return await x.QueryAsync<PersonnelRole>(sql: query,
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

		public async Task<PersonnelRole> GetRoleByRoleIdAsync(int personnelRoleId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<PersonnelRole>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("RoleId", personnelRoleId);

					var query = _queryFactory.GetQuery<SelectRolesByRoleIdQuery>();

					var dictionary = new Dictionary<int, PersonnelRole>();
					var result = await x.QueryAsync<PersonnelRole, PersonnelRoleUser, PersonnelRole>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: PersonnelRoleUserMapping(dictionary),
						splitOn: "PersonnelRoleUserId");

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

				throw;
			}
		}

		public async Task<IEnumerable<PersonnelRole>> GetPersonnelRolesByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<PersonnelRole>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectRolesByDidQuery>();

					var dictionary = new Dictionary<int, PersonnelRole>();
					var result = await x.QueryAsync<PersonnelRole, PersonnelRoleUser, PersonnelRole>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: PersonnelRoleUserMapping(dictionary),
						splitOn: "PersonnelRoleUserId");

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

		private static Func<PersonnelRole, PersonnelRoleUser, PersonnelRole> PersonnelRoleUserMapping(Dictionary<int, PersonnelRole> dictionary)
		{
			return new Func<PersonnelRole, PersonnelRoleUser, PersonnelRole>((role, roleUser) =>
			{
				var dictionaryRole = default(PersonnelRole);

				if (roleUser != null)
				{
					if (dictionary.TryGetValue(role.PersonnelRoleId, out dictionaryRole))
					{
						if (dictionaryRole.Users.All(x => x.PersonnelRoleUserId != roleUser.PersonnelRoleUserId))
							dictionaryRole.Users.Add(roleUser);
					}
					else
					{
						if (role.Users == null)
							role.Users = new List<PersonnelRoleUser>();

						role.Users.Add(roleUser);
						dictionary.Add(role.PersonnelRoleId, role);

						dictionaryRole = role;
					}
				}
				else
				{
					role.Users = new List<PersonnelRoleUser>();
					dictionaryRole = role;
					dictionary.Add(role.PersonnelRoleId, role);
				}

				return dictionaryRole;
			});
		}
	}
}
