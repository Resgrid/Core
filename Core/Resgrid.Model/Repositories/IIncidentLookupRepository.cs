using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Pick-list and label reads over the Incident Back Office tables (and the shipped InventoryVendors master the vendor catalog
	/// extends). Table and label columns come only from IncidentBackOfficeTables; nothing a caller passes reaches SQL unchecked.
	/// </summary>
	public interface IIncidentLookupRepository
	{
		Task<IReadOnlyList<IncidentLookupRow>> GetOptionsAsync(string table, int departmentId, IReadOnlyList<string> activationIds, bool includeDepartmentCatalog, int take, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<IncidentLookupRow>> GetLabelsAsync(string table, int departmentId, IReadOnlyList<string> ids, CancellationToken cancellationToken = default);

		/// <summary>Worker scope: every activation not closed or sealed, across departments (the only cross-tenant read of the module).</summary>
		Task<IReadOnlyList<IncidentActivationRef>> GetOpenActivationsAsync(CancellationToken cancellationToken = default);

		/// <summary>Worker scope: activations closed or sealed before <paramref name="closedBefore"/> (retention, plan §18).</summary>
		Task<IReadOnlyList<IncidentActivationRef>> GetClosedActivationsAsync(System.DateTime closedBefore, CancellationToken cancellationToken = default);

		/// <summary>Worker scope: departments holding live rows in a department-scoped catalog table (vendor compliance, teams).</summary>
		Task<IReadOnlyList<int>> GetDepartmentsWithRowsAsync(string table, CancellationToken cancellationToken = default);

		/// <summary>Self-service pages: the department and id of the live link with this token hash (the only cross-tenant lookup by token).</summary>
		Task<(int DepartmentId, string LinkId)?> FindSelfServiceLinkAsync(string tokenHash, CancellationToken cancellationToken = default);
	}

	public sealed class IncidentActivationRef
	{
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public int Status { get; set; }
		public System.DateTime? ClosedOn { get; set; }
	}

	public sealed class IncidentLookupRow
	{
		public string Id { get; set; }
		public string Label { get; set; }
	}
}
