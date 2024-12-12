using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.DepartmentSettings;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentSettingsRepository : RepositoryBase<DepartmentSetting>, IDepartmentSettingsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentSettingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<DepartmentSetting> GetDepartmentSettingByUserIdTypeAsync(string userId, DepartmentSettingTypes type)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentSetting>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("SettingType", (int)type);

					var query = _queryFactory.GetQuery<SelectBySettingTypeUserIdQuery>();

					return await x.QueryFirstOrDefaultAsync<DepartmentSetting>(sql: query,
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

			return null;
		}

		public async Task<DepartmentSetting> GetDepartmentSettingByIdTypeAsync(int departmentId, DepartmentSettingTypes type)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentSetting>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("SettingType", (int)type);

					var query = _queryFactory.GetQuery<SelectBySettingTypeDepartmentIdQuery>();

					return await x.QueryFirstOrDefaultAsync<DepartmentSetting>(sql: query,
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

		public async Task<DepartmentSetting> GetDepartmentSettingBySettingTypeAsync(string setting, DepartmentSettingTypes type)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentSetting>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Setting", setting);
					dynamicParameters.Add("SettingType", (int)type);

					var query = _queryFactory.GetQuery<SelectBySettingAndTypeQuery>();

					return await x.QueryFirstOrDefaultAsync<DepartmentSetting>(sql: query,
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

		public async Task<List<DepartmentManagerInfo>> GetAllDepartmentManagerInfoAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<List<DepartmentManagerInfo>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					var query = _queryFactory.GetQuery<SelectAllManagerInfoQuery>();

					return await x.QueryFirstOrDefaultAsync<List<DepartmentManagerInfo>>(sql: query,
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

		public async Task<DepartmentManagerInfo> GetDepartmentManagerInfoByEmailAsync(string emailAddress)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentManagerInfo>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("EmailAddress", emailAddress);

					var query = _queryFactory.GetQuery<SelectManagerInfoByEmailQuery>();

					return await x.QueryFirstOrDefaultAsync<DepartmentManagerInfo>(sql: query,
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
	}
}
