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
	public class UnitTrackingDevicesRepository : RepositoryBase<UnitTrackingDevice>, IUnitTrackingDevicesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UnitTrackingDevicesRepository(
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

		public async Task<IEnumerable<UnitTrackingDevice>> GetAllByUnitIdAsync(int departmentId, int unitId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("UnitId", unitId);
				var query = _queryFactory.GetQuery<SelectUnitTrackingDevicesByUnitQuery>();

				return await WithConnectionAsync(connection =>
					connection.QueryAsync<UnitTrackingDevice>(query, parameters, _unitOfWork.Transaction));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<UnitTrackingDevice> GetByProtocolIdentifierAsync(string protocolKey, string deviceIdentifier)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ProtocolKey", protocolKey);
				parameters.Add("DeviceIdentifier", deviceIdentifier);
				var query = _queryFactory.GetQuery<SelectUnitTrackingDeviceByProtocolIdentifierQuery>();

				return await WithConnectionAsync(connection =>
					connection.QuerySingleOrDefaultAsync<UnitTrackingDevice>(
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
