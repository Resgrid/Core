using System;
using System.Collections.Generic;
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
	public class DocumentRepository : RepositoryBase<Document>, IDocumentRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DocumentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<Document>> GetRecentByDepartmentIdAsync(int departmentId, int take, bool includeAdminsOnly, string category = null,
			CancellationToken cancellationToken = default)
		{
			var filterCategory = !string.IsNullOrWhiteSpace(category);
			var sql = BuildRecentSql(DataConfig.DatabaseType == DatabaseTypes.Postgres, _sqlConfiguration.SchemaName, includeAdminsOnly, filterCategory);
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Take", Math.Clamp(take, 1, 100));
			if (filterCategory)
				parameters.Add("Category", category);

			try
			{
				if (_unitOfWork?.Connection != null)
					return await _unitOfWork.CreateOrGetConnection().QueryAsync<Document>(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction,
						cancellationToken: cancellationToken));

				await using var connection = _connectionProvider.Create();
				await connection.OpenAsync(cancellationToken);
				return await connection.QueryAsync<Document>(new Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		/// <summary>
		/// The recent-documents query for each dialect. Metadata columns only: Data (the file), Description and Filename
		/// (ADP-catalogued) are never selected.
		/// </summary>
		public static string BuildRecentSql(bool postgres, string schema, bool includeAdminsOnly, bool filterCategory)
		{
			if (postgres)
			{
				return "SELECT documentid, departmentid, name, category, adminsonly, type, userid, addedon, removeon, isprotected " +
					$"FROM {schema}.documents WHERE departmentid = @DepartmentId" +
					(includeAdminsOnly ? string.Empty : " AND adminsonly = false") +
					(filterCategory ? " AND category = @Category::citext" : string.Empty) +
					" ORDER BY addedon DESC, documentid DESC LIMIT @Take";
			}

			return "SELECT TOP (@Take) [DocumentId], [DepartmentId], [Name], [Category], [AdminsOnly], [Type], [UserId], [AddedOn], [RemoveOn], [IsProtected] " +
				$"FROM {schema}.[Documents] WHERE [DepartmentId] = @DepartmentId" +
				(includeAdminsOnly ? string.Empty : " AND [AdminsOnly] = 0") +
				(filterCategory ? " AND [Category] = @Category" : string.Empty) +
				" ORDER BY [AddedOn] DESC, [DocumentId] DESC";
		}
	}
}
