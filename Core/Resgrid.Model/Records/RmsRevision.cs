using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One immutable, checksummed revision of a Record (RMS plan sections 4.8/5.2). Written by finalize,
	/// amend and void; pinned to the definition version it was authored under; references the prior
	/// revision. <see cref="SnapshotJson"/> is the complete server-authored snapshot (header, typed
	/// details, participants, units, attachment metadata); diffs are computed from two snapshots and
	/// never stored. Self-referencing through <see cref="PriorRevisionId"/> only as a plain column, never
	/// a navigation collection, because of the RepositoryBase cascade landmine (plan section 5.11.1).
	/// </summary>
	[Table("RmsRevisions")]
	public class RmsRevision : IEntity
	{
		public string RmsRevisionId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		/// <summary><see cref="RmsRecordKind"/>.</summary>
		public int RecordKind { get; set; }

		public int RevisionNumber { get; set; }

		/// <summary><see cref="RmsRevisionTransition"/>.</summary>
		public int Transition { get; set; }

		public string PriorRevisionId { get; set; }

		public string DefinitionKey { get; set; }

		public int DefinitionVersion { get; set; }

		public string SnapshotJson { get; set; }

		/// <summary>Lower-case hex SHA-256 of <see cref="SnapshotJson"/>.</summary>
		public string Checksum { get; set; }

		public string ActorUserId { get; set; }

		public string ActorRoleSnapshot { get; set; }

		/// <summary>Required for amend and void; null for the first finalization.</summary>
		public string ReasonCode { get; set; }

		public string ReasonText { get; set; }

		/// <summary>Version of the attestation statement the actor accepted, when the transition attests.</summary>
		public string AttestationStatementVersion { get; set; }

		public DateTime? AttestedOn { get; set; }

		/// <summary><see cref="RmsOriginClient"/>.</summary>
		public int OriginClient { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRevisionId; }
			set { RmsRevisionId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRevisions";

		[NotMapped]
		public string IdName => "RmsRevisionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
