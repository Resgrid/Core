namespace Resgrid.Model
{
	/// <summary>
	/// How closest-unit dispatch compares candidates: the run card engine's closest-unit picks and the nearest-unit board
	/// both follow it (DispatchRecommendationConfig.ClosestUnitRanking).
	/// </summary>
	public enum ClosestUnitRankingModes
	{
		/// <summary>
		/// The fastest response: turnout plus travel, with routed drive times for the shortlist when routed ETAs are on.
		/// The default.
		/// </summary>
		FastestResponse = 0,

		/// <summary>The shortest straight-line distance to the call. Turnout and drive times are shown but don't reorder anything.</summary>
		ShortestDistance = 1
	}
}
