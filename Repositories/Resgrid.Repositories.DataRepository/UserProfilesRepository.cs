using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.UserProfiles;

namespace Resgrid.Repositories.DataRepository
{
	public class UserProfilesRepository : RepositoryBase<UserProfile>, IUserProfilesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UserProfilesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<UserProfile> GetProfileByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UserProfile>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectProfileByUserIdQuery>();

					var result = await x.QueryAsync<UserProfile, IdentityUser, UserProfile>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);

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

		public async Task<UserProfile> GetProfileByMobileNumberAsync(string mobileNumber)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UserProfile>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("MobileNumber", mobileNumber);

					var query = _queryFactory.GetQuery<SelectProfileByMobileQuery>();

					var result = await x.QueryAsync<UserProfile, IdentityUser, UserProfile>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }//,
						/*splitOn: "Id"*/);

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

		public async Task<UserProfile> GetProfileByHomeNumberAsync(string homeNumber)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UserProfile>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("HomeNumber", homeNumber);

					var query = _queryFactory.GetQuery<SelectProfileByHomeQuery>();

					var result = await x.QueryAsync<UserProfile, IdentityUser, UserProfile>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);

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

		public async Task<IEnumerable<UserProfile>> GetAllUserProfilesForDepartmentAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UserProfile>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectAllNonDeletedProfilesByDIdQuery>();

					var dictionary = new Dictionary<int, UserProfile>();
					var result = await x.QueryAsync<UserProfile, IdentityUser, UserProfile>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: UserProfileMapping(dictionary)/*,
						splitOn: "Id"*/);

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



		public async Task<IEnumerable<UserProfile>> GetAllUserProfilesForDepartmentIncDisabledDeletedAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UserProfile>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectAllProfilesByDIdQuery>();

					var dictionary = new Dictionary<int, UserProfile>();
					var result = await x.QueryAsync<UserProfile, IdentityUser, UserProfile>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: UserProfileMapping(dictionary)/*,
						splitOn: "Id"*/);

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

		public async Task<IEnumerable<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UserProfile>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					var usersToQuery = String.Join(",", userIds.Select(p => $"'{p.ToString()}'").ToArray());
					dynamicParameters.Add("UserIds", usersToQuery);

					var query = _queryFactory.GetQuery<SelectProfilesByIdsQuery>();
					query = query.Replace("@UserIds", usersToQuery, StringComparison.InvariantCultureIgnoreCase);

					var dictionary = new Dictionary<int, UserProfile>();
					var result = await x.QueryAsync<UserProfile, IdentityUser, UserProfile>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: UserProfileMapping(dictionary)/*,
						splitOn: "Id"*/);

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

		private static Func<UserProfile, IdentityUser, UserProfile> UserProfileMapping(Dictionary<int, UserProfile> dictionary)
		{
			return new Func<UserProfile, IdentityUser, UserProfile>((userProfile, identityUser) =>
			{
				var dictionaryProfile = default(UserProfile);

				if (identityUser != null)
				{
					if (dictionary.TryGetValue(userProfile.UserProfileId, out dictionaryProfile))
					{
						if (dictionaryProfile.UserId == identityUser.Id)
							dictionaryProfile.User = identityUser;
					}
					else
					{
						if (userProfile.UserId == identityUser.Id)
							userProfile.User = identityUser;

						dictionary.Add(userProfile.UserProfileId, userProfile);

						dictionaryProfile = userProfile;
					}
				}
				else
				{
					dictionaryProfile = userProfile;
					dictionary.Add(userProfile.UserProfileId, userProfile);
				}

				return dictionaryProfile;
			});
		}
	}
}
