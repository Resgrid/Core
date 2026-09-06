using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRmsExportTemplatesRepository : IRepository<RmsExportTemplate>
	{
		Task<RmsExportTemplate> GetByIdForDepartmentAsync(int departmentId, string templateId);

		Task<RmsExportTemplate> GetByKeyAsync(int departmentId, string templateKey);

		Task<IEnumerable<RmsExportTemplate>> GetForDepartmentAsync(int departmentId);

		/// <summary>Enabled, scheduled templates whose NextRunOn is at or before <paramref name="utcNow"/>, across departments, oldest first.</summary>
		Task<IEnumerable<RmsExportTemplate>> GetDueAsync(DateTime utcNow, int take);

		Task<bool> TryBumpRowVersionAsync(int departmentId, string templateId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsExportRunsRepository : IRepository<RmsExportRun>
	{
		/// <summary>Metadata only (no Data column).</summary>
		Task<RmsExportRun> GetByIdForDepartmentAsync(int departmentId, string runId);

		/// <summary>The run with its bytes.</summary>
		Task<RmsExportRun> GetWithDataAsync(int departmentId, string runId);

		Task<IEnumerable<RmsExportRun>> GetForTemplateAsync(int departmentId, string templateId, int take);

		Task<int> DeleteExpiredAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
