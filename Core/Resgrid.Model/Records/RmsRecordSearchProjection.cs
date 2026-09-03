using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// RecordSearchProjectionV1: the derived, rebuildable, safe search row per Record (RMS plan sections
	/// 5.3/5.10). Holds scope, identity, number/type/state, safe dates, station/group, Call correlation,
	/// authorization hints and <see cref="SearchText"/> limited to fields whose classification and
	/// Searchable flag currently permit it. Never narrative, address detail, restricted sections,
	/// ciphertext or attachment content. Keyed by the Record id.
	/// </summary>
	[Table("RmsRecordSearchProjections")]
	public class RmsRecordSearchProjection : IEntity
	{
		public const int CurrentProjectionVersion = 1;

		public string RmsRecordSearchProjectionId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary><see cref="RmsSearchSourceType"/>.</summary>
		public int SourceType { get; set; }

		public string SourceId { get; set; }

		public int RecordKind { get; set; }

		public string RecordNumber { get; set; }

		public string DraftReference { get; set; }

		public string DefinitionKey { get; set; }

		public int DefinitionVersion { get; set; }

		public int? RecordType { get; set; }

		public int State { get; set; }

		public DateTime? OccurredOn { get; set; }

		public DateTime RecordCreatedOn { get; set; }

		public DateTime? FinalizedOn { get; set; }

		public int? StationGroupId { get; set; }

		public int? CallId { get; set; }

		public string CallNumber { get; set; }

		public string AuthorUserId { get; set; }

		public string OwnerUserId { get; set; }

		public string ReviewerUserId { get; set; }

		/// <summary>Comma-separated participant user ids (always-visible rule, plan section 5.7.1).</summary>
		public string ParticipantUserIds { get; set; }

		/// <summary>Comma-separated responding unit ids.</summary>
		public string UnitIds { get; set; }

		/// <summary>Comma-separated group ids from RmsRecordGroupScopes.</summary>
		public string GroupScopeIds { get; set; }

		public string DisplaySummary { get; set; }

		public string SearchText { get; set; }

		public bool IsLegacy { get; set; }

		public int ProjectionVersion { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public long PolicyEpoch { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordSearchProjectionId; }
			set { RmsRecordSearchProjectionId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRecordSearchProjections";

		[NotMapped]
		public string IdName => "RmsRecordSearchProjectionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Per-department index generation key for the RMS-owned records index (plan section 5.10).</summary>
	[Table("RmsSearchIndexStates")]
	public class RmsSearchIndexState : IEntity
	{
		public const string RecordsIndexName = "records";

		public int RmsSearchIndexStateId { get; set; }

		public int DepartmentId { get; set; }

		public string IndexName { get; set; }

		public int SchemaVersion { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public long PolicyEpoch { get; set; }

		/// <summary>Generation key text: {schemaVersion}.{protectedCatalogVersion}.{policyEpoch}.</summary>
		public string Generation { get; set; }

		public int State { get; set; }

		public int DocumentCount { get; set; }

		public DateTime? LastRebuiltOn { get; set; }

		public DateTime? LastIndexedModifiedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public static string BuildGeneration(int schemaVersion, int protectedCatalogVersion, long policyEpoch)
		{
			return $"{schemaVersion}.{protectedCatalogVersion}.{policyEpoch}";
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsSearchIndexStateId; }
			set { RmsSearchIndexStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RmsSearchIndexStates";

		[NotMapped]
		public string IdName => "RmsSearchIndexStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
