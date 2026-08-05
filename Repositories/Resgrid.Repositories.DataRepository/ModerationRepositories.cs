using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
	public class ModerationRequestRepository : RepositoryBase<ModerationRequest>, IModerationRequestRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ModerationRequestRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<ModerationRequest> GetByItemAsync(int departmentId, int itemType, string itemId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("ItemType", itemType);
				parameters.Add("ItemId", itemId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $@"SELECT moderationrequestid, departmentid, itemtype, itemid, callid,
	chatchannelid, contentauthoruserid, contentauthorunitid, contentcreatedon,
	originalsubject, originaltext, originalfilename, originalcontenttype,
	originalcontent, originalmetadatajson, status, disposition, createdon, modifiedon,
	completedbyuserid, completedon, adminnote
FROM {_sqlConfiguration.SchemaName}.moderationrequests WHERE departmentid = {notation}DepartmentId AND itemtype = {notation}ItemType AND itemid = {notation}ItemId"
					: $@"SELECT [ModerationRequestId], [DepartmentId], [ItemType], [ItemId], [CallId],
	[ChatChannelId], [ContentAuthorUserId], [ContentAuthorUnitId], [ContentCreatedOn],
	[OriginalSubject], [OriginalText], [OriginalFileName], [OriginalContentType],
	[OriginalContent], [OriginalMetadataJson], [Status], [Disposition], [CreatedOn], [ModifiedOn],
	[CompletedByUserId], [CompletedOn], [AdminNote]
FROM {_sqlConfiguration.SchemaName}.[ModerationRequests] WHERE [DepartmentId] = {notation}DepartmentId AND [ItemType] = {notation}ItemType AND [ItemId] = {notation}ItemId";

				return (await QueryAsync(sql, parameters)).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ModerationRequest>> GetByItemsAndReporterAsync(int departmentId, int itemType,
			IEnumerable<string> itemIds, string reporterUserId)
		{
			try
			{
				var ids = itemIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ModerationRequest>();

				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("ItemType", itemType);
				parameters.Add("ItemIds", ids);
				parameters.Add("ReporterUserId", reporterUserId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $@"SELECT r.moderationrequestid, r.departmentid, r.itemtype, r.itemid, r.callid,
	r.chatchannelid, r.contentauthoruserid, r.contentauthorunitid, r.contentcreatedon,
	r.originalsubject, r.originaltext, r.originalfilename, r.originalcontenttype,
	r.originalmetadatajson, r.status, r.disposition, r.createdon, r.modifiedon,
	r.completedbyuserid, r.completedon, r.adminnote
FROM {_sqlConfiguration.SchemaName}.moderationrequests r
WHERE r.departmentid = {notation}DepartmentId AND r.itemtype = {notation}ItemType
	AND r.itemid IN {notation}ItemIds
	AND EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.moderationreports p
		WHERE p.moderationrequestid = r.moderationrequestid AND p.reportedbyuserid = {notation}ReporterUserId)"
					: $@"SELECT r.[ModerationRequestId], r.[DepartmentId], r.[ItemType], r.[ItemId], r.[CallId],
	r.[ChatChannelId], r.[ContentAuthorUserId], r.[ContentAuthorUnitId], r.[ContentCreatedOn],
	r.[OriginalSubject], r.[OriginalText], r.[OriginalFileName], r.[OriginalContentType],
	r.[OriginalMetadataJson], r.[Status], r.[Disposition], r.[CreatedOn], r.[ModifiedOn],
	r.[CompletedByUserId], r.[CompletedOn], r.[AdminNote]
FROM {_sqlConfiguration.SchemaName}.[ModerationRequests] r
WHERE r.[DepartmentId] = {notation}DepartmentId AND r.[ItemType] = {notation}ItemType
	AND r.[ItemId] IN {notation}ItemIds
	AND EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.[ModerationReports] p
		WHERE p.[ModerationRequestId] = r.[ModerationRequestId] AND p.[ReportedByUserId] = {notation}ReporterUserId)";

				return await QueryAsync(sql, parameters);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ModerationRequest>> SearchAsync(int departmentId, ModerationSearchCriteria criteria,
			IEnumerable<int> visibleGroupIds, string reporterUserId)
		{
			criteria ??= new ModerationSearchCriteria();
			var pageSize = criteria.PageSize <= 0 ? 50 : Math.Min(criteria.PageSize, 200);
			var page = Math.Max(criteria.Page, 1);

			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("PageSize", pageSize);
				parameters.Add("Offset", (page - 1) * pageSize);
				var notation = _sqlConfiguration.ParameterNotation;
				var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
				var requestAlias = postgres ? "r" : "r";
				var filters = new List<string>();

				if (criteria.Status.HasValue)
				{
					parameters.Add("Status", (int)criteria.Status.Value);
					filters.Add(postgres ? $"r.status = {notation}Status" : $"r.[Status] = {notation}Status");
				}

				if (criteria.ItemType.HasValue)
				{
					parameters.Add("ItemType", (int)criteria.ItemType.Value);
					filters.Add(postgres ? $"r.itemtype = {notation}ItemType" : $"r.[ItemType] = {notation}ItemType");
				}

				if (!string.IsNullOrWhiteSpace(criteria.ContentAuthorUserId))
				{
					parameters.Add("ContentAuthorUserId", criteria.ContentAuthorUserId);
					filters.Add(postgres
						? $"r.contentauthoruserid = {notation}ContentAuthorUserId"
						: $"r.[ContentAuthorUserId] = {notation}ContentAuthorUserId");
				}

				if (!string.IsNullOrWhiteSpace(criteria.ReportedByUserId))
				{
					parameters.Add("ReportedByUserId", criteria.ReportedByUserId);
					if (string.IsNullOrWhiteSpace(reporterUserId))
					{
						filters.Add(postgres
							? $"EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.moderationreports rf WHERE rf.moderationrequestid = r.moderationrequestid AND rf.reportedbyuserid = {notation}ReportedByUserId)"
							: $"EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.[ModerationReports] rf WHERE rf.[ModerationRequestId] = r.[ModerationRequestId] AND rf.[ReportedByUserId] = {notation}ReportedByUserId)");
					}
				}

				if (criteria.From.HasValue)
				{
					parameters.Add("From", criteria.From.Value);
					filters.Add(postgres ? $"r.createdon >= {notation}From" : $"r.[CreatedOn] >= {notation}From");
				}

				if (criteria.To.HasValue)
				{
					parameters.Add("To", criteria.To.Value);
					filters.Add(postgres ? $"r.createdon < {notation}To" : $"r.[CreatedOn] < {notation}To");
				}

				var groupIds = visibleGroupIds?.Distinct().ToList() ?? new List<int>();
				if (!string.IsNullOrWhiteSpace(reporterUserId))
				{
					parameters.Add("ReporterUserId", reporterUserId);
					if (groupIds.Count > 0)
					{
						parameters.Add("VisibleGroupIds", groupIds);
						var requestedReporter = string.IsNullOrWhiteSpace(criteria.ReportedByUserId)
							? string.Empty
							: postgres
								? $" AND rv.reportedbyuserid = {notation}ReportedByUserId"
								: $" AND rv.[ReportedByUserId] = {notation}ReportedByUserId";
						filters.Add(postgres
							? $"EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.moderationreports rv WHERE rv.moderationrequestid = r.moderationrequestid{requestedReporter} AND (rv.reportedbyuserid = {notation}ReporterUserId OR rv.reportergroupid IN {notation}VisibleGroupIds))"
							: $"EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.[ModerationReports] rv WHERE rv.[ModerationRequestId] = r.[ModerationRequestId]{requestedReporter} AND (rv.[ReportedByUserId] = {notation}ReporterUserId OR rv.[ReporterGroupId] IN {notation}VisibleGroupIds))");
					}
					else
					{
						var requestedReporter = string.IsNullOrWhiteSpace(criteria.ReportedByUserId)
							? string.Empty
							: postgres
								? $" AND rv.reportedbyuserid = {notation}ReportedByUserId"
								: $" AND rv.[ReportedByUserId] = {notation}ReportedByUserId";
						filters.Add(postgres
							? $"EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.moderationreports rv WHERE rv.moderationrequestid = r.moderationrequestid{requestedReporter} AND rv.reportedbyuserid = {notation}ReporterUserId)"
							: $"EXISTS (SELECT 1 FROM {_sqlConfiguration.SchemaName}.[ModerationReports] rv WHERE rv.[ModerationRequestId] = r.[ModerationRequestId]{requestedReporter} AND rv.[ReportedByUserId] = {notation}ReporterUserId)");
					}
				}

				var extra = filters.Count > 0 ? " AND " + string.Join(" AND ", filters) : string.Empty;
				string sql;
				if (postgres)
				{
					sql = $@"SELECT r.moderationrequestid, r.departmentid, r.itemtype, r.itemid, r.callid,
	r.chatchannelid, r.contentauthoruserid, r.contentauthorunitid, r.contentcreatedon,
	r.originalsubject, r.originaltext, r.originalfilename, r.originalcontenttype,
	r.originalmetadatajson, r.status, r.disposition, r.createdon, r.modifiedon,
	r.completedbyuserid, r.completedon, r.adminnote
FROM {_sqlConfiguration.SchemaName}.moderationrequests {requestAlias}
WHERE r.departmentid = {notation}DepartmentId{extra}
ORDER BY r.modifiedon DESC
LIMIT {notation}PageSize OFFSET {notation}Offset";
				}
				else
				{
					sql = $@"SELECT r.[ModerationRequestId], r.[DepartmentId], r.[ItemType], r.[ItemId], r.[CallId],
	r.[ChatChannelId], r.[ContentAuthorUserId], r.[ContentAuthorUnitId], r.[ContentCreatedOn],
	r.[OriginalSubject], r.[OriginalText], r.[OriginalFileName], r.[OriginalContentType],
	r.[OriginalMetadataJson], r.[Status], r.[Disposition], r.[CreatedOn], r.[ModifiedOn],
	r.[CompletedByUserId], r.[CompletedOn], r.[AdminNote]
FROM {_sqlConfiguration.SchemaName}.[ModerationRequests] r
WHERE r.[DepartmentId] = {notation}DepartmentId{extra}
ORDER BY r.[ModifiedOn] DESC
OFFSET {notation}Offset ROWS FETCH NEXT {notation}PageSize ROWS ONLY";
				}

				return await QueryAsync(sql, parameters);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		private async Task<IEnumerable<ModerationRequest>> QueryAsync(string sql, object parameters)
		{
			var select = new Func<DbConnection, Task<IEnumerable<ModerationRequest>>>(connection =>
				connection.QueryAsync<ModerationRequest>(sql, parameters, _unitOfWork.Transaction));

			if (_unitOfWork?.Connection == null)
			{
				using var connection = _connectionProvider.Create();
				await connection.OpenAsync();
				return await select(connection);
			}

			return await select(_unitOfWork.CreateOrGetConnection());
		}
	}

	public class ModerationReportRepository : RepositoryBase<ModerationReport>, IModerationReportRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ModerationReportRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<ModerationReport> GetByRequestAndReporterAsync(string moderationRequestId, string reportedByUserId)
		{
			var rows = await QueryAsync(moderationRequestId, reportedByUserId);
			return rows.FirstOrDefault();
		}

		public Task<IEnumerable<ModerationReport>> GetByRequestAsync(string moderationRequestId)
		{
			return QueryAsync(moderationRequestId, null);
		}

		public async Task<IEnumerable<ModerationReport>> GetByRequestIdsAsync(IEnumerable<string> moderationRequestIds)
		{
			try
			{
				var ids = moderationRequestIds?.Distinct().ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ModerationReport>();

				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.moderationreports WHERE moderationrequestid IN {notation}Ids ORDER BY moderationrequestid, reportedon"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ModerationReports] WHERE [ModerationRequestId] IN {notation}Ids ORDER BY [ModerationRequestId], [ReportedOn]";

				var select = new Func<DbConnection, Task<IEnumerable<ModerationReport>>>(connection =>
					connection.QueryAsync<ModerationReport>(sql, new { Ids = ids }, _unitOfWork?.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		private async Task<IEnumerable<ModerationReport>> QueryAsync(string moderationRequestId, string reportedByUserId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ModerationRequestId", moderationRequestId);
				var notation = _sqlConfiguration.ParameterNotation;
				var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
				var reporterClause = string.Empty;
				if (!string.IsNullOrWhiteSpace(reportedByUserId))
				{
					parameters.Add("ReportedByUserId", reportedByUserId);
					reporterClause = postgres
						? $" AND reportedbyuserid = {notation}ReportedByUserId"
						: $" AND [ReportedByUserId] = {notation}ReportedByUserId";
				}

				var sql = postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.moderationreports WHERE moderationrequestid = {notation}ModerationRequestId{reporterClause} ORDER BY reportedon"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[ModerationReports] WHERE [ModerationRequestId] = {notation}ModerationRequestId{reporterClause} ORDER BY [ReportedOn]";

				var select = new Func<DbConnection, Task<IEnumerable<ModerationReport>>>(connection =>
					connection.QueryAsync<ModerationReport>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class ModerationActionRepository : RepositoryBase<ModerationAction>, IModerationActionRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public ModerationActionRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ModerationAction>> GetByRequestAsync(string moderationRequestId)
		{
			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("ModerationRequestId", moderationRequestId);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $@"SELECT moderationactionid, moderationrequestid, departmentid, actiontype,
	performedbyuserid, performedon, note, previousstatus, newstatus, actorrole, ipaddress,
	useragent, traceid, servername, detailsjson, evidencetext, evidencemetadatajson
FROM {_sqlConfiguration.SchemaName}.moderationactions
WHERE moderationrequestid = {notation}ModerationRequestId ORDER BY performedon"
					: $@"SELECT [ModerationActionId], [ModerationRequestId], [DepartmentId], [ActionType],
	[PerformedByUserId], [PerformedOn], [Note], [PreviousStatus], [NewStatus], [ActorRole], [IpAddress],
	[UserAgent], [TraceId], [ServerName], [DetailsJson], [EvidenceText], [EvidenceMetadataJson]
FROM {_sqlConfiguration.SchemaName}.[ModerationActions]
WHERE [ModerationRequestId] = {notation}ModerationRequestId ORDER BY [PerformedOn]";

				var select = new Func<DbConnection, Task<IEnumerable<ModerationAction>>>(connection =>
					connection.QueryAsync<ModerationAction>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<ModerationAction>> GetByRequestIdsAsync(IEnumerable<string> moderationRequestIds)
		{
			try
			{
				var ids = moderationRequestIds?.Distinct().ToList() ?? new List<string>();
				if (ids.Count == 0)
					return new List<ModerationAction>();

				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $@"SELECT moderationactionid, moderationrequestid, departmentid, actiontype,
	performedbyuserid, performedon, note, previousstatus, newstatus, actorrole, ipaddress,
	useragent, traceid, servername, detailsjson, evidencetext, evidencemetadatajson
FROM {_sqlConfiguration.SchemaName}.moderationactions
WHERE moderationrequestid IN {notation}Ids ORDER BY moderationrequestid, performedon"
					: $@"SELECT [ModerationActionId], [ModerationRequestId], [DepartmentId], [ActionType],
	[PerformedByUserId], [PerformedOn], [Note], [PreviousStatus], [NewStatus], [ActorRole], [IpAddress],
	[UserAgent], [TraceId], [ServerName], [DetailsJson], [EvidenceText], [EvidenceMetadataJson]
FROM {_sqlConfiguration.SchemaName}.[ModerationActions]
WHERE [ModerationRequestId] IN {notation}Ids ORDER BY [ModerationRequestId], [PerformedOn]";

				var select = new Func<DbConnection, Task<IEnumerable<ModerationAction>>>(connection =>
					connection.QueryAsync<ModerationAction>(sql, new { Ids = ids }, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return await select(connection);
				}

				return await select(_unitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
