using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>
	/// trainings.bulletin (Training T0 item 6, Vector "bulletin board" parity): what is new in training for the member — trainings
	/// newly assigned to them (or whose scheduled start just passed), and open trainings recently published to the catalog.
	/// </summary>
	public sealed class TrainingsBulletinWidgetData
	{
		/// <summary>New assignments in the window that the member has not finished.</summary>
		public int NewAssignedCount { get; set; }

		/// <summary>Open (catalog) trainings published in the window that are not assigned to the member.</summary>
		public int CatalogCount { get; set; }

		public List<TrainingsBulletinItem> Items { get; set; } = new List<TrainingsBulletinItem>();
	}

	public sealed class TrainingsBulletinItem
	{
		public const string AssignedKind = "assigned";
		public const string CatalogKind = "catalog";

		public int TrainingId { get; set; }

		public string Name { get; set; }

		/// <summary><see cref="AssignedKind"/> or <see cref="CatalogKind"/>.</summary>
		public string Kind { get; set; }

		/// <summary>When it reached the member: the assignment (or the scheduled start, if later), or the catalog publication.</summary>
		public DateTime PublishedUtc { get; set; }

		/// <summary>The member's due moment (assignments only), UTC.</summary>
		public DateTime? DueUtc { get; set; }

		public bool IsOverdue { get; set; }

		/// <summary>A catalog training the member may enrol in.</summary>
		public bool CanSelfEnroll { get; set; }

		public string Url { get; set; }
	}
}
