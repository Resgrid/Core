using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// RMS-1B/1C repositories (registry M0158, M0159, M0161, M0162, M0163). Every department query begins at
	// DepartmentId; the product catalog tables are read with DepartmentId 0.

	public class RmsRecordDefinitionsRepository : RmsRepositoryBase<RmsRecordDefinition>, IRmsRecordDefinitionsRepository
	{
		public RmsRecordDefinitionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsRecordDefinition> GetByKeyAsync(int departmentId, string definitionKey)
			=> QueryFirstOrDefaultAsync<RmsRecordDefinition>($"SELECT * FROM {Tbl("RmsRecordDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DefinitionKey")} = {P}Key AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Key = definitionKey });

		public Task<RmsRecordDefinition> GetByIdForDepartmentAsync(int departmentId, string definitionId)
			=> QueryFirstOrDefaultAsync<RmsRecordDefinition>($"SELECT * FROM {Tbl("RmsRecordDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionId")} = {P}Id AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = definitionId });

		public Task<IEnumerable<RmsRecordDefinition>> GetForDepartmentAsync(int departmentId, bool includeRetired)
		{
			var retired = includeRetired ? string.Empty : $" AND {Col("IsRetired")} = {(IsPostgres ? "FALSE" : "0")}";
			return QueryAsync<RmsRecordDefinition>($"SELECT * FROM {Tbl("RmsRecordDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL{retired} ORDER BY {Col("Name")}", new { DepartmentId = departmentId });
		}

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string definitionId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsRecordDefinitions")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionId")} = {P}Id AND {Col("RowVersion")} = {P}Version", new { DepartmentId = departmentId, Id = definitionId, Version = expectedVersion }, cancellationToken) == 1;
	}

	public class RmsRecordDefinitionVersionsRepository : RmsRepositoryBase<RmsRecordDefinitionVersion>, IRmsRecordDefinitionVersionsRepository
	{
		public RmsRecordDefinitionVersionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsRecordDefinitionVersion> GetByIdForDepartmentAsync(int departmentId, string versionId)
			=> QueryFirstOrDefaultAsync<RmsRecordDefinitionVersion>($"SELECT * FROM {Tbl("RmsRecordDefinitionVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionVersionId")} = {P}Id", new { DepartmentId = departmentId, Id = versionId });

		public Task<RmsRecordDefinitionVersion> GetAsync(int departmentId, string definitionKey, int version)
			=> QueryFirstOrDefaultAsync<RmsRecordDefinitionVersion>($"SELECT * FROM {Tbl("RmsRecordDefinitionVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DefinitionKey")} = {P}Key AND {Col("Version")} = {P}Version", new { DepartmentId = departmentId, Key = definitionKey, Version = version });

		public Task<IEnumerable<RmsRecordDefinitionVersion>> GetForDefinitionAsync(int departmentId, string definitionKey)
			=> QueryAsync<RmsRecordDefinitionVersion>($"SELECT * FROM {Tbl("RmsRecordDefinitionVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DefinitionKey")} = {P}Key ORDER BY {Col("Version")}", new { DepartmentId = departmentId, Key = definitionKey });

		public Task<IEnumerable<RmsRecordDefinitionVersion>> GetPublishedForDepartmentAsync(int departmentId)
			=> QueryAsync<RmsRecordDefinitionVersion>($"SELECT * FROM {Tbl("RmsRecordDefinitionVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {(int)RmsDefinitionVersionState.Published} ORDER BY {Col("DefinitionKey")}, {Col("Version")}", new { DepartmentId = departmentId });

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string versionId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsRecordDefinitionVersions")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionVersionId")} = {P}Id AND {Col("RowVersion")} = {P}Version", new { DepartmentId = departmentId, Id = versionId, Version = expectedVersion }, cancellationToken) == 1;
	}

	public class RmsRecordSectionDefinitionsRepository : RmsRepositoryBase<RmsRecordSectionDefinition>, IRmsRecordSectionDefinitionsRepository
	{
		public RmsRecordSectionDefinitionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordSectionDefinition>> GetForVersionAsync(int departmentId, string versionId)
			=> QueryAsync<RmsRecordSectionDefinition>($"SELECT * FROM {Tbl("RmsRecordSectionDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionVersionId")} = {P}VersionId ORDER BY {Col("Ordinal")}", new { DepartmentId = departmentId, VersionId = versionId });

		public Task<int> DeleteForVersionAsync(int departmentId, string versionId, CancellationToken cancellationToken = default)
			=> ExecuteAsync($"DELETE FROM {Tbl("RmsRecordSectionDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionVersionId")} = {P}VersionId", new { DepartmentId = departmentId, VersionId = versionId }, cancellationToken);
	}

	public class RmsRecordFieldDefinitionsRepository : RmsRepositoryBase<RmsRecordFieldDefinition>, IRmsRecordFieldDefinitionsRepository
	{
		public RmsRecordFieldDefinitionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordFieldDefinition>> GetForVersionAsync(int departmentId, string versionId)
			=> QueryAsync<RmsRecordFieldDefinition>($"SELECT * FROM {Tbl("RmsRecordFieldDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionVersionId")} = {P}VersionId ORDER BY {Col("SectionKey")}, {Col("Ordinal")}", new { DepartmentId = departmentId, VersionId = versionId });

		public Task<int> DeleteForVersionAsync(int departmentId, string versionId, CancellationToken cancellationToken = default)
			=> ExecuteAsync($"DELETE FROM {Tbl("RmsRecordFieldDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionVersionId")} = {P}VersionId", new { DepartmentId = departmentId, VersionId = versionId }, cancellationToken);
	}

	public class RmsRecordValueGroupsRepository : RmsRepositoryBase<RmsRecordValueGroup>, IRmsRecordValueGroupsRepository
	{
		public RmsRecordValueGroupsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordValueGroup>> GetForRecordAsync(int departmentId, string recordId, string revisionId)
		{
			var revision = revisionId == null ? $"{Col("RevisionId")} IS NULL" : $"{Col("RevisionId")} = {P}RevisionId";
			return QueryAsync<RmsRecordValueGroup>($"SELECT * FROM {Tbl("RmsRecordValueGroups")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {revision} ORDER BY {Col("SectionKey")}, {Col("Ordinal")}", new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId });
		}

		public Task<IEnumerable<RmsRecordValueGroup>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds, bool draftsOnly)
		{
			var ids = (recordIds ?? Enumerable.Empty<string>()).Distinct().ToArray();
			if (ids.Length == 0) return Task.FromResult(Enumerable.Empty<RmsRecordValueGroup>());
			var revision = draftsOnly ? $" AND {Col("RevisionId")} IS NULL" : string.Empty;
			return QueryAsync<RmsRecordValueGroup>($"SELECT * FROM {Tbl("RmsRecordValueGroups")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RecordId", "Ids")}{revision}", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<IEnumerable<RmsRecordValueGroup>> GetForRevisionsAsync(int departmentId, IEnumerable<string> revisionIds)
		{
			var ids = (revisionIds ?? Enumerable.Empty<string>()).Distinct().ToArray();
			if (ids.Length == 0) return Task.FromResult(Enumerable.Empty<RmsRecordValueGroup>());
			return QueryAsync<RmsRecordValueGroup>($"SELECT * FROM {Tbl("RmsRecordValueGroups")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RevisionId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
			=> ExecuteAsync($"DELETE FROM {Tbl("RmsRecordValueGroups")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL", new { DepartmentId = departmentId, RecordId = recordId }, cancellationToken);
	}

	public class RmsRecordValuesRepository : RmsRepositoryBase<RmsRecordValue>, IRmsRecordValuesRepository
	{
		public RmsRecordValuesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordValue>> GetForRecordAsync(int departmentId, string recordId, string revisionId)
		{
			var revision = revisionId == null ? $"{Col("RevisionId")} IS NULL" : $"{Col("RevisionId")} = {P}RevisionId";
			return QueryAsync<RmsRecordValue>($"SELECT * FROM {Tbl("RmsRecordValues")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {revision} ORDER BY {Col("FieldKey")}, {Col("Ordinal")}", new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId });
		}

		public Task<IEnumerable<RmsRecordValue>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds, bool draftsOnly)
		{
			var ids = (recordIds ?? Enumerable.Empty<string>()).Distinct().ToArray();
			if (ids.Length == 0) return Task.FromResult(Enumerable.Empty<RmsRecordValue>());
			var revision = draftsOnly ? $" AND {Col("RevisionId")} IS NULL" : string.Empty;
			return QueryAsync<RmsRecordValue>($"SELECT * FROM {Tbl("RmsRecordValues")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RecordId", "Ids")}{revision}", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<IEnumerable<RmsRecordValue>> GetForRevisionsAsync(int departmentId, IEnumerable<string> revisionIds)
		{
			var ids = (revisionIds ?? Enumerable.Empty<string>()).Distinct().ToArray();
			if (ids.Length == 0) return Task.FromResult(Enumerable.Empty<RmsRecordValue>());
			return QueryAsync<RmsRecordValue>($"SELECT * FROM {Tbl("RmsRecordValues")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RevisionId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
			=> ExecuteAsync($"DELETE FROM {Tbl("RmsRecordValues")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL", new { DepartmentId = departmentId, RecordId = recordId }, cancellationToken);

		public Task<int> CountRecordsOnVersionAsync(int departmentId, string versionId, bool draftsOnly)
		{
			var revision = draftsOnly ? $" AND {Col("RevisionId")} IS NULL" : string.Empty;
			return ScalarAsync<int>($"SELECT COUNT(DISTINCT {Col("RecordId")}) FROM {Tbl("RmsRecordValues")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordDefinitionVersionId")} = {P}VersionId{revision}", new { DepartmentId = departmentId, VersionId = versionId });
		}
	}

	public class RmsSavedReportDefinitionsRepository : RmsRepositoryBase<RmsSavedReportDefinition>, IRmsSavedReportDefinitionsRepository
	{
		public RmsSavedReportDefinitionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsSavedReportDefinition> GetByIdForDepartmentAsync(int departmentId, string reportId)
			=> QueryFirstOrDefaultAsync<RmsSavedReportDefinition>($"SELECT * FROM {Tbl("RmsSavedReportDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsSavedReportDefinitionId")} = {P}Id AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = reportId });

		public Task<IEnumerable<RmsSavedReportDefinition>> GetForDepartmentAsync(int departmentId)
			=> QueryAsync<RmsSavedReportDefinition>($"SELECT * FROM {Tbl("RmsSavedReportDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Name")}", new { DepartmentId = departmentId });

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string reportId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsSavedReportDefinitions")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsSavedReportDefinitionId")} = {P}Id AND {Col("RowVersion")} = {P}Version AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = reportId, Version = expectedVersion }, cancellationToken) == 1;
	}

	public class RmsTemplatePackVersionsRepository : RmsRepositoryBase<RmsTemplatePackVersion>, IRmsTemplatePackVersionsRepository
	{
		public RmsTemplatePackVersionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsTemplatePackVersion>> GetCatalogAsync()
			=> QueryAsync<RmsTemplatePackVersion>($"SELECT * FROM {Tbl("RmsTemplatePackVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("PackKey")}, {Col("Version")}", new { DepartmentId = RmsTemplatePackVersion.ProductDepartmentId });

		public Task<RmsTemplatePackVersion> GetAsync(string packKey, int version)
			=> QueryFirstOrDefaultAsync<RmsTemplatePackVersion>($"SELECT * FROM {Tbl("RmsTemplatePackVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("PackKey")} = {P}Key AND {Col("Version")} = {P}Version", new { DepartmentId = RmsTemplatePackVersion.ProductDepartmentId, Key = packKey, Version = version });
	}

	public class RmsJurisdictionProfileVersionsRepository : RmsRepositoryBase<RmsJurisdictionProfileVersion>, IRmsJurisdictionProfileVersionsRepository
	{
		public RmsJurisdictionProfileVersionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsJurisdictionProfileVersion>> GetCatalogAsync()
			=> QueryAsync<RmsJurisdictionProfileVersion>($"SELECT * FROM {Tbl("RmsJurisdictionProfileVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ProfileKey")}, {Col("Version")}", new { DepartmentId = RmsTemplatePackVersion.ProductDepartmentId });

		public Task<RmsJurisdictionProfileVersion> GetAsync(string profileKey, int version)
			=> QueryFirstOrDefaultAsync<RmsJurisdictionProfileVersion>($"SELECT * FROM {Tbl("RmsJurisdictionProfileVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProfileKey")} = {P}Key AND {Col("Version")} = {P}Version", new { DepartmentId = RmsTemplatePackVersion.ProductDepartmentId, Key = profileKey, Version = version });

		public Task<RmsJurisdictionProfileVersion> GetLatestAsync(string profileKey)
			=> QueryFirstOrDefaultAsync<RmsJurisdictionProfileVersion>($"SELECT * FROM {Tbl("RmsJurisdictionProfileVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProfileKey")} = {P}Key ORDER BY {Col("Version")} DESC", new { DepartmentId = RmsTemplatePackVersion.ProductDepartmentId, Key = profileKey });
	}

	public class RmsExternalOrdersRepository : RmsRepositoryBase<RmsExternalOrder>, IRmsExternalOrdersRepository
	{
		private static readonly string MetadataColumns = Cols("RmsExternalOrderId", "DepartmentId", "ProtectionId", "RecordId", "ProfileKey", "ProfileVersion", "HomeProfileKey", "HostProfileKey", "SourceScheme", "SourceSystem",
			"OrderNumber", "IncidentName", "IncidentNumber", "IncidentCountry", "IncidentSubdivision", "OrderingOffice", "DispatchOffice", "RequestingAgency", "ReceivingAgency", "SendingAgency", "DepartmentRole", "CostCode",
			"AgreementReference", "CurrencyCode", "MeasurementSystem", "TimeZoneId", "CapturedOffsetMinutes", "SourceCapturedOn", "SourceVersion", "ArtifactFileName", "ArtifactContentType", "ArtifactChecksum", "ArtifactSafeUrl",
			"Status", "MobilizedOn", "ReleasedOn", "ClosedOutOn", "ClosedOutByUserId", "CloseoutNotes", "IsProtected", "ProtectedCatalogVersion", "CreatedOn", "CreatedByUserId", "ModifiedOn", "ModifiedByUserId", "RowVersion", "DeletedOn");

		public RmsExternalOrdersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsExternalOrder> GetByIdForDepartmentAsync(int departmentId, string orderId, bool includeArtifact)
			=> QueryFirstOrDefaultAsync<RmsExternalOrder>($"SELECT {(includeArtifact ? "*" : MetadataColumns)} FROM {Tbl("RmsExternalOrders")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderId")} = {P}Id AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = orderId });

		public Task<RmsExternalOrder> GetForRecordAsync(int departmentId, string recordId)
			=> QueryFirstOrDefaultAsync<RmsExternalOrder>($"SELECT {MetadataColumns} FROM {Tbl("RmsExternalOrders")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, RecordId = recordId });

		public Task<IEnumerable<RmsExternalOrder>> GetForDepartmentAsync(int departmentId, bool includeClosed)
		{
			var closed = includeClosed ? string.Empty : $" AND {Col("Status")} <> {(int)RmsExternalOrderStatus.ClosedOut}";
			return QueryAsync<RmsExternalOrder>($"SELECT {MetadataColumns} FROM {Tbl("RmsExternalOrders")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL{closed} ORDER BY {Col("CreatedOn")} DESC", new { DepartmentId = departmentId });
		}

		public Task<byte[]> GetArtifactAsync(int departmentId, string orderId)
			=> ScalarAsync<byte[]>($"SELECT {Col("ArtifactData")} FROM {Tbl("RmsExternalOrders")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderId")} = {P}Id AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = orderId });

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string orderId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsExternalOrders")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderId")} = {P}Id AND {Col("RowVersion")} = {P}Version AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = orderId, Version = expectedVersion }, cancellationToken) == 1;
	}

	public class RmsExternalOrderFillsRepository : RmsRepositoryBase<RmsExternalOrderFill>, IRmsExternalOrderFillsRepository
	{
		public RmsExternalOrderFillsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsExternalOrderFill>> GetForOrderAsync(int departmentId, string orderId)
			=> QueryAsync<RmsExternalOrderFill>($"SELECT * FROM {Tbl("RmsExternalOrderFills")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderId")} = {P}OrderId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("RequestNumber")}", new { DepartmentId = departmentId, OrderId = orderId });

		public Task<RmsExternalOrderFill> GetByIdForDepartmentAsync(int departmentId, string fillId)
			=> QueryFirstOrDefaultAsync<RmsExternalOrderFill>($"SELECT * FROM {Tbl("RmsExternalOrderFills")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExternalOrderFillId")} = {P}Id AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Id = fillId });
	}
}
