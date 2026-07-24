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
using Resgrid.Repositories.DataRepository.Queries.UnitTracking;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitTrackingCredentialsRepository : RepositoryBase<UnitTrackingCredential>, IUnitTrackingCredentialsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UnitTrackingCredentialsRepository(
			IConnectionProvider connectionProvider,
			SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork,
			IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<UnitTrackingCredential>> GetAllByDeviceIdAsync(string unitTrackingDeviceId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("UnitTrackingDeviceId", unitTrackingDeviceId);
				var query = _queryFactory.GetQuery<SelectUnitTrackingCredentialsByDeviceQuery>();

				return await WithConnectionAsync(connection =>
					connection.QueryAsync<UnitTrackingCredential>(query, parameters, _unitOfWork.Transaction));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<UnitTrackingCredential> GetBySecretHashAsync(string secretHash)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("SecretHash", secretHash);
				var query = _queryFactory.GetQuery<SelectUnitTrackingCredentialBySecretHashQuery>();

				return await WithConnectionAsync(connection =>
					connection.QuerySingleOrDefaultAsync<UnitTrackingCredential>(
						query,
						parameters,
						_unitOfWork.Transaction));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		private async Task<TResult> WithConnectionAsync<TResult>(Func<DbConnection, Task<TResult>> action)
		{
			if (_unitOfWork?.Connection != null)
				return await action(_unitOfWork.CreateOrGetConnection());

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync();
			return await action(connection);
		}
	}
}
