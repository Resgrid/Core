using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Onboarding;

namespace Resgrid.Services.Onboarding.Writers
{
	/// <summary>One mapped field of a target record: its canonical string form (for diffs and before-values) and its setter.</summary>
	public sealed class ImportFieldBinding<T>
	{
		public ImportFieldBinding(string key, Func<T, string> get, Action<T, string> set)
		{
			Key = key;
			Get = get;
			Set = set;
		}

		public string Key { get; }
		public Func<T, string> Get { get; }
		public Action<T, string> Set { get; }
	}

	/// <summary>Canonical-string conversions shared by the writers (the same forms ImportFieldConverter produces).</summary>
	public static class ImportCanonical
	{
		public static string Bool(bool value) => value ? "true" : "false";
		public static string Bool(bool? value) => value.HasValue ? Bool(value.Value) : null;
		public static bool ToBool(string value) => value == "true";
		public static bool? ToNullableBool(string value) => value == null ? (bool?)null : value == "true";
		public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
		public static string Int(int? value) => value?.ToString(CultureInfo.InvariantCulture);
		public static int ToInt(string value, int fallback = 0) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
		public static int? ToNullableInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : (int?)null;
		public static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		/// <summary>The code at an enum value's position in the contract's choice list (codes are declared in enum order).</summary>
		public static string Code(string[] codes, int value, int offset = 0) =>
			value - offset >= 0 && value - offset < codes.Length ? codes[value - offset] : null;

		public static int Value(string[] codes, string code, int offset = 0, int fallback = 0)
		{
			var index = Array.IndexOf(codes, code);
			return index < 0 ? fallback : index + offset;
		}
	}

	/// <summary>Issues a writer raises while planning (L7/L8); the preview completes them with the dataset (ImportIssueFactory.ForPlan).</summary>
	public static class ImportWriterIssues
	{
		public static ImportIssue Row(ImportCanonicalRow row, string code, ImportIssueSeverity severity, string fieldKey = null,
			ImportLadderLevel level = ImportLadderLevel.L7Department, IDictionary<string, string> parameters = null) => new ImportIssue
		{
			StagedRowId = row?.StagedRowId,
			RowNumber = row?.RowNumber,
			FieldKey = fieldKey,
			Code = code,
			Severity = (int)severity,
			Level = (int)level,
			Parameters = parameters == null || parameters.Count == 0 ? null : JsonConvert.SerializeObject(parameters)
		};
	}

	/// <summary>
	/// The shared shape of a wave-1 writer over an integer-identity department record (wizard plan sections 11.1 and 12):
	/// match by exact unique natural key, create through the owning service's quiet overload inside the chunk transaction,
	/// reviewed updates limited to fields that differ, before-values journaled for restore, and rollback through the
	/// owning service's delete path. Subclasses describe the record; the engine does claims, chunking and effects.
	/// </summary>
	public abstract class ImportRecordWriter<T> : IImportTargetWriter, IImportReplaceableWriter where T : class
	{
		public abstract string EntityKey { get; }

		public abstract ImportEffects AffectedEffects { get; }

		/// <summary>Plain (non-reference) fields, in contract order.</summary>
		protected abstract IReadOnlyList<ImportFieldBinding<T>> Bindings { get; }

		/// <summary>The department's live (not deleted) records of this kind.</summary>
		protected abstract Task<List<T>> LoadAsync(int departmentId, CancellationToken cancellationToken);

		protected abstract string KeyOf(T record);

		/// <summary>The record's natural key text (normalized by the base).</summary>
		protected abstract string NaturalKeyOf(T record);

		/// <summary>The row's natural key text (normalized by the base).</summary>
		protected virtual string NaturalKeyOf(ImportCanonicalRow row) => row.Text("name");

		/// <summary>The normalized key two records must share to be the same (composite keys normalize each part).</summary>
		protected virtual string MatchKeyOf(T record) => Norm(NaturalKeyOf(record));

		protected virtual string MatchKeyOf(ImportCanonicalRow row) => Norm(NaturalKeyOf(row));

		/// <summary>A composite key: each part normalized, joined with '|' (null when every part is empty).</summary>
		protected static string Composite(params string[] parts)
		{
			var normalized = parts.Select(p => ImportFieldConverter.Normalize(p)).ToList();
			return normalized.All(p => p.Length == 0) ? null : string.Join("|", normalized);
		}

		/// <summary>A new record for the department with the row's plain fields still unset.</summary>
		protected abstract T New(ImportWriteContext context, ImportCanonicalRow row);

		/// <summary>Saves through the owning service with <see cref="ImportWriteContext.Effects"/>; joins the chunk transaction.</summary>
		protected abstract Task<T> SaveAsync(ImportWriteContext context, T record, CancellationToken cancellationToken);

		/// <summary>Removes a record the import created (rollback, section 11.8) through the owning service's delete path.</summary>
		protected abstract Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, T record, CancellationToken cancellationToken);

		/// <summary>Reference fields: resolves tokens through <see cref="ImportWriteContext.ResolveReference"/>. Returns an issue code or null.</summary>
		protected virtual Task<string> ApplyReferencesAsync(ImportWriteContext context, T record, ImportCanonicalRow row, CancellationToken cancellationToken) =>
			Task.FromResult<string>(null);

		/// <summary>Child rows saved after the record (unit seats, custom field values).</summary>
		protected virtual Task AfterSaveAsync(ImportWriteContext context, T record, ImportCanonicalRow row, bool created, CancellationToken cancellationToken) =>
			Task.CompletedTask;

		/// <summary>Activity created outside the import that makes deleting the record unsafe (a call uses the unit…).</summary>
		protected virtual Task<bool> HasOutsideActivityAsync(ImportWriteContext context, T record, CancellationToken cancellationToken) => Task.FromResult(false);

		/// <summary>
		/// Whether Replace may retire this kind of record: only when <see cref="RemoveAsync"/> is a soft delete that
		/// <see cref="RestoreRetiredAsync"/> undoes (the rollback proof). Hard-deleted records are never offered.
		/// </summary>
		protected virtual bool CanRetireRecords => false;

		/// <summary>A record by key including retired (soft-deleted) ones, for restore; null when gone.</summary>
		protected virtual Task<T> GetIncludingRetiredAsync(ImportWriteContext context, string key, CancellationToken cancellationToken) =>
			Task.FromResult<T>(null);

		protected virtual bool IsRetired(T record) => false;

		/// <summary>Brings a retired record back (clears its soft delete).</summary>
		protected virtual Task RestoreRetiredAsync(ImportWriteContext context, T record, CancellationToken cancellationToken) => Task.CompletedTask;

		/// <summary>Why this record cannot be retired (in use, the only one left …), or null.</summary>
		protected virtual Task<string> RetireBlockerAsync(ImportWriteContext context, T record, CancellationToken cancellationToken) =>
			Task.FromResult<string>(null);

		public bool SupportsReplace => CanRetireRecords;

		public virtual async Task<List<ImportReplaceCandidate>> GetRetireCandidatesAsync(ImportWriteContext context, IReadOnlyCollection<string> keptTargetKeys,
			CancellationToken cancellationToken)
		{
			var candidates = new List<ImportReplaceCandidate>();
			if (!CanRetireRecords)
				return candidates;

			var kept = new HashSet<string>(keptTargetKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
			foreach (var record in await LoadAsync(context.DepartmentId, cancellationToken) ?? new List<T>())
				if (!kept.Contains(KeyOf(record)))
					candidates.Add(new ImportReplaceCandidate { TargetKey = KeyOf(record), BlockedCode = await RetireBlockerAsync(context, record, cancellationToken) });
			return candidates;
		}

		public virtual async Task<string> RetireAsync(ImportWriteContext context, string targetKey, ImportJournalWriter journal, CancellationToken cancellationToken)
		{
			if (!CanRetireRecords)
				return "IMP-REPLACE-UNSUPPORTED";

			var record = await GetAsync(context, targetKey, cancellationToken);
			if (record == null)
				return null;
			var blocked = await RetireBlockerAsync(context, record, cancellationToken);
			if (blocked != null)
				return blocked;

			await RemoveAsync(context, record, cancellationToken);
			await journal.AppendAsync(EntityKey, targetKey, ImportJournalOperation.Retire, cancellationToken: cancellationToken);
			return null;
		}

		/// <summary>Seats a created row uses (units count against the plan; catalog values do not).</summary>
		protected virtual bool UsesUnitSeat => false;

		protected virtual async Task<T> GetAsync(ImportWriteContext context, string key, CancellationToken cancellationToken) =>
			(await LoadAsync(context.DepartmentId, cancellationToken)).FirstOrDefault(r => KeyOf(r) == key);

		public virtual Task<ImportGateResult> CheckGateAsync(ImportWriteContext context, CancellationToken cancellationToken) =>
			Task.FromResult(ImportGateResult.Ok());

		public virtual async Task<IReadOnlyDictionary<string, string>> FindExistingAsync(int departmentId, CancellationToken cancellationToken) =>
			(await IndexAsync(departmentId, cancellationToken)).Unique;

		protected static string Norm(string value)
		{
			var normalized = ImportFieldConverter.Normalize(value);
			return normalized.Length == 0 ? null : normalized;
		}

		protected async Task<(Dictionary<string, string> Unique, HashSet<string> Ambiguous)> IndexAsync(int departmentId, CancellationToken cancellationToken)
		{
			var unique = new Dictionary<string, string>(StringComparer.Ordinal);
			var ambiguous = new HashSet<string>(StringComparer.Ordinal);
			foreach (var record in await LoadAsync(departmentId, cancellationToken) ?? new List<T>())
			{
				var key = MatchKeyOf(record);
				if (key == null || ambiguous.Contains(key))
					continue;
				if (unique.Remove(key))
					ambiguous.Add(key);
				else
					unique[key] = KeyOf(record);
			}

			return (unique, ambiguous);
		}

		public virtual async Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			var (existing, ambiguous) = await IndexAsync(context.DepartmentId, cancellationToken);
			var records = context.WriteMode == ImportWriteMode.ReviewedUpdate
				? (await LoadAsync(context.DepartmentId, cancellationToken)).GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
				: null;
			var result = new ImportPlanResult();

			foreach (var row in rows)
			{
				var planned = new ImportPlannedRow { StagedRowId = row.StagedRowId, TargetKey = row.TargetKey };
				result.Rows.Add(planned);

				if (row.Decision == ImportRowDecision.Skip || row.Decision == ImportRowDecision.Unchanged)
				{
					// Excluded by the administrator, or already written by an earlier run of this source (external-id map).
					planned.Decision = row.Decision;
					continue;
				}

				var target = row.TargetKey;
				if (target == null)
				{
					var key = MatchKeyOf(row);
					if (key != null && ambiguous.Contains(key))
					{
						planned.Decision = ImportRowDecision.Skip;
						planned.Issues.Add(ImportWriterIssues.Row(row, "IMP-MATCH-AMBIGUOUS", ImportIssueSeverity.Blocking, "name"));
						continue;
					}

					if (key != null)
						existing.TryGetValue(key, out target);
				}

				if (target == null)
				{
					planned.Decision = ImportRowDecision.Create;
					if (UsesUnitSeat)
						result.UnitSeats++;
					continue;
				}

				planned.TargetKey = target;
				planned.Decision = ImportRowDecision.Link;
				if (records != null && records.TryGetValue(target, out var record))
				{
					planned.Changes = Diff(record, row);
					if (planned.Changes.Count > 0)
						planned.Decision = ImportRowDecision.Update;
				}
				else if (context.WriteMode == ImportWriteMode.CreateOnly)
				{
					planned.Issues.Add(ImportWriterIssues.Row(row, "IMP-MATCH-EXISTING", ImportIssueSeverity.Info, "name"));
				}
			}

			return result;
		}

		/// <summary>
		/// <see cref="IImportImplicitCreator.EnsureAsync"/> for writers that accept "create new" decisions: the record with this
		/// exact (normalized) name, or a new one with only its name set.
		/// </summary>
		public virtual async Task<(string TargetKey, bool Created)> EnsureAsync(ImportWriteContext context, string name, CancellationToken cancellationToken)
		{
			var key = Norm(name);
			if (key == null)
				return (null, false);

			var (existing, ambiguous) = await IndexAsync(context.DepartmentId, cancellationToken);
			if (existing.TryGetValue(key, out var target))
				return (target, false);
			if (ambiguous.Contains(key))
				return (null, false);

			var row = new ImportCanonicalRow();
			row.Values["name"] = name.Trim();
			var record = New(context, row);
			foreach (var binding in Bindings.Where(b => row.Values.ContainsKey(b.Key)))
				binding.Set(record, row.Text(binding.Key));
			record = await SaveAsync(context, record, cancellationToken);
			return (KeyOf(record), true);
		}

		/// <summary>
		/// <see cref="IImportExporter.ExportAsync"/> for writers that declare it: the department's records as canonical
		/// field values (plain fields only; subclasses add reference names in <see cref="ExportReferencesAsync"/>).
		/// </summary>
		public virtual async Task<List<Dictionary<string, string>>> ExportAsync(int departmentId, CancellationToken cancellationToken)
		{
			var rows = new List<Dictionary<string, string>>();
			foreach (var record in await LoadAsync(departmentId, cancellationToken) ?? new List<T>())
			{
				var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["source_id"] = "resgrid:" + KeyOf(record) };
				foreach (var binding in Bindings)
					row[binding.Key] = binding.Get(record);
				await ExportReferencesAsync(departmentId, record, row, cancellationToken);
				rows.Add(row);
			}
			return rows;
		}

		protected virtual Task ExportReferencesAsync(int departmentId, T record, Dictionary<string, string> row, CancellationToken cancellationToken) => Task.CompletedTask;

		/// <summary>Plain fields whose canonical value differs from the record (reviewed update, section 12.4).</summary>
		protected Dictionary<string, (string Before, string After)> Diff(T record, ImportCanonicalRow row)
		{
			var changes = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
			foreach (var binding in Bindings)
			{
				if (!row.Values.ContainsKey(binding.Key))
					continue;
				var before = binding.Get(record);
				var after = row.Text(binding.Key);
				if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
					changes[binding.Key] = (before, after);
			}

			return changes;
		}

		public virtual async Task<ImportRowWriteResult> WriteRowAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal,
			CancellationToken cancellationToken)
		{
			var result = new ImportRowWriteResult { StagedRowId = row.StagedRowId };

			switch (row.Decision)
			{
				case ImportRowDecision.Create:
				{
					if (UsesUnitSeat)
					{
						// Capacity unknown or spent: no unit beyond the plan is written (section 11.6).
						if (!context.HasUnitSeat)
							return Skipped(result, "IMP-PLAN-CAPACITY");
					}

					var record = New(context, row);
					foreach (var binding in Bindings)
						if (row.Values.ContainsKey(binding.Key))
							binding.Set(record, row.Text(binding.Key));

					var referenceCode = await ApplyReferencesAsync(context, record, row, cancellationToken);
					if (referenceCode != null)
						return Failed(result, referenceCode);

					record = await SaveAsync(context, record, cancellationToken);
					await AfterSaveAsync(context, record, row, true, cancellationToken);
					if (UsesUnitSeat)
						context.UseUnitSeat();

					result.TargetKey = KeyOf(record);
					result.Outcome = ImportRowOutcome.Created;
					await journal.AppendAsync(EntityKey, result.TargetKey, ImportJournalOperation.Create, cancellationToken: cancellationToken);
					break;
				}

				case ImportRowDecision.Update:
				{
					var record = await GetAsync(context, row.TargetKey, cancellationToken);
					if (record == null)
						return Skipped(result, "IMP-TARGET-MISSING");

					var changes = Diff(record, row);
					if (changes.Count == 0)
					{
						result.TargetKey = row.TargetKey;
						result.Outcome = ImportRowOutcome.Unchanged;
						break;
					}

					// Optimistic concurrency with people working in the department (section 11.7).
					var version = VersionOf(record);
					if (row.TargetVersion != null && version != null && row.TargetVersion != version)
						return Skipped(result, "IMP-CHANGED-SINCE-PREVIEW");

					var before = changes.ToDictionary(c => c.Key, c => c.Value.Before, StringComparer.OrdinalIgnoreCase);
					foreach (var binding in Bindings.Where(b => changes.ContainsKey(b.Key)))
						binding.Set(record, row.Text(binding.Key));

					record = await SaveAsync(context, record, cancellationToken);
					await AfterSaveAsync(context, record, row, false, cancellationToken);
					result.TargetKey = KeyOf(record);
					result.Outcome = ImportRowOutcome.Updated;
					await journal.AppendAsync(EntityKey, result.TargetKey, ImportJournalOperation.Update, JsonConvert.SerializeObject(before), VersionOf(record),
						cancellationToken: cancellationToken);
					break;
				}

				case ImportRowDecision.Link:
				case ImportRowDecision.Unchanged:
				{
					var record = await GetAsync(context, row.TargetKey, cancellationToken);
					if (record == null)
						return Skipped(result, "IMP-TARGET-MISSING");

					result.TargetKey = row.TargetKey;
					result.Outcome = row.Decision == ImportRowDecision.Link ? ImportRowOutcome.Linked : ImportRowOutcome.Unchanged;
					if (row.Decision == ImportRowDecision.Link)
						await journal.AppendAsync(EntityKey, result.TargetKey, ImportJournalOperation.Link, cancellationToken: cancellationToken);
					break;
				}

				default:
					return Skipped(result, null);
			}

			return result;
		}

		/// <summary>A value that changes whenever the record is edited (ModifiedOn, revision), or null when the record has none.</summary>
		protected virtual string VersionOf(T record) => null;

		public virtual async Task<ImportRollbackEntryResult> RollbackAsync(ImportWriteContext context, ImportChangeJournalEntry entry, CancellationToken cancellationToken)
		{
			var result = new ImportRollbackEntryResult { JournalEntryId = entry.Id };
			var record = await GetAsync(context, entry.TargetKey, cancellationToken);

			switch ((ImportJournalOperation)entry.Operation)
			{
				case ImportJournalOperation.Create:
					if (record == null)
					{
						// Someone already removed it: nothing to undo.
						result.Outcome = ImportRollbackOutcome.Deleted;
						return result;
					}

					if (await HasOutsideActivityAsync(context, record, cancellationToken))
						return Manual(result, "IMP-ROLLBACK-HAS-ACTIVITY");

					result.Outcome = await RemoveAsync(context, record, cancellationToken);
					return result;

				case ImportJournalOperation.Update:
				{
					if (record == null)
						return Manual(result, "IMP-TARGET-MISSING");
					if (entry.TargetVersionAtWrite != null && VersionOf(record) != null && VersionOf(record) != entry.TargetVersionAtWrite)
						return Manual(result, "IMP-ROLLBACK-CHANGED-SINCE");

					var before = string.IsNullOrWhiteSpace(entry.BeforeValues)
						? new Dictionary<string, string>()
						: JsonConvert.DeserializeObject<Dictionary<string, string>>(entry.BeforeValues);
					foreach (var binding in Bindings.Where(b => before.ContainsKey(b.Key)))
						binding.Set(record, before[binding.Key]);
					await SaveAsync(context, record, cancellationToken);
					result.Outcome = ImportRollbackOutcome.Restored;
					return result;
				}

				case ImportJournalOperation.Retire:
				{
					// Replace retired it: bring it back, unless it is already back or a live record now carries its name.
					var retired = await GetIncludingRetiredAsync(context, entry.TargetKey, cancellationToken);
					if (retired == null)
						return Manual(result, "IMP-TARGET-MISSING");
					if (!IsRetired(retired))
					{
						result.Outcome = ImportRollbackOutcome.None;
						return result;
					}
					var name = MatchKeyOf(retired);
					if (name != null && (await LoadAsync(context.DepartmentId, cancellationToken)).Any(r => MatchKeyOf(r) == name))
						return Manual(result, "IMP-ROLLBACK-CHANGED-SINCE");

					await RestoreRetiredAsync(context, retired, cancellationToken);
					result.Outcome = ImportRollbackOutcome.Restored;
					return result;
				}

				default:
					// Links changed nothing on the target.
					result.Outcome = ImportRollbackOutcome.None;
					return result;
			}
		}

		public virtual async Task<ImportVerifyResult> VerifyAsync(ImportWriteContext context, IReadOnlyList<ImportExternalId> written, CancellationToken cancellationToken)
		{
			var keys = new HashSet<string>((await LoadAsync(context.DepartmentId, cancellationToken)).Select(KeyOf), StringComparer.Ordinal);
			var expected = written.Where(w => w.TargetKey != null).Select(w => w.TargetKey).Distinct(StringComparer.Ordinal).ToList();
			var result = new ImportVerifyResult();
			result.Checks[EntityKey + ".present"] = (expected.Count, expected.Count(keys.Contains));
			return result;
		}

		protected static ImportRowWriteResult Skipped(ImportRowWriteResult result, string code)
		{
			result.Outcome = ImportRowOutcome.Skipped;
			result.Code = code;
			return result;
		}

		protected static ImportRowWriteResult Failed(ImportRowWriteResult result, string code)
		{
			result.Outcome = ImportRowOutcome.Failed;
			result.Code = code;
			return result;
		}

		protected static ImportRollbackEntryResult Manual(ImportRollbackEntryResult result, string code)
		{
			result.Outcome = ImportRollbackOutcome.ManualReconciliation;
			result.Code = code;
			return result;
		}

		/// <summary>Resolves one reference token to an integer key; null when it does not resolve (or is a label).</summary>
		protected static async Task<int?> ResolveIntAsync(ImportWriteContext context, string entityKey, string token, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(token) || ImportReferenceToken.Is(token, ImportReferenceToken.Label))
				return null;
			var key = await context.ResolveReference(entityKey, token, cancellationToken);
			return ImportCanonical.ToNullableInt(key);
		}
	}
}
