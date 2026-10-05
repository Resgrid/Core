using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The run and callback reports written for a call, for an author working on that call's incident report (or another
	/// run report) to read and pull data from. Every report goes through the same per-record visibility check as the
	/// Records queue, and each one shown is recorded in its access audit.
	/// </summary>
	public interface IRecordCallReportsService
	{
		/// <summary>
		/// The call's run reports the user may view, newest first; voided, cancelled and purged ones are left out.
		/// <paramref name="excludeRecordId"/> leaves out the record being edited. <paramref name="purpose"/> is written to
		/// each shown record's access audit.
		/// </summary>
		Task<List<CallRunReport>> GetForCallAsync(int departmentId, string userId, int callId, string excludeRecordId, string purpose, RmsOriginClient origin = RmsOriginClient.Web);
	}
}
