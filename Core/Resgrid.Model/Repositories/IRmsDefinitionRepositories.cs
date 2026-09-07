using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	// RMS-1B/1C repositories (registry M0158, M0159, M0161, M0162, M0163). Every query begins at DepartmentId; product
	// catalog rows (packs, profiles) use DepartmentId 0.

	public interface IRmsRecordDefinitionsRepository : IRepository<RmsRecordDefinition>
	{
		Task<RmsRecordDefinition> GetByKeyAsync(int departmentId, string definitionKey);
		Task<RmsRecordDefinition> GetByIdForDepartmentAsync(int departmentId, string definitionId);
		Task<IEnumerable<RmsRecordDefinition>> GetForDepartmentAsync(int departmentId, bool includeRetired);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string definitionId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordDefinitionVersionsRepository : IRepository<RmsRecordDefinitionVersion>
	{
		Task<RmsRecordDefinitionVersion> GetByIdForDepartmentAsync(int departmentId, string versionId);
		Task<RmsRecordDefinitionVersion> GetAsync(int departmentId, string definitionKey, int version);
		Task<IEnumerable<RmsRecordDefinitionVersion>> GetForDefinitionAsync(int departmentId, string definitionKey);
		/// <summary>Published versions across the department, for the client catalog and the New Record chooser.</summary>
		Task<IEnumerable<RmsRecordDefinitionVersion>> GetPublishedForDepartmentAsync(int departmentId);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string versionId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordSectionDefinitionsRepository : IRepository<RmsRecordSectionDefinition>
	{
		Task<IEnumerable<RmsRecordSectionDefinition>> GetForVersionAsync(int departmentId, string versionId);
		Task<int> DeleteForVersionAsync(int departmentId, string versionId, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordFieldDefinitionsRepository : IRepository<RmsRecordFieldDefinition>
	{
		Task<IEnumerable<RmsRecordFieldDefinition>> GetForVersionAsync(int departmentId, string versionId);
		Task<int> DeleteForVersionAsync(int departmentId, string versionId, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordValueGroupsRepository : IRepository<RmsRecordValueGroup>
	{
		Task<IEnumerable<RmsRecordValueGroup>> GetForRecordAsync(int departmentId, string recordId, string revisionId);
		Task<IEnumerable<RmsRecordValueGroup>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds, bool draftsOnly);
		Task<IEnumerable<RmsRecordValueGroup>> GetForRevisionsAsync(int departmentId, IEnumerable<string> revisionIds);
		Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordValuesRepository : IRepository<RmsRecordValue>
	{
		Task<IEnumerable<RmsRecordValue>> GetForRecordAsync(int departmentId, string recordId, string revisionId);
		/// <summary>Draft rows for many records at once (the New Record chooser's duplicate hints, saved reports over drafts).</summary>
		Task<IEnumerable<RmsRecordValue>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds, bool draftsOnly);
		/// <summary>Revision rows for a bounded record set (saved reports run over immutable revisions).</summary>
		Task<IEnumerable<RmsRecordValue>> GetForRevisionsAsync(int departmentId, IEnumerable<string> revisionIds);
		Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);
		Task<int> CountRecordsOnVersionAsync(int departmentId, string versionId, bool draftsOnly);
	}

	public interface IRmsSavedReportDefinitionsRepository : IRepository<RmsSavedReportDefinition>
	{
		Task<RmsSavedReportDefinition> GetByIdForDepartmentAsync(int departmentId, string reportId);
		Task<IEnumerable<RmsSavedReportDefinition>> GetForDepartmentAsync(int departmentId);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string reportId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsTemplatePackVersionsRepository : IRepository<RmsTemplatePackVersion>
	{
		Task<IEnumerable<RmsTemplatePackVersion>> GetCatalogAsync();
		Task<RmsTemplatePackVersion> GetAsync(string packKey, int version);
	}

	public interface IRmsJurisdictionProfileVersionsRepository : IRepository<RmsJurisdictionProfileVersion>
	{
		Task<IEnumerable<RmsJurisdictionProfileVersion>> GetCatalogAsync();
		Task<RmsJurisdictionProfileVersion> GetAsync(string profileKey, int version);
		Task<RmsJurisdictionProfileVersion> GetLatestAsync(string profileKey);
	}

	public interface IRmsExternalOrdersRepository : IRepository<RmsExternalOrder>
	{
		Task<RmsExternalOrder> GetByIdForDepartmentAsync(int departmentId, string orderId, bool includeArtifact);
		Task<RmsExternalOrder> GetForRecordAsync(int departmentId, string recordId);
		Task<IEnumerable<RmsExternalOrder>> GetForDepartmentAsync(int departmentId, bool includeClosed);
		Task<byte[]> GetArtifactAsync(int departmentId, string orderId);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string orderId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsExternalOrderFillsRepository : IRepository<RmsExternalOrderFill>
	{
		Task<IEnumerable<RmsExternalOrderFill>> GetForOrderAsync(int departmentId, string orderId);
		Task<RmsExternalOrderFill> GetByIdForDepartmentAsync(int departmentId, string fillId);
	}
}
