using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Search
{
	/// <summary>
	/// The safe, rebuildable search row for one entity in the global index (Unified Search plan R2.3, R3). One table
	/// with an entity-type discriminator rather than one per family: the sweep, keyset checkpoint, rebuild and erasure
	/// logic are then written once. Only allowlisted fields land here; cataloged (protected) columns are included only
	/// for a department where protection is not enforced (R2.15), and a value carrying a protected-data envelope or
	/// the redaction placeholder is never written. Keyed by (DepartmentId, EntityType, EntityId).
	/// </summary>
	[Table("SearchProjections")]
	public class SearchProjection : IEntity
	{
		public string SearchProjectionId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="SearchEntityTypes"/>.</summary>
		public string EntityType { get; set; }

		public string EntityId { get; set; }

		public string Title { get; set; }

		public string Summary { get; set; }

		/// <summary>Analyzed free text (never stored in the index).</summary>
		public string SearchText { get; set; }

		/// <summary>Identifiers worth exact and prefix matching: call number, incident number, callsign, id number.</summary>
		public string Keywords { get; set; }

		public string Category { get; set; }

		public string Status { get; set; }

		public int? Priority { get; set; }

		public int? GroupId { get; set; }

		public string OwnerUserId { get; set; }

		/// <summary>Comma-separated user ids that may see the row regardless of other rules (message recipients, deployment roster).</summary>
		public string ParticipantUserIds { get; set; }

		public bool IsAdminOnly { get; set; }

		public bool IsActive { get; set; }

		public DateTime OccurredOn { get; set; }

		/// <summary>Relative web path to open the entity.</summary>
		public string Url { get; set; }

		public string MetadataJson { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public long PolicyEpoch { get; set; }

		/// <summary>True when cataloged text columns were included because protection was not enforced at projection time.</summary>
		public bool IncludesProtectedText { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SearchProjectionId; }
			set { SearchProjectionId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "SearchProjections";

		[NotMapped]
		public string IdName => "SearchProjectionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		public static string BuildKey(int departmentId, string entityType, string entityId)
		{
			return departmentId + "|" + entityType + "|" + entityId;
		}
	}

	/// <summary>Per-index, per-department generation key and checkpoint for the shared host (plan R4 Phase 1b item 3).</summary>
	[Table("SearchIndexStates")]
	public class SearchIndexState : IEntity
	{
		public int SearchIndexStateId { get; set; }

		public string IndexName { get; set; }

		public int DepartmentId { get; set; }

		public int SchemaVersion { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public long PolicyEpoch { get; set; }

		/// <summary>{schemaVersion}.{protectedCatalogVersion}.{policyEpoch}</summary>
		public string Generation { get; set; }

		/// <summary><see cref="SearchIndexBuildState"/>.</summary>
		public int State { get; set; }

		public int DocumentCount { get; set; }

		public DateTime? LastRebuiltOn { get; set; }

		public DateTime? LastIndexedModifiedOn { get; set; }

		/// <summary>Set by the admin rebuild endpoint; the next sweep rebuilds the department and clears it.</summary>
		public DateTime? RebuildRequestedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return SearchIndexStateId; }
			set { SearchIndexStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "SearchIndexStates";

		[NotMapped]
		public string IdName => "SearchIndexStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// The database publish lease: the second guard (after the Recreate rollout strategy) that keeps two worker pods
	/// from publishing the same index prefix at once (plan R7 writer sequence step 2). One row per index name.
	/// </summary>
	[Table("SearchIndexLeases")]
	public class SearchIndexLease : IEntity
	{
		public string IndexName { get; set; }

		public string LeaseOwner { get; set; }

		public DateTime? LeaseExpiresOn { get; set; }

		public string LastPublishedRevision { get; set; }

		public DateTime? LastPublishedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IndexName; }
			set { IndexName = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "SearchIndexLeases";

		[NotMapped]
		public string IdName => "IndexName";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
