using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Department saved reports (RMS plan section 4.1, RMS-1B). Managing needs ManageRecordReports; running honors the runner's group scope and restricted permission.</summary>
	public interface IRecordSavedReportsService
	{
		Task<List<RmsSavedReportDefinition>> GetForDepartmentAsync(int departmentId);
		Task<RmsSavedReportDefinition> GetAsync(int departmentId, string reportId);
		Task<RecordReportValidation> ValidateAsync(int departmentId, RmsSavedReportDefinition report);
		Task<RmsSavedReportDefinition> SaveAsync(int departmentId, string userId, RmsSavedReportDefinition report, CancellationToken cancellationToken = default);
		/// <summary>Pass the row version the caller saw to reject a delete that would discard somebody else's edit.</summary>
		Task<bool> DeleteAsync(int departmentId, string userId, string reportId, long? expectedRowVersion = null, CancellationToken cancellationToken = default);
		Task<RecordReportResult> RunAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default);
		/// <summary>RFC 4180 CSV of a run (formula-guarded), for download.</summary>
		string ToCsv(RecordReportResult result);
	}
}
