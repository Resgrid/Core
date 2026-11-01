using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.qualityReview (plan §7.8): pending quality reviews the viewer may open, theirs first.</summary>
	public sealed class RecordsQualityReviewWidgetData
	{
		public int Pending { get; set; }

		/// <summary>Pending reviews whose reviewer is the viewer.</summary>
		public int AssignedToMe { get; set; }

		/// <summary>The pending queue was read to its cap; counts are a lower bound.</summary>
		public bool HasMore { get; set; }

		public List<RecordsQualityReviewItem> Items { get; set; } = new List<RecordsQualityReviewItem>();
	}

	public sealed class RecordsQualityReviewItem
	{
		/// <summary>Opens /User/RecordsQuality/Review?id={ReviewId}.</summary>
		public string ReviewId { get; set; }

		public string RecordId { get; set; }

		public string RecordNumber { get; set; }

		public string DefinitionKey { get; set; }

		public DateTime SampledOn { get; set; }

		public bool AssignedToMe { get; set; }
	}
}
