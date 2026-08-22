using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
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

		public async Task<bool> DeleteRoleDependenciesAsync(int personnelRoleId, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				var dynamicParameters = new DynamicParametersExtension();
				dynamicParameters.Add("RoleId", personnelRoleId);

				var notation = _sqlConfiguration.ParameterNotation;
				var schema = _sqlConfiguration.SchemaName;

				// Every table below points at PersonnelRoles. CallDispatchRoles is the one with a
				// non-cascading FK, so leaving any of these behind blocks the role delete outright.
				// UnitRoles only names the role as an optional qualification for a seat, so the
				// requirement is cleared rather than the seat being deleted.
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $@"DELETE FROM {schema}.personnelroleusers WHERE personnelroleid = {notation}RoleId;
						 DELETE FROM {schema}.calldispatchroles WHERE roleid = {notation}RoleId;
						 DELETE FROM {schema}.shiftgrouproles WHERE personnelroleid = {notation}RoleId;
						 DELETE FROM {schema}.commanddefinitionrolepersonnelroles WHERE personnelroleid = {notation}RoleId;
						 DELETE FROM {schema}.runcardrolerequirements WHERE personnelroleid = {notation}RoleId;
						 DELETE FROM {schema}.stationcoveragerequirements WHERE personnelroleid = {notation}RoleId;
						 DELETE FROM {schema}.chatchannelaccessrules WHERE personnelroleid = {notation}RoleId;
						 UPDATE {schema}.unitroles SET personnelroleid = NULL, personnelrolerequired = false WHERE personnelroleid = {notation}RoleId;"
					: $@"DELETE FROM {schema}.[PersonnelRoleUsers] WHERE [PersonnelRoleId] = {notation}RoleId;
						 DELETE FROM {schema}.[CallDispatchRoles] WHERE [RoleId] = {notation}RoleId;
						 DELETE FROM {schema}.[ShiftGroupRoles] WHERE [PersonnelRoleId] = {notation}RoleId;
						 DELETE FROM {schema}.[CommandDefinitionRolePersonnelRoles] WHERE [PersonnelRoleId] = {notation}RoleId;
						 DELETE FROM {schema}.[RunCardRoleRequirements] WHERE [PersonnelRoleId] = {notation}RoleId;
						 DELETE FROM {schema}.[StationCoverageRequirements] WHERE [PersonnelRoleId] = {notation}RoleId;
						 DELETE FROM {schema}.[ChatChannelAccessRules] WHERE [PersonnelRoleId] = {notation}RoleId;
						 UPDATE {schema}.[UnitRoles] SET [PersonnelRoleId] = NULL, [PersonnelRoleRequired] = 0 WHERE [PersonnelRoleId] = {notation}RoleId;";

				var executeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					await x.ExecuteAsync(sql, dynamicParameters, _unitOfWork.Transaction);

					return true;
				});

				if (_unitOfWork?.Connection == null)
				{
					using (var conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await executeFunction(conn);
					}
				}

				return await executeFunction(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
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
