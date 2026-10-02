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
using Resgrid.Repositories.DataRepository.Queries.Sso;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentSsoConfigRepository : AuditedConfigurationRepository<DepartmentSsoConfig>, IDepartmentSsoConfigRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentSsoConfigRepository(
			IConnectionProvider connectionProvider,
			SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork,
			IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<DepartmentSsoConfig>> GetAllByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentSsoConfig>>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					var query = _queryFactory.GetQuery<SelectSsoConfigsByDepartmentIdQuery>();
					return await x.QueryAsync<DepartmentSsoConfig>(sql: query, param: dp, transaction: _unitOfWork?.Transaction);
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

				conn = _unitOfWork.CreateOrGetConnection();
				return await selectFunction(conn);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		public async Task<DepartmentSsoConfig> GetByDepartmentIdAndTypeAsync(int departmentId, SsoProviderType providerType)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentSsoConfig>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					dp.Add("SsoProviderType", (int)providerType);
					var query = _queryFactory.GetQuery<SelectSsoConfigByDepartmentIdAndTypeQuery>();
					return await x.QueryFirstOrDefaultAsync<DepartmentSsoConfig>(sql: query, param: dp, transaction: _unitOfWork?.Transaction);
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

				conn = _unitOfWork.CreateOrGetConnection();
				return await selectFunction(conn);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		public async Task<DepartmentSsoConfig> GetByEntityIdAsync(string entityId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentSsoConfig>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("EntityId", entityId);
					var query = _queryFactory.GetQuery<SelectSsoConfigByEntityIdQuery>();
					return await x.QueryFirstOrDefaultAsync<DepartmentSsoConfig>(sql: query, param: dp, transaction: _unitOfWork?.Transaction);
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

				conn = _unitOfWork.CreateOrGetConnection();
				return await selectFunction(conn);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}

		public async Task<long> AdvanceFederatedMfaMappingVersionAsync(string departmentSsoConfigId,
			System.Threading.CancellationToken cancellationToken = default)
		{
			// One statement advances the version and clears the test, so no reader sees a new mapping as tested.
			var postgres = Resgrid.Config.DataConfig.DatabaseType == Resgrid.Config.DatabaseTypes.Postgres;
			var sql = postgres
				? $@"UPDATE {_sqlConfiguration.SchemaName}.departmentssoconfigs SET federatedmfamappingversion = federatedmfamappingversion + 1,
					federatedmfatestedversion = NULL, federatedmfatestedonutc = NULL, federatedmfatestedbyuserid = NULL
					WHERE departmentssoconfigid = @Id RETURNING federatedmfamappingversion"
				: $@"UPDATE {_sqlConfiguration.SchemaName}.[DepartmentSsoConfigs] SET [FederatedMfaMappingVersion] = [FederatedMfaMappingVersion] + 1,
					[FederatedMfaTestedVersion] = NULL, [FederatedMfaTestedOnUtc] = NULL, [FederatedMfaTestedByUserId] = NULL
					OUTPUT INSERTED.[FederatedMfaMappingVersion] WHERE [DepartmentSsoConfigId] = @Id";

			await using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QueryFirstOrDefaultAsync<long>(new Dapper.CommandDefinition(sql, new { Id = departmentSsoConfigId },
				cancellationToken: cancellationToken));
		}

		public async Task<bool> TryRecordFederatedMfaTestAsync(string departmentSsoConfigId, long version, string userId, DateTime utcNow,
			System.Threading.CancellationToken cancellationToken = default)
		{
			var postgres = Resgrid.Config.DataConfig.DatabaseType == Resgrid.Config.DatabaseTypes.Postgres;
			var sql = postgres
				? $@"UPDATE {_sqlConfiguration.SchemaName}.departmentssoconfigs SET federatedmfatestedversion = @Version, federatedmfatestedonutc = @Now,
					federatedmfatestedbyuserid = @UserId WHERE departmentssoconfigid = @Id AND federatedmfamappingversion = @Version"
				: $@"UPDATE {_sqlConfiguration.SchemaName}.[DepartmentSsoConfigs] SET [FederatedMfaTestedVersion] = @Version, [FederatedMfaTestedOnUtc] = @Now,
					[FederatedMfaTestedByUserId] = @UserId WHERE [DepartmentSsoConfigId] = @Id AND [FederatedMfaMappingVersion] = @Version";

			await using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new Dapper.CommandDefinition(sql, new
			{
				Id = departmentSsoConfigId,
				Version = version,
				UserId = userId,
				Now = postgres ? DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified) : utcNow
			}, cancellationToken: cancellationToken)) == 1;
		}
	}
}
