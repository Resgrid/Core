using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Trainings;

namespace Resgrid.Repositories.DataRepository
{
	public class TrainingAttachmentRepository : RmsRepositoryBase<TrainingAttachment>, ITrainingAttachmentRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public TrainingAttachmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<TrainingAttachment>> GetTrainingAttachmentsByTrainingIdAsync(int trainingId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<TrainingAttachment>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("TrainingId", trainingId);

					var query = _queryFactory.GetQuery<SelectTrainingAttachmentsBytIdQuery>();

					return await x.QueryAsync<TrainingAttachment>(sql: query,
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

		private static string Metadata => $"{Cols("TrainingAttachmentId", "TrainingId", "FileName", "FileType")}, " +
			(IsPostgres ? $"CAST(octet_length({Col("Data")}) AS BIGINT)" : $"CAST(DATALENGTH({Col("Data")}) AS BIGINT)") + " AS FileSize";

		public async Task<List<TrainingAttachment>> GetAttachmentMetadataByTrainingIdAsync(int trainingId)
		{
			return (await QueryAsync<TrainingAttachment>($"SELECT {Metadata} FROM {Tbl("TrainingAttachments")} WHERE {Col("TrainingId")} = {P}TrainingId ORDER BY {Col("TrainingAttachmentId")}",
				new { TrainingId = trainingId })).ToList();
		}

		public Task<TrainingAttachment> GetAttachmentMetadataByIdAsync(int trainingAttachmentId)
		{
			return QueryFirstOrDefaultAsync<TrainingAttachment>($"SELECT {Metadata} FROM {Tbl("TrainingAttachments")} WHERE {Col("TrainingAttachmentId")} = {P}Id",
				new { Id = trainingAttachmentId });
		}

		public Task<TrainingAttachment> GetAttachmentWithDataAsync(int trainingAttachmentId)
		{
			return QueryFirstOrDefaultAsync<TrainingAttachment>($"SELECT * FROM {Tbl("TrainingAttachments")} WHERE {Col("TrainingAttachmentId")} = {P}Id",
				new { Id = trainingAttachmentId });
		}

		public Task<int> CopyAttachmentsAsync(int fromTrainingId, int toTrainingId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"INSERT INTO {Tbl("TrainingAttachments")} ({Cols("TrainingId", "FileName", "FileType", "Data")}) " +
				$"SELECT {P}ToTrainingId, {Cols("FileName", "FileType", "Data")} FROM {Tbl("TrainingAttachments")} WHERE {Col("TrainingId")} = {P}FromTrainingId",
				new { FromTrainingId = fromTrainingId, ToTrainingId = toTrainingId }, cancellationToken);
		}
	}
}
