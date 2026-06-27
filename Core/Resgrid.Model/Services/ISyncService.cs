using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Aggregates the offline shift-start REFERENCE data set (department configuration + a safe personnel roster) into
	/// a single payload, so an IC/Unit app can pull everything it needs to start and run an incident in one round-trip.
	/// The live per-incident state is delivered separately by IIncidentCommandService (board bundle + change deltas).
	/// </summary>
	public interface ISyncService
	{
		Task<SyncReferenceData> GetReferenceDataAsync(int departmentId);
	}
}
