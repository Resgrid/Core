using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The versioned Field Records client contract (RMS plan RMS-1D "FieldRecordCatalogV1"): what a field app may
	/// assume the server speaks. Everything a client renders, validates, queues or syncs is named here so the shared
	/// golden fixtures can pin it; a client that reports an unknown control or rule is refused for authoring and may
	/// still read. Constants only; the catalog itself is computed per request by <see cref="Services.IFieldRecordsService"/>.
	/// </summary>
	public static class FieldRecordCatalogV1
	{
		public const string ContractVersion = "field-catalog.v1";
		public const string SyncContractVersion = "field-sync.v1";
		public const string PrefillContractVersion = "field-prefill.v1";

		/// <summary>Launch contexts a definition surface may name; "none" is the app's Field Records home.</summary>
		public static class LaunchContexts
		{
			public const string None = "none";
			public const string Call = "call";
			public const string Unit = "unit";
			public const string Contact = "contact";
			public const string Checklist = "checklist";
			public const string WorkOrder = "workorder";
			public const string Command = "command";
			public static readonly IReadOnlyList<string> All = new[] { None, Call, Unit, Contact, Checklist, WorkOrder, Command };
		}

		/// <summary>Why a definition is withheld from a catalog. Codes only: never a reason a forged client could act on.</summary>
		public static class ExclusionReasons
		{
			public const string OriginNotField = "origin_not_field";
			public const string ModuleDisabled = "module_disabled";
			public const string RecordsNotUsable = "records_not_usable";
			public const string AppDisabled = "app_disabled";
			public const string AppVersionTooOld = "app_version_too_old";
			public const string NotMember = "not_member";
			public const string SurfaceNotEnabled = "surface_not_enabled";
			public const string ContextNotAllowed = "context_not_allowed";
			public const string ContextNotVerified = "context_not_verified";
			public const string CapabilityUnsupported = "capability_unsupported";
			public const string Retired = "retired";
			public const string NotPublished = "not_published";
			public const string ProtectedDataUnavailable = "protected_data_unavailable";
			public static readonly IReadOnlyList<string> All = new[]
			{
				OriginNotField, ModuleDisabled, RecordsNotUsable, AppDisabled, AppVersionTooOld, NotMember, SurfaceNotEnabled, ContextNotAllowed, ContextNotVerified,
				CapabilityUnsupported, Retired, NotPublished, ProtectedDataUnavailable
			};
		}

		/// <summary>Controls the contract expects every adapter to render (the RMS-1B/1C field catalog).</summary>
		public static readonly IReadOnlyList<string> SupportedControls = Enum.GetNames(typeof(RmsFieldType));

		/// <summary>Rule effects and operators an adapter must evaluate client-side exactly as the server does.</summary>
		public static readonly IReadOnlyList<string> SupportedRuleEffects = Enum.GetNames(typeof(RmsRuleEffect));
		public static readonly IReadOnlyList<string> SupportedRuleOperators = Enum.GetNames(typeof(RmsRuleOperator));

		/// <summary>Lifecycle actions a field client may issue. Amendment, void and approval stay Web-first (plan RMS-1D).</summary>
		public static readonly IReadOnlyList<string> LifecycleActions = new[] { "create", "save", "submit-for-review", "finalize", "cancel", "acknowledge-assignment", "complete-assignment" };

		/// <summary>Sync states a client reports and the server can answer with.</summary>
		public static readonly IReadOnlyList<string> SyncStates = new[] { "fresh", "delta", "reset-required", "conflict" };

		/// <summary>Conflict kinds a client must present explicitly rather than replay silently.</summary>
		public static readonly IReadOnlyList<string> ConflictKinds = new[] { "etag", "permission", "scope", "definition-retired", "protected-data", "app-version" };

		/// <summary>Prefill sources a definition surface's prefill map may name (server-calculated, provenance-stamped).</summary>
		public static readonly IReadOnlyList<string> PrefillSources = new[]
		{
			"call.id", "call.number", "call.name", "call.nature", "call.address", "call.geolocation", "call.logged_on",
			"unit.id", "unit.name", "group.id", "group.name", "user.id", "command.name", "command.commander", "now"
		};

		/// <summary>
		/// Locked system definitions a field app may start without a department surface (the "Field-ready starter
		/// allowlist"): Responder authors self/assignment records, Unit authors apparatus activity, IC and Dispatch
		/// author Call-bound run records. A department definition reaches an app only through its client surface.
		/// </summary>
		public static IReadOnlyList<string> LockedStarterAllowlist(RmsOriginClient origin)
		{
			switch (origin)
			{
				case RmsOriginClient.Responder: return new[] { RmsDefinitionKeys.Training, RmsDefinitionKeys.Meeting, RmsDefinitionKeys.Work };
				case RmsOriginClient.Unit: return new[] { RmsDefinitionKeys.UnitActivity, RmsDefinitionKeys.Run };
				case RmsOriginClient.IncidentCommand: return new[] { RmsDefinitionKeys.Run };
				case RmsOriginClient.Dispatch: return new[] { RmsDefinitionKeys.Callback, RmsDefinitionKeys.Run };
				default: return Array.Empty<string>();
			}
		}

		/// <summary>The launch contexts a locked starter definition accepts.</summary>
		public static IReadOnlyList<string> LockedLaunchContexts(string definitionKey)
		{
			switch (definitionKey)
			{
				case RmsDefinitionKeys.Run: return new[] { LaunchContexts.Call, LaunchContexts.Command };
				case RmsDefinitionKeys.UnitActivity: return new[] { LaunchContexts.Unit, LaunchContexts.Call, LaunchContexts.None };
				case RmsDefinitionKeys.Callback: return new[] { LaunchContexts.Call, LaunchContexts.None };
				default: return new[] { LaunchContexts.None };
			}
		}

		public static bool IsFieldOrigin(RmsOriginClient origin)
			=> origin == RmsOriginClient.Responder || origin == RmsOriginClient.Unit || origin == RmsOriginClient.IncidentCommand || origin == RmsOriginClient.Dispatch;

		/// <summary>Numeric dotted-version compare ("1.2.10" &gt; "1.2.9"); a null or blank side sorts lowest; non-numeric segments compare ordinally.</summary>
		public static int CompareVersions(string left, string right)
		{
			var a = Segments(left);
			var b = Segments(right);
			for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
			{
				var x = i < a.Count ? a[i] : "0";
				var y = i < b.Count ? b[i] : "0";
				if (int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var xi) && int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yi))
				{
					if (xi != yi) return xi.CompareTo(yi);
					continue;
				}
				var ordinal = string.CompareOrdinal(x, y);
				if (ordinal != 0) return ordinal;
			}
			return 0;
		}

		/// <summary>True when the app version meets the minimum; an unset minimum passes, an unset app version never does.</summary>
		public static bool MeetsMinimum(string appVersion, string minimum)
		{
			if (string.IsNullOrWhiteSpace(minimum)) return true;
			if (string.IsNullOrWhiteSpace(appVersion)) return false;
			return CompareVersions(appVersion, minimum) >= 0;
		}

		private static List<string> Segments(string version)
		{
			var text = (version ?? string.Empty).Trim();
			if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
			var plus = text.IndexOf('+');
			if (plus >= 0) text = text.Substring(0, plus);
			var dash = text.IndexOf('-');
			if (dash >= 0) text = text.Substring(0, dash);
			return text.Length == 0 ? new List<string>() : text.Split('.').Select(s => s.Trim()).ToList();
		}
	}

	/// <summary>The verified field context a request runs in: identifiers only, every one checked server-side.</summary>
	public class FieldRecordContext
	{
		public int? CallId { get; set; }
		public int? UnitId { get; set; }
		public int? GroupId { get; set; }
		/// <summary>IC app: the command role the user claims on the Call; verified against the active command.</summary>
		public string CommandRole { get; set; }
		public int? ContactId { get; set; }

		public bool IsEmpty => !CallId.HasValue && !UnitId.HasValue && !GroupId.HasValue && string.IsNullOrWhiteSpace(CommandRole) && !ContactId.HasValue;

		/// <summary>The launch context kind this context represents for catalog filtering.</summary>
		public string Kind
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(CommandRole)) return FieldRecordCatalogV1.LaunchContexts.Command;
				if (UnitId.HasValue) return FieldRecordCatalogV1.LaunchContexts.Unit;
				if (CallId.HasValue) return FieldRecordCatalogV1.LaunchContexts.Call;
				if (ContactId.HasValue) return FieldRecordCatalogV1.LaunchContexts.Contact;
				return FieldRecordCatalogV1.LaunchContexts.None;
			}
		}
	}

	/// <summary>Outcome of server-side context authorization.</summary>
	public class FieldRecordContextVerification
	{
		public bool Ok { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public string CallNumber { get; set; }
		public string UnitName { get; set; }
		public string GroupName { get; set; }
		public string CommandName { get; set; }
		public bool StaffedOnUnit { get; set; }
		public bool HoldsCommandRole { get; set; }
		public static FieldRecordContextVerification Allowed() => new FieldRecordContextVerification { Ok = true };
		public static FieldRecordContextVerification Denied(string reason) => new FieldRecordContextVerification { Ok = false, Reasons = { reason } };
	}

	public class FieldRecordCatalogRequest
	{
		public RmsOriginClient Origin { get; set; }
		public string AppVersion { get; set; }
		/// <summary>The renderer capability the client reports (records.v1 / v1b / v1c); unknown is treated as records.v1.</summary>
		public string ClientCapability { get; set; }
		public FieldRecordContext Context { get; set; } = new FieldRecordContext();
	}

	/// <summary>Minimum-version / flag preflight a field app runs before showing Records at all.</summary>
	public class FieldRecordPreflight
	{
		public string ContractVersion { get; set; } = FieldRecordCatalogV1.ContractVersion;
		public string SyncContractVersion { get; set; } = FieldRecordCatalogV1.SyncContractVersion;
		public RmsOriginClient Origin { get; set; }
		public bool Ok { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public bool ModuleEnabled { get; set; }
		public bool RecordsUsable { get; set; }
		public bool AppEnabled { get; set; }
		public string MinimumAppVersion { get; set; }
		public string AppVersion { get; set; }
		public string ClientCapability { get; set; }
		public string ProtectionState { get; set; }
		public long ServerTimestampMs { get; set; }
	}

	/// <summary>One definition a field app may start, with what it needs to decide offline/attachment/protected behavior.</summary>
	public class FieldRecordCatalogEntry
	{
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public bool Locked { get; set; }
		public int? RecordType { get; set; }
		public string LifecyclePreset { get; set; }
		public List<string> LaunchContexts { get; set; } = new List<string>();
		public bool AllowOffline { get; set; }
		public bool AllowAttachments { get; set; }
		public string MinimumAppVersion { get; set; }
		public string MinimumClientCapability { get; set; }
		public bool Restricted { get; set; }
		/// <summary>The definition carries Protected fields: authoring needs a live grant and never caches plaintext offline.</summary>
		public bool RequiresProtectedGrant { get; set; }
		public string SchemaChecksum { get; set; }
		public int PrefillVersion { get; set; }
		public bool SupportsPrefill { get; set; }
	}

	public class FieldRecordCatalogExclusion
	{
		public string DefinitionKey { get; set; }
		public string Reason { get; set; }
	}

	/// <summary>The manifest, filtered server-side; a client cannot widen it by changing a query value.</summary>
	public class FieldRecordCatalog
	{
		public string ContractVersion { get; set; } = FieldRecordCatalogV1.ContractVersion;
		public RmsOriginClient Origin { get; set; }
		public bool Ok { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public string ContextKind { get; set; }
		public bool ContextVerified { get; set; }
		public string ProtectionState { get; set; }
		public string ScopeStamp { get; set; }
		public List<FieldRecordCatalogEntry> Definitions { get; set; } = new List<FieldRecordCatalogEntry>();
		public List<FieldRecordCatalogExclusion> Exclusions { get; set; } = new List<FieldRecordCatalogExclusion>();
		public long ServerTimestampMs { get; set; }

		public bool Includes(string definitionKey, int version) => Definitions.Any(d => string.Equals(d.DefinitionKey, definitionKey, StringComparison.OrdinalIgnoreCase) && d.Version == version);
	}

	/// <summary>Where one prefilled value came from.</summary>
	public class FieldRecordPrefillProvenance
	{
		public string FieldKey { get; set; }
		public string Source { get; set; }
		public string SourceId { get; set; }
		public DateTime CapturedOn { get; set; }
	}

	/// <summary>Server-calculated prefill for a definition version in a verified context.</summary>
	public class FieldRecordPrefill
	{
		public string ContractVersion { get; set; } = FieldRecordCatalogV1.PrefillContractVersion;
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
		public int PrefillVersion { get; set; }
		public int? CallId { get; set; }
		public int? UnitId { get; set; }
		public int? StationGroupId { get; set; }
		public List<RecordValueInput> Values { get; set; } = new List<RecordValueInput>();
		public List<FieldRecordPrefillProvenance> Provenance { get; set; } = new List<FieldRecordPrefillProvenance>();
		public List<string> SuggestedParticipantUserIds { get; set; } = new List<string>();
		public List<int> SuggestedUnitIds { get; set; } = new List<int>();
		public DateTime CalculatedOn { get; set; }
	}

	public class FieldRecordSyncRequest
	{
		public RmsOriginClient Origin { get; set; }
		public string AppVersion { get; set; }
		public string ClientCapability { get; set; }
		public FieldRecordContext Context { get; set; } = new FieldRecordContext();
		public long Since { get; set; }
		public string SinceId { get; set; }
		public string ScopeStamp { get; set; }
		public int Take { get; set; } = 200;
		public bool IncludeCatalog { get; set; } = true;
	}

	/// <summary>
	/// A bounded Field Records sync bundle: catalog, authorized change delta with tombstones, the caller's own
	/// drafts and returned Records, and the caller's open work assignments. Never a bulk RMS export: every row
	/// is re-authorized at read time and a scope change resets the client.
	/// </summary>
	public class FieldRecordSyncBundle
	{
		public string ContractVersion { get; set; } = FieldRecordCatalogV1.SyncContractVersion;
		public bool Ok { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public string ScopeStamp { get; set; }
		public bool ResetRequired { get; set; }
		public long Since { get; set; }
		public long ServerTimestampMs { get; set; }
		public string ServerCursorId { get; set; }
		public bool HasMore { get; set; }
		public FieldRecordCatalog Catalog { get; set; }
		public List<RmsRecordSearchProjection> Changes { get; set; } = new List<RmsRecordSearchProjection>();
		/// <summary>Record ids in <see cref="Changes"/> the caller may no longer read; the client evicts them.</summary>
		public List<string> Tombstones { get; set; } = new List<string>();
		public List<RmsRecordSearchProjection> Drafts { get; set; } = new List<RmsRecordSearchProjection>();
		public List<RmsRecordWorkAssignment> Assignments { get; set; } = new List<RmsRecordWorkAssignment>();
	}
}
