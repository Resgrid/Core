using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class UserExternalIdentityLinksRepository : RepositoryBase<UserExternalIdentityLink>, IUserExternalIdentityLinksRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public UserExternalIdentityLinksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.userexternalidentitylinks"
				: $"{sqlConfiguration.SchemaName}.[UserExternalIdentityLinks]";
		}

		public Task<UserExternalIdentityLink> GetActiveBySubjectAsync(string departmentSsoConfigId, string externalSubject)
		{
			var where = _isPostgres
				? "departmentssoconfigid = @ConfigId AND externalsubject = @ExternalSubject AND isactive = TRUE"
				: "[DepartmentSsoConfigId] = @ConfigId AND [ExternalSubject] = @ExternalSubject AND [IsActive] = 1";
			return QuerySingleAsync(where, new { ConfigId = departmentSsoConfigId, ExternalSubject = externalSubject });
		}

		public Task<UserExternalIdentityLink> GetActiveByUserAndConfigAsync(string userId, string departmentSsoConfigId)
		{
			var where = _isPostgres
				? "userid = @UserId AND departmentssoconfigid = @ConfigId AND isactive = TRUE"
				: "[UserId] = @UserId AND [DepartmentSsoConfigId] = @ConfigId AND [IsActive] = 1";
			return QuerySingleAsync(where, new { UserId = userId, ConfigId = departmentSsoConfigId });
		}

		public async Task<IReadOnlyList<UserExternalIdentityLink>> GetActiveByUserAsync(string userId)
		{
			var where = _isPostgres ? "userid = @UserId AND isactive = TRUE" : "[UserId] = @UserId AND [IsActive] = 1";
			var links = await WithConnectionAsync(connection => connection.QueryAsync<UserExternalIdentityLink>(
				$"SELECT * FROM {_table} WHERE {where}", new { UserId = userId }, _unitOfWork?.Transaction));
			return links.ToList();
		}

		private Task<UserExternalIdentityLink> QuerySingleAsync(string where, object parameters) =>
			WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<UserExternalIdentityLink>(
				$"SELECT * FROM {_table} WHERE {where}", parameters, _unitOfWork?.Transaction));

		private async Task<TResult> WithConnectionAsync<TResult>(Func<DbConnection, Task<TResult>> operation)
		{
			if (_unitOfWork?.Connection != null)
				return await operation(_unitOfWork.CreateOrGetConnection());

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync();
			return await operation(connection);
		}
	}
}
