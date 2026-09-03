using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Source-agnostic activity feed for the department reports (RMS plan section 4.10). Legacy Logs remain the
	/// pre-cutover history and finalized Records are the post-cutover history; both are returned in one shape so
	/// report totals reconcile across activation. Records are filtered by the viewer's group scope exactly as the
	/// Records queue is, so a report never discloses a record the viewer could not open.
	/// </summary>
	public interface IRecordsReportingService
	{
		Task<List<ReportActivityEntry>> GetActivityAsync(int departmentId, string viewerUserId, RmsOperationalRecordType type, DateTime start, DateTime end);

		/// <summary>Every legacy Log and finalized Record linked to one call, any type.</summary>
		Task<List<ReportActivityEntry>> GetCallActivityAsync(int departmentId, string viewerUserId, int callId);
	}
}
