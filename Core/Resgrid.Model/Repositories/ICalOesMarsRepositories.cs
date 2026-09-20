using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.CostRecovery.CalOesMars;

namespace Resgrid.Model.Repositories
{
	// Workforce & Business Operations plan, Phase C-M3 (C3): Cal OES MARS shadow-table repositories (registry M0219).
	// Every query is department-scoped; snapshots and observations are corrected by a superseding row, never rewritten.

	public interface ICalOesMarsAgencyProfileRepository : IRepository<CalOesMarsAgencyProfile>
	{
		Task<CalOesMarsAgencyProfile> GetByDepartmentAsync(int departmentId);
	}

	public interface ICalOesMarsResourceProfileRepository : IRepository<CalOesMarsResourceProfile>
	{
		Task<CalOesMarsResourceProfile> GetByIdForDepartmentAsync(string resourceProfileId, int departmentId);
		Task<IEnumerable<CalOesMarsResourceProfile>> GetForDepartmentAsync(int departmentId);
		Task<IEnumerable<CalOesMarsResourceProfile>> GetByUnitIdsAsync(int departmentId, IEnumerable<int> unitIds);
	}

	public interface ICalOesMarsRateProfileRepository : IRepository<CalOesMarsRateProfile>
	{
		Task<CalOesMarsRateProfile> GetByIdForDepartmentAsync(string rateProfileId, int departmentId);
		Task<IEnumerable<CalOesMarsRateProfile>> GetForDepartmentAsync(int departmentId, int? submissionYear = null);
		/// <summary>Profiles of every submission type effective on the date (the as-of-dispatch lookup).</summary>
		Task<IEnumerable<CalOesMarsRateProfile>> GetEffectiveAsync(int departmentId, DateTime asOf);
	}

	public interface ICalOesMarsRateLineRepository : IRepository<CalOesMarsRateLine>
	{
		Task<CalOesMarsRateLine> GetByIdForDepartmentAsync(string rateLineId, int departmentId);
		Task<IEnumerable<CalOesMarsRateLine>> GetByProfileAsync(string rateProfileId);
		Task<IEnumerable<CalOesMarsRateLine>> GetByProfilesAsync(IEnumerable<string> rateProfileIds);
	}

	public interface ICalOesMarsAdministrativeRateInputRepository : IRepository<CalOesMarsAdministrativeRateInput>
	{
		Task<CalOesMarsAdministrativeRateInput> GetByIdForDepartmentAsync(string inputId, int departmentId);
		Task<IEnumerable<CalOesMarsAdministrativeRateInput>> GetByProfileAsync(string rateProfileId);
	}

	public interface ICalOesMarsAgreementSnapshotRepository : IRepository<CalOesMarsAgreementSnapshot>
	{
		Task<CalOesMarsAgreementSnapshot> GetByIdForDepartmentAsync(string agreementSnapshotId, int departmentId);
		Task<IEnumerable<CalOesMarsAgreementSnapshot>> GetForDepartmentAsync(int departmentId);
	}

	public interface ICalOesMarsWorkItemRepository : IRepository<CalOesMarsWorkItem>
	{
		Task<CalOesMarsWorkItem> GetByIdForDepartmentAsync(string workItemId, int departmentId);
		Task<IEnumerable<CalOesMarsWorkItem>> GetByDeploymentAsync(string deploymentId, int departmentId);
		Task<IEnumerable<CalOesMarsWorkItem>> GetByExternalIdAsync(int departmentId, string marsRecordId);
		/// <summary>Open items (every state but Closed) for the department queue.</summary>
		Task<IEnumerable<CalOesMarsWorkItem>> GetActionQueueAsync(int departmentId, int? recordType = null);
		/// <summary>Every non-deleted item (closed ones included) that references the agreement snapshot — the immutability / in-use check.</summary>
		Task<IEnumerable<CalOesMarsWorkItem>> GetByAgreementSnapshotAsync(int departmentId, string agreementSnapshotId);
		/// <summary>Items whose external state is not settled (submitted, returned, approved without an invoice, invoices not paid).</summary>
		Task<IEnumerable<CalOesMarsWorkItem>> GetUnreconciledAsync(int departmentId);
		/// <summary>Departments with any open item (the worker's sweep scope).</summary>
		Task<IEnumerable<int>> GetDepartmentsWithOpenItemsAsync();
	}

	public interface ICalOesMarsReimbursementLineRepository : IRepository<CalOesMarsReimbursementLine>
	{
		Task<IEnumerable<CalOesMarsReimbursementLine>> GetByWorkItemAsync(string workItemId);
		Task<bool> DeleteByWorkItemAsync(string workItemId, CancellationToken cancellationToken = default);
	}
}
