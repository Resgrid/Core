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
using Resgrid.Repositories.DataRepository.Queries.Protocols;

namespace Resgrid.Repositories.DataRepository
{
	public class DispatchProtocolAttachmentRepository : AuditedConfigurationRepository<DispatchProtocolAttachment>, IDispatchProtocolAttachmentRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DispatchProtocolAttachmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		/// <summary>Columns of an attachment without its bytes; the size is computed in the database.</summary>
		private static string InfoColumns(string alias) =>
			$"{alias}.{Col("DispatchProtocolAttachmentId")}, {alias}.{Col("DispatchProtocolId")}, {alias}.{Col("FileName")}, {alias}.{Col("FileType")}, " +
			(IsPostgres ? $"octet_length({alias}.data) AS size" : $"CAST(DATALENGTH({alias}.[Data]) AS bigint) AS [Size]");

		public Task<IEnumerable<DispatchProtocolAttachment>> GetAttachmentInfoByProtocolIdAsync(int protocolId)
		{
			return QueryAsync<DispatchProtocolAttachment>(
				$"SELECT {InfoColumns("a")} FROM {Tbl("DispatchProtocolAttachments")} a WHERE a.{Col("DispatchProtocolId")} = {P}ProtocolId ORDER BY a.{Col("DispatchProtocolAttachmentId")}",
				new { ProtocolId = protocolId });
		}

		public Task<IEnumerable<DispatchProtocolAttachment>> GetAttachmentInfoByDepartmentIdAsync(int departmentId)
		{
			return QueryAsync<DispatchProtocolAttachment>(
				$"SELECT {InfoColumns("a")} FROM {Tbl("DispatchProtocolAttachments")} a INNER JOIN {Tbl("DispatchProtocols")} p ON p.{Col("DispatchProtocolId")} = a.{Col("DispatchProtocolId")} " +
				$"WHERE p.{Col("DepartmentId")} = {P}DepartmentId ORDER BY a.{Col("DispatchProtocolAttachmentId")}",
				new { DepartmentId = departmentId });
		}

		public async Task<IEnumerable<DispatchProtocolAttachment>> GetAttachmentHashesByDepartmentIdAsync(int departmentId)
		{
			// Rows stored before M0381 (or written by a path that does not hash) are hashed in the database, once.
			var hash = IsPostgres ? "encode(sha256(data), 'hex')" : "LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', [Data]), 2))";
			await ExecuteAsync(
				$"UPDATE {Tbl("DispatchProtocolAttachments")} SET {Col("Sha256")} = {hash} " +
				$"WHERE {Col("Sha256")} IS NULL AND {Col("Data")} IS NOT NULL AND {Col("DispatchProtocolId")} IN " +
				$"(SELECT p.{Col("DispatchProtocolId")} FROM {Tbl("DispatchProtocols")} p WHERE p.{Col("DepartmentId")} = {P}DepartmentId)",
				new { DepartmentId = departmentId });

			return await QueryAsync<DispatchProtocolAttachment>(
				$"SELECT {InfoColumns("a")}, a.{Col("Sha256")} AS {Col("Sha256")} FROM {Tbl("DispatchProtocolAttachments")} a " +
				$"INNER JOIN {Tbl("DispatchProtocols")} p ON p.{Col("DispatchProtocolId")} = a.{Col("DispatchProtocolId")} " +
				$"WHERE p.{Col("DepartmentId")} = {P}DepartmentId ORDER BY a.{Col("DispatchProtocolAttachmentId")}",
				new { DepartmentId = departmentId });
		}

		public async Task<IEnumerable<DispatchProtocolAttachment>> GetDispatchProtocolAttachmentByProtocolIdAsync(int protocolId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DispatchProtocolAttachment>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ProtocolId", protocolId);

					var query = _queryFactory.GetQuery<SelectProtocolAttachmentsByProIdQuery>();

					return await x.QueryAsync<DispatchProtocolAttachment>(sql: query,
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
