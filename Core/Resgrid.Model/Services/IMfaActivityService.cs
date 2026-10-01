using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The account's recent verifications (plan section 6.5). Successes are recorded where evidence is written; denials where
	/// each surface refuses a factor. Recording never fails the verification it describes.
	/// </summary>
	public interface IMfaActivityService
	{
		Task RecordAsync(MfaActivityEntry entry, CancellationToken cancellationToken = default);

		/// <summary>The user's activity within retention, newest first.</summary>
		Task<IReadOnlyList<MfaActivity>> GetRecentAsync(string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// "This wasn't me": marks the user's own activity reported, ends the session it opened or served (unless it is the
		/// reporting session), audits, and sends the security notice (plan section 6.4).
		/// </summary>
		Task<MfaActivityReport> ReportAsync(string userId, string mfaActivityId, string reportingSessionId, SharedSessionRequestInfo request,
			CancellationToken cancellationToken = default);
	}
}
