using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Pre-activation legacy Log/UnitLog counts (RMS plan section 4.1). Read-only; no legacy row is ever mutated.</summary>
	public class RmsLegacyStats
	{
		public int LogCount { get; set; }
		/// <summary>Logs rows with no StationGroupId; they stay department-wide under group scoping (plan 5.7.1).</summary>
		public int LogsWithoutGroupCount { get; set; }
		public int EventTypeLogCount { get; set; }
		public int UnitLogCount { get; set; }
		public int MaxLogId { get; set; }
		public int MaxUnitLogId { get; set; }
	}

	public interface IRmsLegacyStatsRepository
	{
		Task<RmsLegacyStats> GetLegacyStatsAsync(int departmentId);
	}
}
