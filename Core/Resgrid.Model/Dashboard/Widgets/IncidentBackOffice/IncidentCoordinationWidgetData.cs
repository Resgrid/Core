using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// ibo.coordination (plan §7.10): the department's coordination boards (the boards it runs and shares with partners), newest
	/// activity first, with stale lanes and active partner shares. Board names and counts only.
	/// </summary>
	public sealed class IncidentCoordinationWidgetData
	{
		public bool ReadOnly { get; set; }

		public int Boards { get; set; }

		public int ActiveShares { get; set; }

		public int StaleLanes { get; set; }

		public List<IncidentCoordinationBoardSummaryRow> Rows { get; set; } = new List<IncidentCoordinationBoardSummaryRow>();
	}

	public sealed class IncidentCoordinationBoardSummaryRow
	{
		public string BoardId { get; set; }

		public string Name { get; set; }

		/// <summary>The incident the board belongs to; null for a department board.</summary>
		public string ActivationId { get; set; }

		public bool IsFrozen { get; set; }

		public int Lanes { get; set; }

		public int StaleLanes { get; set; }

		public int ActiveShares { get; set; }

		public DateTime LastUpdateOn { get; set; }
	}
}
