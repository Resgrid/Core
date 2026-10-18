using System;
using System.Collections.Generic;

namespace Resgrid.Model.Onboarding
{
	// JSON bodies stored in Import* text columns. They are versioned by the run's pinned engine version
	// (section 13.3): add members, never rename or repurpose them.

	/// <summary>The workspace's domain plan from step 3 "Choose what to bring" (plan section 6.3).</summary>
	public sealed class ImportDomainPlan
	{
		public List<ImportDomainChoice> Domains { get; set; } = new List<ImportDomainChoice>();
	}

	public sealed class ImportDomainChoice
	{
		/// <summary>Entity contract key.</summary>
		public string EntityKey { get; set; }
		/// <summary>Product keys the administrator said this data comes from.</summary>
		public List<string> ProductKeys { get; set; } = new List<string>();
		/// <summary>The product that owns the domain when more than one source offers it.</summary>
		public string OwnerProductKey { get; set; }
	}

	/// <summary>A dataset's column mapping (plan section 8.4). One binding per source column.</summary>
	public sealed class ImportColumnMapBody
	{
		public List<ImportColumnBinding> Columns { get; set; } = new List<ImportColumnBinding>();
		/// <summary>Constant values for target fields that have no source column.</summary>
		public Dictionary<string, string> Constants { get; set; } = new Dictionary<string, string>();
		/// <summary>Answers to per-column format questions ("dd/MM/yyyy", the source time zone).</summary>
		public Dictionary<string, string> Formats { get; set; } = new Dictionary<string, string>();
		/// <summary>The source column holding each row's source identity, if any.</summary>
		public string SourceIdentityColumn { get; set; }
		/// <summary>The 1-based header row; rows above it are ignored.</summary>
		public int HeaderRow { get; set; } = 1;
	}

	public sealed class ImportColumnBinding
	{
		public string SourceColumn { get; set; }
		/// <summary><see cref="ImportColumnBindingKind"/>.</summary>
		public int Kind { get; set; }
		/// <summary>Contract field key when Kind is Field or Reference.</summary>
		public string FieldKey { get; set; }
		/// <summary>Transform keys from the fixed transform catalog, applied in order.</summary>
		public List<string> Transforms { get; set; } = new List<string>();
		/// <summary>The referenced entity when Kind is Reference ("groups").</summary>
		public string ReferencesEntity { get; set; }
		/// <summary>How a reference matches: source-id, name or existing-record.</summary>
		public string ReferenceMatch { get; set; }
		/// <summary>Label for "keep as custom field".</summary>
		public string CustomFieldLabel { get; set; }
		public int Confidence { get; set; }
	}

	public enum ImportColumnBindingKind
	{
		Ignore = 0,
		Field = 1,
		Reference = 2,
		KeepAsCustomField = 3,
		SourceIdentity = 4,
		RetainAsSourceLabel = 5
	}

	/// <summary>One uploaded multipart part (plan section 8.7).</summary>
	public sealed class ImportArtifactPart
	{
		public int PartNumber { get; set; }
		/// <summary>Plaintext bytes in the part.</summary>
		public long Size { get; set; }
		/// <summary>SHA-256 of the plaintext chunk (hex).</summary>
		public string Sha256 { get; set; }
		/// <summary>The store's ETag for the encrypted part.</summary>
		public string ETag { get; set; }
	}

	/// <summary>Where a run resumes (plan section 11.4). Everything needed to resume lives here and in the claims.</summary>
	public sealed class ImportRunCursor
	{
		public int WaveIndex { get; set; }
		public string EntityKey { get; set; }
		public string DatasetId { get; set; }
		/// <summary>Highest staged row number fully processed in <see cref="DatasetId"/>.</summary>
		public int LastRowNumber { get; set; }
		/// <summary>Writer phase within an entity (two-pass writers, e.g. custom state next-status lists).</summary>
		public int Phase { get; set; }
		/// <summary>Extraction: endpoint and page token.</summary>
		public string Endpoint { get; set; }
		public string PageToken { get; set; }
		public long JournalSequence { get; set; }
		public List<string> CompletedEntities { get; set; } = new List<string>();
	}

	/// <summary>Versions pinned on a run so a resumed run behaves as the approved one (section 13.3, I7).</summary>
	public sealed class ImportRunVersions
	{
		public string Engine { get; set; }
		public string Catalog { get; set; }
		public Dictionary<string, string> Contracts { get; set; } = new Dictionary<string, string>();
		public string Pack { get; set; }
		public string Preset { get; set; }
	}

	/// <summary>Per-entity counters maintained by a run.</summary>
	public sealed class ImportRunCounts
	{
		public Dictionary<string, ImportEntityCounts> Entities { get; set; } = new Dictionary<string, ImportEntityCounts>();
		/// <summary>Plan capacity read once at commit start (null when unknown, section 11.6).</summary>
		public int? PersonnelCapacityRemaining { get; set; }
		public int? UnitCapacityRemaining { get; set; }
		/// <summary>Entity plans: personnel and units draw on one pool.</summary>
		public bool SharedCapacity { get; set; }
	}

	public sealed class ImportEntityCounts
	{
		public int Planned { get; set; }
		public int Created { get; set; }
		public int Updated { get; set; }
		public int Linked { get; set; }
		public int Unchanged { get; set; }
		public int Skipped { get; set; }
		public int Failed { get; set; }
	}

	/// <summary>The administrator's stated choices for L8 consequences (section 11.2.3).</summary>
	public sealed class ImportConsequenceDecisions
	{
		/// <summary>True (default): expired certifications imported as Expired with the notice marked sent.</summary>
		public bool SuppressExpiredCertificationNotices { get; set; } = true;
		/// <summary>True (default): role requirements imported WarnOnly until activation.</summary>
		public bool RelaxCertificationEnforcement { get; set; } = true;
		/// <summary>True (default): checklist schedules and maintenance plans created disabled/paused.</summary>
		public bool CreateSchedulesDisabled { get; set; } = true;
		/// <summary>Consequence ids the administrator confirmed in the preview.</summary>
		public List<string> Acknowledged { get; set; } = new List<string>();
	}

	/// <summary>
	/// Who a write acts as (plan section 17.8). Self-service: an administrator. Managed migration: staff
	/// operating on behalf of the authorizing administrator (or the managing user under a contract).
	/// </summary>
	public sealed class ImportAuthority
	{
		public int DepartmentId { get; set; }
		/// <summary><see cref="ImportActorKind"/>: Administrator or ResgridStaff.</summary>
		public int Kind { get; set; }
		/// <summary>The department user entity attribution fields use.</summary>
		public string OnBehalfOfUserId { get; set; }
		public string StaffOperator { get; set; }
		public string StaffChecker { get; set; }
		public string GrantId { get; set; }
		public string EngagementReference { get; set; }

		public bool IsStaff => Kind == (int)ImportActorKind.ResgridStaff;

		public static ImportAuthority ForAdministrator(int departmentId, string userId) => new ImportAuthority
		{
			DepartmentId = departmentId,
			Kind = (int)ImportActorKind.Administrator,
			OnBehalfOfUserId = userId
		};
	}

	/// <summary>A staff caller of the support and operator services (plan section 17.6).</summary>
	public sealed class StaffActor
	{
		public StaffActor(string identity, string role, string grantId = null)
		{
			Identity = identity;
			Role = role;
			GrantId = grantId;
		}

		/// <summary>Staff email or subject.</summary>
		public string Identity { get; }
		/// <summary>BackOffice role ("Backoffice.Support", "Backoffice.Migration", "Backoffice.SuperAdmin", "Backoffice.ReadOnly").</summary>
		public string Role { get; }
		public string GrantId { get; }

		public const string ReadOnlyRole = "Backoffice.ReadOnly";
		public const string SupportRole = "Backoffice.Support";
		public const string MigrationRole = "Backoffice.Migration";
		public const string SuperAdminRole = "Backoffice.SuperAdmin";

		public bool CanSupport => Role == SupportRole || Role == MigrationRole || Role == SuperAdminRole;
		public bool CanOperate => Role == MigrationRole || Role == SuperAdminRole;
		public bool IsSuperAdmin => Role == SuperAdminRole;
	}

	/// <summary>Column statistics from analysis (no raw values beyond a few sampled examples).</summary>
	public sealed class ImportDatasetProfile
	{
		public List<ImportColumnProfile> Columns { get; set; } = new List<ImportColumnProfile>();
		public int RowCount { get; set; }
		public bool Truncated { get; set; }
	}

	public sealed class ImportColumnProfile
	{
		public int Index { get; set; }
		public string Header { get; set; }
		/// <summary>Inferred field type key ("text", "integer", "date", "email", "phone", "boolean").</summary>
		public string InferredType { get; set; }
		public int FilledCount { get; set; }
		public int DistinctCount { get; set; }
		/// <summary>Up to three examples; omitted when the column looks personal or excluded.</summary>
		public List<string> Examples { get; set; } = new List<string>();
		public bool LooksSensitive { get; set; }
		public bool LooksExcluded { get; set; }
	}
}
