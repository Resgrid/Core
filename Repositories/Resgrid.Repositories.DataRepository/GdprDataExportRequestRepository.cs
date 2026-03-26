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

namespace Resgrid.Repositories.DataRepository
{
	public class GdprDataExportRequestRepository : RepositoryBase<GdprDataExportRequest>, IGdprDataExportRequestRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public GdprDataExportRequestRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<GdprDataExportRequest>> GetPendingRequestsAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<GdprDataExportRequest>>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("Status", (int)GdprExportStatus.Pending);

					return await x.QueryAsync<GdprDataExportRequest>(
						sql: "SELECT GdprDataExportRequestId, UserId, DepartmentId, Status, RequestedOn, ProcessingStartedOn, CompletedOn, DownloadToken, TokenExpiresAt, FileSizeBytes, ErrorMessage FROM GdprDataExportRequests WHERE Status = @Status",
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

		public async Task<GdprDataExportRequest> GetByTokenAsync(string token)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<GdprDataExportRequest>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("Token", token);

					return await x.QueryFirstOrDefaultAsync<GdprDataExportRequest>(
						sql: "SELECT * FROM GdprDataExportRequests WHERE DownloadToken = @Token",
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

		public async Task<GdprDataExportRequest> GetActiveRequestByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<GdprDataExportRequest>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("PendingStatus", (int)GdprExportStatus.Pending);
					dynamicParameters.Add("ProcessingStatus", (int)GdprExportStatus.Processing);

					return await x.QueryFirstOrDefaultAsync<GdprDataExportRequest>(
						sql: "SELECT GdprDataExportRequestId, UserId, DepartmentId, Status, RequestedOn, ProcessingStartedOn, CompletedOn, DownloadToken, TokenExpiresAt, FileSizeBytes, ErrorMessage FROM GdprDataExportRequests WHERE UserId = @UserId AND Status IN (@PendingStatus, @ProcessingStatus)",
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

		public async Task<IEnumerable<GdprDataExportRequest>> GetExpiredRequestsAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<GdprDataExportRequest>>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("CompletedStatus", (int)GdprExportStatus.Completed);
					dynamicParameters.Add("Now", DateTime.UtcNow);

					return await x.QueryAsync<GdprDataExportRequest>(
						sql: "SELECT GdprDataExportRequestId, UserId, DepartmentId, Status, RequestedOn, ProcessingStartedOn, CompletedOn, DownloadToken, TokenExpiresAt, FileSizeBytes, ErrorMessage FROM GdprDataExportRequests WHERE Status = @CompletedStatus AND TokenExpiresAt < @Now",
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
