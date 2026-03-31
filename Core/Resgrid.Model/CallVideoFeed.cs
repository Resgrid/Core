using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	[Table("CallVideoFeeds")]
	public class CallVideoFeed : IEntity
	{
		public string CallVideoFeedId { get; set; }

		public int CallId { get; set; }

		public int DepartmentId { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Url { get; set; }

		public int? FeedType { get; set; }

		public int? FeedFormat { get; set; }

		public string Description { get; set; }

		public int Status { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime AddedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public int SortOrder { get; set; }

		public bool IsDeleted { get; set; }

		public string DeletedByUserId { get; set; }

		public DateTime? DeletedOn { get; set; }

		public bool IsFlagged { get; set; }

		public string FlaggedReason { get; set; }

		public string FlaggedByUserId { get; set; }

		public DateTime? FlaggedOn { get; set; }

		[ForeignKey("CallId")]
		public virtual Call Call { get; set; }

		[NotMapped]
		public string TableName => "CallVideoFeeds";

		[NotMapped]
		public string IdName => "CallVideoFeedId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallVideoFeedId; }
			set { CallVideoFeedId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Call" };
	}
}
