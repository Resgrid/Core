using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// What a write may cause besides the row itself (wizard plan section 11.2.2). Passed explicitly to the quiet service
	/// overloads writers call; never ambient state.
	/// </summary>
	public sealed class ImportEffectPolicy
	{
		/// <summary>*Added/*Created/Note/Certification/Shift/Calendar/Document events: notifications and workflow triggers.</summary>
		public bool RaiseDomainEvents { get; set; }
		/// <summary>DomainEventOutbox rows.</summary>
		public bool EnqueueOutbox { get; set; }
		/// <summary>SecurityRefreshEvent per write (false = collected and published once per chunk).</summary>
		public bool ImmediateSecurityRefresh { get; set; }
		/// <summary>Inline search projection (false = one department rebuild after the run).</summary>
		public bool ImmediateSearchProjection { get; set; }
		/// <summary>Novu, Loops, geocoding, What3Words, address verification, telephony.</summary>
		public bool ExternalCalls { get; set; }
		/// <summary>Email, SMS, push, voice or in-app message to anyone.</summary>
		public bool Messages { get; set; }
		/// <summary>Per-row AuditEvent (false = one summary audit per entity per chunk plus the journal).</summary>
		public bool PerRowAudit { get; set; }

		/// <summary>
		/// Services that normally commit the shared unit of work themselves (custom field values) join the caller's open
		/// transaction instead, so a chunk stays all-or-nothing (section 11.3).
		/// </summary>
		public bool JoinCallerTransaction { get; set; }

		/// <summary>The import policy: everything off; refresh, projection and audit deferred to chunk/run boundaries.</summary>
		public static ImportEffectPolicy Import => new ImportEffectPolicy { JoinCallerTransaction = true };

		/// <summary>Today's interactive behaviour (controllers keep it).</summary>
		public static ImportEffectPolicy Interactive => new ImportEffectPolicy
		{
			RaiseDomainEvents = true,
			EnqueueOutbox = true,
			ImmediateSecurityRefresh = true,
			ImmediateSearchProjection = true,
			ExternalCalls = true,
			Messages = true,
			PerRowAudit = true
		};
	}

	/// <summary>Deferred effects a writer touched; the engine runs each once per chunk or once per run (section 11.5).</summary>
	[Flags]
	public enum ImportEffects
	{
		None = 0,
		PersonnelVisibility = 1,
		UnitVisibility = 2,
		GroupVisibility = 4,
		RoleVisibility = 8,
		DepartmentUsersCache = 16,
		GroupsCache = 32,
		UnitsCache = 64,
		LimitsCache = 128,
		CustomStatesCache = 256,
		CallTypesCache = 512,
		SettingsCache = 1024,
		AppsResync = 2048,
		SearchRebuild = 4096,
		ProfilesCache = 8192
	}

	/// <summary>A staged row decrypted for a writer: typed canonical values only (section 8.3 "Typed past validation").</summary>
	public sealed class ImportCanonicalRow
	{
		public string StagedRowId { get; set; }
		public string DatasetId { get; set; }
		public int RowNumber { get; set; }
		public string SourceKey { get; set; }
		/// <summary>Field key → canonical string, or a list of strings for multi fields. References are tokens (see <see cref="ImportReferenceToken"/>).</summary>
		public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Source columns kept as custom fields: label → value.</summary>
		public Dictionary<string, string> CustomFields { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public ImportRowDecision Decision { get; set; }
		/// <summary>Existing target matched at plan time (Link/Update/Unchanged), string form.</summary>
		public string TargetKey { get; set; }
		public string TargetVersion { get; set; }
		/// <summary>True for rows the engine synthesized from "create new" value-map decisions.</summary>
		public bool Implicit { get; set; }
		/// <summary><see cref="ImportRowValidationState"/> after L1–L7; only Valid and Warning rows are written.</summary>
		public ImportRowValidationState ValidationState { get; set; }
		/// <summary>The commit outcome so far (rows written by an earlier slice keep theirs).</summary>
		public ImportRowOutcome Outcome { get; set; }

		public bool IsWritable => (ValidationState == ImportRowValidationState.Valid || ValidationState == ImportRowValidationState.Warning) &&
			Decision != ImportRowDecision.Undecided;

		public string Text(string key) => Values.TryGetValue(key, out var v) ? v as string : null;

		public IReadOnlyList<string> List(string key) =>
			Values.TryGetValue(key, out var v) && v is IReadOnlyList<string> list ? list
			: Values.TryGetValue(key, out var single) && single is string s && s != null ? new[] { s } : Array.Empty<string>();

		public bool? Bool(string key) => Text(key) switch { "true" => true, "false" => false, _ => (bool?)null };

		public int? Int(string key) => int.TryParse(Text(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : (int?)null;

		public decimal? Decimal(string key) => decimal.TryParse(Text(key), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null;

		public DateTime? Date(string key) => DateTime.TryParseExact(Text(key), new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss" },
			System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;
	}

	/// <summary>
	/// How a reference value was resolved at L6 (section 8.4): to a staged row of the referenced entity by its source key,
	/// to an existing Resgrid record, to a new record the engine creates from a "create new" decision, or kept as a
	/// labeled source value. The commit resolves src: and new: tokens through the external-id map, never by copying keys.
	/// </summary>
	public static class ImportReferenceToken
	{
		public const string Staged = "src:";
		public const string Existing = "id:";
		public const string CreateNew = "new:";
		public const string Label = "label:";

		public static string ForStaged(string sourceKey) => Staged + sourceKey;
		public static string ForExisting(string targetKey) => Existing + targetKey;
		public static string ForNew(string name) => CreateNew + name;
		public static string ForLabel(string text) => Label + text;

		public static bool Is(string token, string prefix) => token != null && token.StartsWith(prefix, StringComparison.Ordinal);
		public static string Value(string token, string prefix) => Is(token, prefix) ? token.Substring(prefix.Length) : null;
	}

	/// <summary>A writer refused one row for a known reason (a value-free code); the engine quarantines the row and continues.</summary>
	public sealed class ImportRowRejectedException : Exception
	{
		public ImportRowRejectedException(string code) : base(code)
		{
			Code = code;
		}

		public string Code { get; }
	}

	/// <summary>Everything a writer needs for one chunk (section 11.1). Created by the commit engine per chunk.</summary>
	public sealed class ImportWriteContext
	{
		public int DepartmentId { get; set; }
		public Department Department { get; set; }
		public ImportRun Run { get; set; }
		public ImportWorkspace Workspace { get; set; }
		public ImportAuthority Authority { get; set; }
		public ImportEffectPolicy Effects { get; set; } = ImportEffectPolicy.Import;
		public ImportWriteMode WriteMode { get; set; } = ImportWriteMode.CreateOnly;
		public ImportConsequenceDecisions Consequences { get; set; } = new ImportConsequenceDecisions();
		/// <summary>The source-system instance key rows of this run belong to.</summary>
		public string SourceSystemKey { get; set; }
		public DateTime Now { get; set; }
		/// <summary>Resolves a reference token to the target key (string form) of the referenced entity, or null.</summary>
		public Func<string, string, CancellationToken, Task<string>> ResolveReference { get; set; }
		/// <summary>Remaining personnel seats; null when capacity is unknown (no new personnel are written, section 11.6).</summary>
		public int? PersonnelCapacity { get; set; }
		/// <summary>Remaining unit seats; null when unknown.</summary>
		public int? UnitCapacity { get; set; }
		/// <summary>Personnel and units draw on one pool (entity plans): using either seat uses both counters.</summary>
		public bool SharedCapacity { get; set; }

		public bool HasPersonnelSeat => PersonnelCapacity is int p && p > 0;
		public bool HasUnitSeat => UnitCapacity is int u && u > 0;

		public void UsePersonnelSeat()
		{
			PersonnelCapacity--;
			if (SharedCapacity)
				UnitCapacity = PersonnelCapacity;
		}

		public void UseUnitSeat()
		{
			UnitCapacity--;
			if (SharedCapacity)
				PersonnelCapacity = UnitCapacity;
		}
		/// <summary>The administrator writes are attributed to (the authorizing administrator under managed migration).</summary>
		public string ActingUserId => Authority?.OnBehalfOfUserId;
		/// <summary>The dataset being written (null for run-level work).</summary>
		public ImportDataset Dataset { get; set; }
		/// <summary>Effects the writer touched in this chunk; the engine runs them once at the chunk boundary.</summary>
		public ImportEffects Touched { get; set; }
	}

	public sealed class ImportGateResult
	{
		public bool Allowed { get; set; } = true;
		/// <summary>IMP-ENTITLEMENT-MISSING and similar.</summary>
		public string Code { get; set; }

		public static ImportGateResult Ok() => new ImportGateResult();
		public static ImportGateResult Refused(string code) => new ImportGateResult { Allowed = false, Code = code };
	}

	/// <summary>One row's planned outcome plus the issues the department check (L7) and consequences (L8) raised.</summary>
	public sealed class ImportPlannedRow
	{
		public string StagedRowId { get; set; }
		public ImportRowDecision Decision { get; set; }
		public string TargetKey { get; set; }
		public string TargetVersion { get; set; }
		/// <summary>Field key → (before, after) for reviewed updates; empty for creates.</summary>
		public Dictionary<string, (string Before, string After)> Changes { get; set; } = new Dictionary<string, (string, string)>();
		public List<ImportIssue> Issues { get; set; } = new List<ImportIssue>();
	}

	public sealed class ImportPlanResult
	{
		public List<ImportPlannedRow> Rows { get; set; } = new List<ImportPlannedRow>();
		/// <summary>Consequence statements (L8) the administrator must acknowledge: id → (resx key, count).</summary>
		public Dictionary<string, int> Consequences { get; set; } = new Dictionary<string, int>();
		/// <summary>New personnel/unit seats this plan would use (plan capacity, L7).</summary>
		public int PersonnelSeats { get; set; }
		public int UnitSeats { get; set; }
	}

	/// <summary>The outcome of writing one row.</summary>
	public sealed class ImportRowWriteResult
	{
		public string StagedRowId { get; set; }
		public ImportRowOutcome Outcome { get; set; }
		public string TargetKey { get; set; }
		/// <summary>Issue code when the row failed or was skipped (value-free).</summary>
		public string Code { get; set; }
	}

	public sealed class ImportChunkResult
	{
		public List<ImportRowWriteResult> Rows { get; set; } = new List<ImportRowWriteResult>();
	}

	public sealed class ImportVerifyResult
	{
		/// <summary>CheckId → (expected, actual).</summary>
		public Dictionary<string, (long Expected, long Actual)> Checks { get; set; } = new Dictionary<string, (long, long)>();
	}

	public sealed class ImportRollbackEntryResult
	{
		public string JournalEntryId { get; set; }
		public ImportRollbackOutcome Outcome { get; set; }
		public string Code { get; set; }
	}

	public sealed class ImportRollbackResult
	{
		public List<ImportRollbackEntryResult> Entries { get; set; } = new List<ImportRollbackEntryResult>();
	}

	/// <summary>
	/// A target writer (wizard plan section 11.1): one per entity contract. Writers call service-layer methods with the
	/// import effect policy, never controllers and never raw repositories for protected data. Rows reach a writer only as
	/// typed canonical records; the engine handles claims, journal, chunking, retries and effects.
	/// </summary>
	public interface IImportTargetWriter
	{
		/// <summary>The contract key this writer serves.</summary>
		string EntityKey { get; }

		/// <summary>Deferred effects a chunk of this writer touches.</summary>
		ImportEffects AffectedEffects { get; }

		/// <summary>L7 entitlement and permission gate for the department and the run's authority.</summary>
		Task<ImportGateResult> CheckGateAsync(ImportWriteContext context, CancellationToken cancellationToken);

		/// <summary>
		/// Existing department records keyed by normalized natural key (name, email, code). Used by L6 to resolve
		/// references to records that already exist and by PlanAsync to detect conflicts.
		/// </summary>
		Task<IReadOnlyDictionary<string, string>> FindExistingAsync(int departmentId, CancellationToken cancellationToken);

		/// <summary>Pure planning (no writes): create/update/link/skip decisions, conflicts (L7) and consequences (L8).</summary>
		Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken);

		/// <summary>Writes one row. The engine has already claimed its external id; integer-identity writers report the new key.</summary>
		Task<ImportRowWriteResult> WriteRowAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal, CancellationToken cancellationToken);

		/// <summary>Removes or restores what one journal entry recorded (section 11.8).</summary>
		Task<ImportRollbackEntryResult> RollbackAsync(ImportWriteContext context, ImportChangeJournalEntry entry, CancellationToken cancellationToken);

		/// <summary>Reconciliation counts for this entity after a commit (L10).</summary>
		Task<ImportVerifyResult> VerifyAsync(ImportWriteContext context, IReadOnlyList<ImportExternalId> written, CancellationToken cancellationToken);
	}

	/// <summary>
	/// A writer whose records other files may reference by name with a "create new" value-map decision (section 8.4,
	/// <see cref="ImportReferenceToken.CreateNew"/>). The engine claims the implicit identity, then asks the writer to find
	/// the department record with that name or create it, inside the chunk transaction.
	/// </summary>
	public interface IImportImplicitCreator
	{
		string EntityKey { get; }

		/// <summary>The department record with this name, created when missing; returns its key and whether it was created.</summary>
		Task<(string TargetKey, bool Created)> EnsureAsync(ImportWriteContext context, string name, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Work a writer can only finish once every row of its dataset (or every dataset of the run) is written: a group's
	/// parent later in the file, a status button's next statuses, roles that waited for certifications. The engine calls
	/// it after all chunks, in dependency order, page by page, each page in its own transaction.
	/// </summary>
	/// <summary>
	/// A writer whose records Replace may retire (wizard plan sections 5.2 and 12.4): the department's records of this kind
	/// that the file does not contain are soft-deleted at commit and restored by rollback. Only writers whose removal is a
	/// restorable soft delete support it; that restorability is the rollback proof the preview shows.
	/// </summary>
	public interface IImportReplaceableWriter
	{
		string EntityKey { get; }

		bool SupportsReplace { get; }

		/// <summary>The department's live records of this kind not in <paramref name="keptTargetKeys"/>, each with whether it can be retired.</summary>
		Task<List<ImportReplaceCandidate>> GetRetireCandidatesAsync(ImportWriteContext context, IReadOnlyCollection<string> keptTargetKeys,
			CancellationToken cancellationToken);

		/// <summary>Retires one record (journaled as <see cref="ImportJournalOperation.Retire"/>); returns an issue code when it could not.</summary>
		Task<string> RetireAsync(ImportWriteContext context, string targetKey, ImportJournalWriter journal, CancellationToken cancellationToken);
	}

	/// <summary>A record a Replace would retire: its key (never a value) and, when it cannot be retired, why.</summary>
	public sealed class ImportReplaceCandidate
	{
		public string TargetKey { get; set; }
		public string BlockedCode { get; set; }
		public bool CanRetire => BlockedCode == null;
	}

	public interface IImportDeferredWork
	{
		/// <summary>Completes the deferred part for rows already written (each row carries its TargetKey); returns rows changed.</summary>
		Task<int> CompleteDeferredAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> writtenRows, ImportJournalWriter journal,
			CancellationToken cancellationToken);
	}

	/// <summary>
	/// A writer whose target key is chosen before the write (identity accounts, section 11.3). The engine claims the row
	/// with a key from <see cref="NewTargetKey"/> and commits the claim before the chunk transaction starts, because part
	/// of the write (the account) happens outside that transaction; a retry finds the claim and reuses the same key.
	/// </summary>
	public interface IImportPregeneratedKeys
	{
		string NewTargetKey();
	}

	/// <summary>
	/// A writer that can export the department's current records in its contract's canonical form, for the "download
	/// current data" workbook (round-trip editing) and the starter workbook's drop-down lists (section 6.3, step 4).
	/// Never implemented for entities that carry personal or protected data.
	/// </summary>
	public interface IImportExporter
	{
		string EntityKey { get; }

		/// <summary>One dictionary per record: contract field key → canonical value (references as the referenced record's name).</summary>
		Task<List<Dictionary<string, string>>> ExportAsync(int departmentId, CancellationToken cancellationToken);
	}

	/// <summary>Appends journal entries for the row being written (the engine supplies run, sequence and department).</summary>
	public sealed class ImportJournalWriter
	{
		private readonly Func<ImportChangeJournalEntry, CancellationToken, Task> _append;

		public ImportJournalWriter(Func<ImportChangeJournalEntry, CancellationToken, Task> append)
		{
			_append = append;
		}

		public Task AppendAsync(string entityType, string targetKey, ImportJournalOperation operation, string beforeValues = null,
			string targetVersion = null, bool createdAccount = false, CancellationToken cancellationToken = default) =>
			_append(new ImportChangeJournalEntry
			{
				EntityType = entityType,
				TargetKey = targetKey,
				Operation = (int)operation,
				BeforeValues = beforeValues,
				TargetVersionAtWrite = targetVersion,
				CreatedAccount = createdAccount
			}, cancellationToken);
	}
}
