using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// RMS access audit row (RMS plan section 5.8): read/search/change/sign/print/export/submit/share/
	/// support/denied action with actor, purpose and correlation. <see cref="DetailJson"/> never holds a
	/// protected-candidate value.
	/// </summary>
	[Table("RmsAccessAudits")]
	public class RmsAccessAudit : IEntity
	{
		public long RmsAccessAuditId { get; set; }

		public int DepartmentId { get; set; }

		public string RecordId { get; set; }

		public string RevisionId { get; set; }

		/// <summary><see cref="RmsAccessAuditAction"/>.</summary>
		public int Action { get; set; }

		public string ActorUserId { get; set; }

		public string Purpose { get; set; }

		public string CorrelationId { get; set; }

		/// <summary><see cref="RmsOriginClient"/>.</summary>
		public int OriginClient { get; set; }

		public string IpAddress { get; set; }

		public bool Successful { get; set; }

		public DateTime OccurredOn { get; set; }

		public string DetailJson { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsAccessAuditId; }
			set { RmsAccessAuditId = Convert.ToInt64(value); }
		}

		[NotMapped]
		public string TableName => "RmsAccessAudits";

		[NotMapped]
		public string IdName => "RmsAccessAuditId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
