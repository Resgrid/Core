using System;
using System.Collections.Generic;

namespace Resgrid.Model.IncidentLogistics
{
	/// <summary>
	/// The department's coordination boards for the home dashboard Coordination Boards widget (home-dashboard-redesign-plan.md §7.10):
	/// board, lane and share-grant counts, newest activity first. Names and counts only; lane filters and share tokens never leave it.
	/// </summary>
	public sealed class IncidentCoordinationSummary
	{
		public int Boards { get; set; }

		/// <summary>Unrevoked, unexpired partner share grants on those boards.</summary>
		public int ActiveShares { get; set; }

		/// <summary>Lanes whose source went stale (StaleSince set).</summary>
		public int StaleLanes { get; set; }

		public List<IncidentCoordinationBoardRow> Recent { get; set; } = new List<IncidentCoordinationBoardRow>();
	}

	public sealed class IncidentCoordinationBoardRow
	{
		public string CoordinationBoardId { get; set; }
		public string Name { get; set; }
		public string IncidentActivationId { get; set; }
		public int Scope { get; set; }
		public bool IsFrozen { get; set; }
		public int Lanes { get; set; }
		public int StaleLanes { get; set; }
		public int ActiveShares { get; set; }

		/// <summary>The newest of the board's edit and its lanes' last source update.</summary>
		public DateTime LastUpdateOn { get; set; }
	}
}
