using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The RMS-3 source feeds that prefill an incident report beside the Call itself: command key times and
	/// the contact/preplan snapshot (RMS plan section 6, RMS-3). Each returns a snapshot with provenance and
	/// never a live reference; a feed that cannot be read returns null rather than failing the report.
	/// </summary>
	public interface IIncidentSourceFeedService
	{
		/// <summary>Null when no Incident Command was established for the Call.</summary>
		Task<IncidentCommandKeyTimes> GetCommandKeyTimesAsync(int departmentId, int callId);

		/// <summary>Contacts linked to the Call and its destination place; empty (never null) when nothing is linked.</summary>
		Task<IncidentPreplanSnapshot> GetPreplanSnapshotAsync(int departmentId, Call call);
	}
}
