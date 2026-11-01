using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>trainings.mine (plan §7.6): trainings assigned to the member that are not complete (or whose quiz was failed), soonest due first.</summary>
	public sealed class TrainingsMineWidgetData
	{
		public int OutstandingCount { get; set; }

		public int OverdueCount { get; set; }

		public List<TrainingsMineItem> Items { get; set; } = new List<TrainingsMineItem>();
	}

	public sealed class TrainingsMineItem
	{
		public int TrainingId { get; set; }

		public string Name { get; set; }

		/// <summary>The training's "to be completed by" date, when set.</summary>
		public DateTime? DueUtc { get; set; }

		public bool IsOverdue { get; set; }

		/// <summary>The member opened it.</summary>
		public bool Viewed { get; set; }

		/// <summary>Completed, but the quiz score is below the training's minimum.</summary>
		public bool QuizFailed { get; set; }

		public string Url { get; set; }
	}
}
