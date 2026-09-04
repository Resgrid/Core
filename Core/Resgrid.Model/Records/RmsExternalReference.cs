using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Versioned correlation from a Record to a source subsystem entity (Call, Inventory usage, Checklist
	/// completion, Run Card activation, external order/request/fill, ...) with identifier scheme, source
	/// version/event, capture time, provenance and checksum (cross-plan contract RmsExternalReference).
	/// Never a capability-bearing download link; <see cref="SafeUrl"/> is display only.
	/// </summary>
	[Table("RmsExternalReferences")]
	public class RmsExternalReference : IEntity
	{
		public string RmsExternalReferenceId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		/// <summary><see cref="RmsRecordKind"/>.</summary>
		public int RecordKind { get; set; }

		public string SourceSubsystem { get; set; }

		public string SourceEntityType { get; set; }

		/// <summary>Opaque, source-qualified identifier; never parsed for meaning.</summary>
		public string SourceEntityId { get; set; }

		public string IdentifierScheme { get; set; }

		public string SourceVersion { get; set; }

		public string SourceEventId { get; set; }

		/// <summary>Subject, Evidence, LinkedCall, InventoryUsage, ...</summary>
		public string SemanticRole { get; set; }

		public DateTime CapturedOn { get; set; }

		public string CapturedByUserId { get; set; }

		public string Checksum { get; set; }

		public string SafeUrl { get; set; }

		/// <summary>Bounded, server-authored display snapshot; never a second source of truth.</summary>
		public string SnapshotJson { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsExternalReferenceId; }
			set { RmsExternalReferenceId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsExternalReferences";

		[NotMapped]
		public string IdName => "RmsExternalReferenceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
