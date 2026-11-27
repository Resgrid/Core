using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A protocol's text as it was when it was attached to a call (M0337). Stored once per distinct protocol and
	/// <see cref="ContentHash"/>, however many calls cite it; a call's CallProtocol row carries the hash. Interim until
	/// protocol versions (P1), which are built from these rows.
	/// </summary>
	[Table("DispatchProtocolSnapshots")]
	public class DispatchProtocolSnapshot : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolSnapshotId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int DispatchProtocolId { get; set; }

		/// <summary><see cref="ProtocolContentHash.Compute"/> of <see cref="ProtocolText"/>.</summary>
		[Required]
		[MaxLength(64)]
		public string ContentHash { get; set; }

		/// <summary>The sanitized protocol text exactly as stored when it was first attached.</summary>
		public string ProtocolText { get; set; }

		/// <summary>When a call first used this text.</summary>
		public DateTime FirstUsedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatchProtocolSnapshotId; }
			set { DispatchProtocolSnapshotId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DispatchProtocolSnapshots";

		[NotMapped]
		public string IdName => "DispatchProtocolSnapshotId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
