using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Kinds of leave of absence (R-C2). Append only.</summary>
	public enum MemberLeaveKinds
	{
		Other = 0,
		Medical = 1,
		Military = 2,
		LightDuty = 3,
		Parental = 4
	}

	/// <summary>
	/// A member's leave of absence (R-C2): a department-scoped date range an officer enters. While it runs, the member's
	/// training due dates and reminders pause; afterwards they are shifted by the leave's length. Named neutrally so T3
	/// learning requirements reuse it; if the Shifts plan adds a leave status, readers switch to that.
	/// </summary>
	[Table("MemberLeavePeriods")]
	public class MemberLeavePeriod : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int MemberLeavePeriodId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>Start of the first department-local day of leave, UTC.</summary>
		public DateTime StartsOn { get; set; }

		/// <summary>End of the last department-local day of leave, UTC.</summary>
		public DateTime EndsOn { get; set; }

		/// <summary><see cref="MemberLeaveKinds"/>.</summary>
		public int Kind { get; set; }

		public string Note { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return MemberLeavePeriodId; }
			set { MemberLeavePeriodId = (int)value; }
		}

		[NotMapped]
		public string TableName => "MemberLeavePeriods";

		[NotMapped]
		public string IdName => "MemberLeavePeriodId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>What a member can pin to My Training. Append only.</summary>
	public enum MemberPinnedItemKinds
	{
		/// <summary>A personnel certification record. RefId is the PersonnelCertificationId.</summary>
		Certification = 1
	}

	/// <summary>An item a member pinned to their My Training page (the Vector "pin to To Do" parity row).</summary>
	[Table("MemberPinnedItems")]
	public class MemberPinnedItem : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int MemberPinnedItemId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public int Kind { get; set; }

		public string RefId { get; set; }

		public DateTime PinnedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return MemberPinnedItemId; }
			set { MemberPinnedItemId = (int)value; }
		}

		[NotMapped]
		public string TableName => "MemberPinnedItems";

		[NotMapped]
		public string IdName => "MemberPinnedItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
