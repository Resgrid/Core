using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class PoisRepository : RepositoryBase<Poi>, IPoisRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public PoisRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		/// <summary>Ids per statement: PoiTypeId plus this many stays under SQL Server's 2,100-parameter ceiling.</summary>
		public const int DeleteIdChunkSize = 1000;

		private static bool IsPostgres => DataConfig.DatabaseType == DatabaseTypes.Postgres;
		private string P => _sqlConfiguration.ParameterNotation;
		private string Tbl(string name) => IsPostgres ? $"{_sqlConfiguration.SchemaName}.{name.ToLowerInvariant()}" : $"{_sqlConfiguration.SchemaName}.[{name}]";
		private static string Col(string name) => IsPostgres ? name.ToLowerInvariant() : $"[{name}]";

		public async Task<int> DeleteForTypeAsync(int poiTypeId, IReadOnlyCollection<int> poiIds, CancellationToken cancellationToken = default)
		{
			var chunks = (poiIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().Chunk(DeleteIdChunkSize).ToList();
			if (chunks.Count == 0)
				return 0;

			var match = IsPostgres ? $"{Col("PoiId")} = ANY({P}Ids)" : $"{Col("PoiId")} IN {P}Ids";
			var sql = $"DELETE FROM {Tbl("Pois")} WHERE {Col("PoiTypeId")} = {P}PoiTypeId AND {match}";

			// Several chunks are one removal: a failure part-way leaves nothing half-deleted.
			var owns = _unitOfWork.Transaction == null;
			var connection = await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				var deleted = 0;
				foreach (var chunk in chunks)
				{
					var parameters = new DynamicParameters();
					parameters.Add("PoiTypeId", poiTypeId);
					// Npgsql binds an int[] to = ANY; Dapper expands a list into IN (...) for SQL Server.
					parameters.Add("Ids", IsPostgres ? (object)chunk : chunk.ToList());
					deleted += await connection.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken));
				}
				if (owns) _unitOfWork.CommitChanges();
				return deleted;
			}
			catch (Exception ex)
			{
				if (owns) _unitOfWork.DiscardChanges();
				if (!(ex is OperationCanceledException)) Logging.LogException(ex);
				throw;
			}
		}
	}
}
