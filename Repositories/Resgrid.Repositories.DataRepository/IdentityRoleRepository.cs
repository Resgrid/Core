using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using Dapper;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories;
using Resgrid.Model.Identity;
using System.Security.Claims;
using Resgrid.Repositories.DataRepository.Queries.Identity.Role;
using Resgrid.Framework;

namespace Resgrid.Repositories.DataRepository
{
	public class IdentityRoleRepository: IIdentityRoleRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public IdentityRoleRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IdentityRole> GetByIdAsync(string id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IdentityRole>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("IdValue", id);

					var query = _queryFactory.GetQuery<SelectByIdQuery>();

					return await x.QueryFirstOrDefaultAsync<IdentityRole>(sql: query,
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

		public async Task<IdentityRole> GetByNameAsync(string roleName)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IdentityRole>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Name", roleName);

					var query = _queryFactory.GetQuery<SelectRoleByNameQuery>();

					return await x.QueryFirstOrDefaultAsync<IdentityRole>(sql: query,
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

		public async Task<IEnumerable<IdentityRoleClaim>> GetClaimsByRole(IdentityRole role, CancellationToken cancellationToken)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<IdentityRoleClaim>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("RoleId", role.Id);

					var query = _queryFactory.GetQuery<GetClaimsByRoleQuery>();

					return await x.QueryAsync<IdentityRoleClaim>(sql: query,
														  param: dynamicParameters,
														  transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

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

		public async Task<bool> InsertAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			try
			{
				var insertFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParameters(role);

					var query = _queryFactory.GetInsertQuery<InsertQuery, IdentityRole>(role);

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					return result > 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await insertFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await insertFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<bool> InsertClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken)
		{
			try
			{
				var insertFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var roleClaim = Activator.CreateInstance<IdentityRoleClaim>();
					roleClaim.ClaimType = claim.Type;
					roleClaim.ClaimValue = claim.Value;
					roleClaim.RoleId = role.Id;

					var dynamicParameters = new DynamicParameters(roleClaim);

					var query = _queryFactory.GetInsertQuery<InsertRoleClaimQuery, IdentityRoleClaim>(roleClaim);

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					return result > 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await insertFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await insertFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("IdValue", id);

					var query = _queryFactory.GetDeleteQuery<DeleteQuery>();

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					return result > 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await removeFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await removeFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<bool> RemoveClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("RoleId", role.Id);
					dynamicParameters.Add("ClaimType", claim.Type);
					dynamicParameters.Add("ClaimValue", claim.Value);

					var query = _queryFactory.GetDeleteQuery<DeleteRoleClaimQuery>();

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					return result > 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await removeFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await removeFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<bool> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			try
			{
				var updateFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParameters(role);

					var query = _queryFactory.GetUpdateQuery<UpdateQuery, IdentityRole>(role);

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					return result > 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);
						return await updateFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await updateFunction(conn);
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
