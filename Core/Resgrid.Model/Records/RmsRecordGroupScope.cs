using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One materialized (RecordId, DepartmentGroupId, AnchorType) visibility row (RMS plan section 5.7.1),
	/// recomputed in-transaction on every save/finalize/amend/participant/unit change. The join target
	/// for every list, search, report, export and sync query under group-scoped visibility.
	/// </summary>
	[Table("RmsRecordGroupScopes")]
	public class RmsRecordGroupScope : IEntity
	{
		public long RmsRecordGroupScopeId { get; set; }

		public int DepartmentId { get; set; }

		public string RecordId { get; set; }

		public int DepartmentGroupId { get; set; }

		/// <summary><see cref="RmsGroupScopeAnchorType"/>.</summary>
		public int AnchorType { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordGroupScopeId; }
			set { RmsRecordGroupScopeId = Convert.ToInt64(value); }
		}

		[NotMapped]
		public string TableName => "RmsRecordGroupScopes";

		[NotMapped]
		public string IdName => "RmsRecordGroupScopeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Explicit, audited, optionally time-boxed grant of one Record to one further group (Record_Share).
	/// Additive only; never widens a restricted section.
	/// </summary>
	[Table("RmsRecordShares")]
	public class RmsRecordShare : IEntity
	{
		public string RmsRecordShareId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		public int DepartmentGroupId { get; set; }

		public string GrantedByUserId { get; set; }

		public DateTime GrantedOn { get; set; }

		public string Reason { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime? RevokedOn { get; set; }

		public string RevokedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public bool IsEffective(DateTime utcNow)
		{
			return !RevokedOn.HasValue && (!ExpiresOn.HasValue || ExpiresOn.Value > utcNow);
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordShareId; }
			set { RmsRecordShareId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRecordShares";

		[NotMapped]
		public string IdName => "RmsRecordShareId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
