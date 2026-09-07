using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Outcome of a protected reveal (RMS plan section 5.9.3): the keyed plaintext for the reveal module, or the reason it was refused.</summary>
	public class RecordRevealResult
	{
		public bool Success { get; set; }
		/// <summary>step_up_required, grant_expired, grant_revoked, protected_access_denied, broker_unavailable.</summary>
		public string Error { get; set; }
		/// <summary>"{table}.{column}:{rowId}" -> plaintext; the same keys the data-adp-field markers carry.</summary>
		public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
	}

	/// <summary>
	/// The reveal endpoints' shared logic (Web MVC and v4): hydrate through the seam with the ambient grant, key every
	/// cataloged column by table.column:rowId, withhold restricted columns without RecordRestricted_View, and audit.
	/// </summary>
	public interface IRecordsRevealService
	{
		Task<RecordRevealResult> RevealRecordAsync(int departmentId, string userId, RecordAggregate aggregate, bool canViewRestricted, string ipAddress);
		Task<RecordRevealResult> RevealIncidentAsync(int departmentId, string userId, IncidentReportAggregate aggregate, bool canViewRestricted, string ipAddress);
	}
}
