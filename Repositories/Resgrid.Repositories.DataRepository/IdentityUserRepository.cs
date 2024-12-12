using Dapper;
using Microsoft.AspNetCore.Identity;
using Resgrid.Framework;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Identity.User;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IdentityRole = Resgrid.Model.Identity.IdentityRole;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Repositories.DataRepository
{
	public class IdentityUserRepository: IIdentityUserRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IIdentityRoleRepository _roleRepository;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IQueryFactory _queryFactory;
		
		public IdentityUserRepository(IConnectionProvider connProv,
							  SqlConfiguration sqlConf,
							  IIdentityRoleRepository roleRepo,
							  IUnitOfWork uow,
							  IQueryFactory queryFactory)
		{
			_connectionProvider = connProv;
			_sqlConfiguration = sqlConf;
			_roleRepository = roleRepo;
			_unitOfWork = uow;
			_queryFactory = queryFactory;
		}

		public Task<IEnumerable<IdentityUser>> GetAllAsync() => throw new NotImplementedException();

		public async Task<IdentityUser> GetByEmailAsync(string email)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IdentityUser>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Email", email);

					var query = _queryFactory.GetQuery<SelectUserByEmailQuery>();

					var userDictionary = new Dictionary<string, IdentityUser>();
					var result = await x.QueryAsync<IdentityUser, IdentityUserRole, IdentityUser>(sql: query,
																			 param: dynamicParameters,
																			 transaction: _unitOfWork.Transaction,
																			 map: UserRoleMapping(userDictionary)/*,
						splitOn: "Id"*/);

					if (userDictionary.Count > 0)
						return userDictionary.FirstOrDefault().Value;

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

		public async Task<IdentityUser> GetByIdAsync(string id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IdentityUser>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Id", id);

					var query = _queryFactory.GetQuery<SelectUserByIdQuery>();

					var userDictionary = new Dictionary<string, IdentityUser>();
					var result = await x.QueryAsync<IdentityUser, IdentityUserRole, IdentityUser>(sql: query,
																			 param: dynamicParameters,
																			 transaction: _unitOfWork.Transaction,
																			 map: UserRoleMapping(userDictionary)/*,
						splitOn: "Id"*/);

					if (userDictionary.Count > 0)
						return userDictionary.FirstOrDefault().Value;

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

		public async Task<IdentityUser> GetByUserNameAsync(string userName)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IdentityUser>>(async x =>
				{
					try
					{
						var dynamicParameters = new DynamicParametersExtension();
						dynamicParameters.Add("UserName", userName);

						var query = _queryFactory.GetQuery<SelectUserByUserNameQuery>();

						var userDictionary = new Dictionary<string, IdentityUser>();
						var result = await x.QueryAsync(sql: query,
														param: dynamicParameters,
														transaction: _unitOfWork.Transaction,
														map: UserRoleMapping(userDictionary)/*,
						splitOn: "Id"*/);

						if (userDictionary.Count > 0)
							return userDictionary.FirstOrDefault().Value;

						return result.FirstOrDefault();
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						return null;
					}
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

		private static Func<IdentityUser, IdentityUserRole, IdentityUser> UserRoleMapping(Dictionary<string, IdentityUser> userDictionary)
		{
			return new Func<IdentityUser, IdentityUserRole, IdentityUser>((user, role) =>
			{
				var dictionaryUser = default(IdentityUser);

				if (role != null)
				{
					if (userDictionary.TryGetValue(user.Id, out dictionaryUser))
					{
						dictionaryUser.Roles.Add(role);
					}
					else
					{
						user.Roles.Add(role);
						userDictionary.Add(user.Id, user);

						dictionaryUser = user;
					}
				}
				else
				{
					dictionaryUser = user;
					userDictionary.Add(user.Id, user);
				}

				return dictionaryUser;
			});
		}

		public async Task<string> InsertAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			try
			{
				var insertFunction = new Func<DbConnection, Task<string>>(async x =>
				{
					try
					{
						var dynamicParameters = new DynamicParameters(user);

						var query = _queryFactory.GetInsertQuery<InsertUserQuery, IdentityUser>(user);

						var result = await x.ExecuteScalarAsync<string>(query, dynamicParameters, _unitOfWork.Transaction);

						return result;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<bool> InsertClaimsAsync(string id, IEnumerable<Claim> claims, CancellationToken cancellationToken)
		{
			try
			{
				var insertFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var resultList = new List<bool>(claims.Count());
						foreach (var claim in claims)
						{
							var userClaim = Activator.CreateInstance<IdentityUserClaim>();
							userClaim.UserId = id;
							userClaim.ClaimType = claim.Type;
							userClaim.ClaimValue = claim.Value;

							var query = _queryFactory.GetInsertQuery<InsertUserClaimQuery, IdentityUserClaim>(userClaim);

							resultList.Add(await x.ExecuteAsync(query, userClaim, _unitOfWork.Transaction) > 0);
						}

						return resultList.TrueForAll(y => y);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<bool> InsertLoginInfoAsync(string id, UserLoginInfo loginInfo, CancellationToken cancellationToken)
		{
			try
			{
				var insertFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						IdentityUserLoginInfo userLogin = new IdentityUserLoginInfo()
						{
							UserId = id,
							LoginProvider = loginInfo.LoginProvider,
							ProviderKey = loginInfo.ProviderKey,
							ProviderDisplayName = loginInfo.ProviderDisplayName
						};

						var query = (string)_queryFactory.GetInsertQuery<InsertUserLoginQuery, IdentityUserLoginInfo>(userLogin);

						var result = await x.ExecuteAsync(query, (object)userLogin, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<bool> AddToRoleAsync(string id, string roleName, CancellationToken cancellationToken)
		{
			try
			{
				var insertFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var role = await _roleRepository.GetByNameAsync(roleName);
						if (role == null)
							return false;

						var userRole = Activator.CreateInstance<IdentityUserRole>();
						userRole.RoleId = role.Id;
						userRole.UserId = id;

						var query = _queryFactory.GetInsertQuery<InsertUserRoleQuery, IdentityUserRole>(userRole);

						var result = await x.ExecuteAsync(query, userRole, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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
					try
					{
						var dynamicParameters = new DynamicParametersExtension();
						dynamicParameters.Add("Id", id);

						var query = _queryFactory.GetDeleteQuery<DeleteUserQuery>();

						var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<bool> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			try
			{
				var updateFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var dynamicParameters = new DynamicParameters(user);

						var query = _queryFactory.GetUpdateQuery<UpdateUserQuery, IdentityUser>(user);

						var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<IdentityUser> GetByUserLoginAsync(string loginProvider, string providerKey)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IdentityUser>>(async x =>
				{
					try
					{
						var query = _queryFactory.GetQuery<GetUserLoginByLoginProviderAndProviderKeyQuery, IdentityUser>();

						var userDictionary = new Dictionary<string, IdentityUser>();
						var result = await x.QueryAsync(sql: query,
														param: new
														{
															LoginProvider = loginProvider,
															ProviderKey = providerKey
														},
														transaction: _unitOfWork.Transaction,
														map: UserRoleMapping(userDictionary)/*,
						splitOn: "Id"*/);

						if (userDictionary.Count > 0)
							return userDictionary.FirstOrDefault().Value;

						return result.FirstOrDefault();
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<IList<Claim>> GetClaimsByUserIdAsync(string id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IList<Claim>>>(async x =>
				{
					var query = _queryFactory.GetQuery<GetClaimsByUserIdQuery>();

					var result = await x.QueryAsync(query, new { UserId = id }, _unitOfWork.Transaction);

					return result?.Select(y => new Claim(y.ClaimType, y.ClaimValue))
								  .ToList();
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

		public async Task<IList<string>> GetRolesByUserIdAsync(string id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IList<string>>>(async x =>
				{
					var query = _queryFactory.GetQuery<GetRolesByUserIdQuery>();

					var result = await x.QueryAsync<string>(query, new { UserId = id }, _unitOfWork.Transaction);

					return result.ToList();
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

		public async Task<IList<UserLoginInfo>> GetUserLoginInfoByIdAsync(string id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IList<UserLoginInfo>>>(async x =>
				{
					var query = _queryFactory.GetQuery<GetUserLoginInfoByIdQuery>();

					var result = await x.QueryAsync(query, new { UserId = id }, _unitOfWork.Transaction);

					return result?.Select(y => new UserLoginInfo(y.LoginProvider, y.ProviderKey, y.Name))
								  .ToList();
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

		public async Task<IList<IdentityUser>> GetUsersByClaimAsync(Claim claim)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IList<IdentityUser>>>(async x =>
				{
					var query = _queryFactory.GetQuery<GetUsersByClaimQuery, IdentityUser>();

					var result = await x.QueryAsync<IdentityUser>(sql: query,
														   param: new
														   {
															   ClaimValue = claim.Value,
															   ClaimType = claim.Type
														   },
														   transaction: _unitOfWork.Transaction);

					return result.ToList();
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

		public async Task<IList<IdentityUser>> GetUsersInRoleAsync(string roleName)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IList<IdentityUser>>>(async x =>
				{
					var query = _queryFactory.GetQuery<GetUsersInRoleQuery, IdentityUser>();

					var result = await x.QueryAsync<IdentityUser>(sql: query,
														   param: new
														   {
															   RoleName = roleName
														   },
														   transaction: _unitOfWork.Transaction);

					return result.ToList();
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

		public async Task<bool> IsInRoleAsync(string id, string roleName)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var query = _queryFactory.GetQuery<IsInRoleQuery, IdentityUser>();

					var result = await x.QueryAsync(sql: query,
													param: new
													{
														RoleName = roleName,
														UserId = id
													},
													transaction: _unitOfWork.Transaction);

					return result.Count() > 0;
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

		public async Task<bool> RemoveClaimsAsync(string id, IEnumerable<Claim> claims, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var query = _queryFactory.GetDeleteQuery<RemoveClaimsQuery>();

						var resultList = new List<bool>(claims.Count());
						foreach (var claim in claims)
						{
							resultList.Add(await x.ExecuteAsync(query, new
							{
								UserId = id,
								ClaimValue = claim.Value,
								ClaimType = claim.Type
							}, _unitOfWork.Transaction) > 0);
						}

						return resultList.TrueForAll(y => y);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<bool> RemoveFromRoleAsync(string id, string roleName, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var query = _queryFactory.GetDeleteQuery<RemoveUserFromRoleQuery>();

						var result = await x.ExecuteAsync(query, new
						{
							UserId = id,
							RoleName = roleName
						}, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<bool> RemoveLoginAsync(string id, string loginProvider, string providerKey, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var query = _queryFactory.GetDeleteQuery<RemoveLoginForUserQuery>();

						var result = await x.ExecuteAsync(query, new
						{
							UserId = id,
							LoginProvider = loginProvider,
							ProviderKey = providerKey
						}, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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

		public async Task<bool> UpdateClaimAsync(string id, Claim oldClaim, Claim newClaim, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						//I don't need to make a new GetUpdateQuery that don't pass a TEntity object just for use
						//on this method, so I'll just pass null because i don't use the TEntity object on this method
						var query = _queryFactory.GetUpdateQuery<UpdateClaimForUserQuery, IdentityUserClaim>(null);

						var result = await x.ExecuteAsync(query, new
						{
							NewClaimType = newClaim.Type,
							NewClaimValue = newClaim.Value,
							UserId = id,
							ClaimType = oldClaim.Type,
							ClaimValue = oldClaim.Value
						}, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
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
	}
}
