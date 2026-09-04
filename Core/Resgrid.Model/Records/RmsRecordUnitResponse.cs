using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Unit response on a Record (LogUnit parity): unit snapshot plus dispatched/en-route/on-scene/
	/// released/in-quarters times. Every prefilled time is freely editable in Draft; the originally
	/// prefilled values and their <see cref="RmsSourceKind"/> are retained in <see cref="PrefillJson"/>
	/// so provenance is never lost (RMS plan section 4.2).
	/// </summary>
	[Table("RmsRecordUnitResponses")]
	public class RmsRecordUnitResponse : IEntity
	{
		public string RmsRecordUnitResponseId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		public string RevisionId { get; set; }

		public int UnitId { get; set; }

		public string UnitNameSnapshot { get; set; }

		public string UnitTypeSnapshot { get; set; }

		public int? StationGroupIdSnapshot { get; set; }

		public DateTime? Dispatched { get; set; }

		public DateTime? Enroute { get; set; }

		public DateTime? OnScene { get; set; }

		public DateTime? Released { get; set; }

		public DateTime? InQuarters { get; set; }

		/// <summary><see cref="RmsSourceKind"/> the time set was prefilled from; None when entered by the author.</summary>
		public int TimesSourceKind { get; set; }

		/// <summary>Server-authored JSON of the originally prefilled times and their sources; never client-authored.</summary>
		public string PrefillJson { get; set; }

		public int Ordinal { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordUnitResponseId; }
			set { RmsRecordUnitResponseId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRecordUnitResponses";

		[NotMapped]
		public string IdName => "RmsRecordUnitResponseId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
