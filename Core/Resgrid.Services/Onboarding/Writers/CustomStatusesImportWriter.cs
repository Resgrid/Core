using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding.Writers
{
	/// <summary>
	/// Custom statuses ("custom-statuses"). Each row is one button of a status set; rows sharing a set name and type
	/// form one set, found or created (and journaled as "set:{id}") the first time a chunk needs it. A button's next
	/// statuses name other buttons of the same set, so they are filled in after the whole dataset is written.
	/// </summary>
	public sealed class CustomStatusesImportWriter : ImportRecordWriter<CustomStateDetail>, IImportDeferredWork
	{
		public const string SetPrefix = "set:";

		private static readonly string[] SetTypes = { "personnel", "unit", "staffing" };
		private static readonly string[] NoteTypes = { "none", "optional", "required" };
		private static readonly string[] Destinations =
			{ "none", "stations", "calls", "calls-and-stations", "pois", "calls-and-pois", "stations-and-pois", "calls-stations-and-pois" };

		private readonly ICustomStateService _customStates;
		// Sets found or created in this chunk's scope (the service list is cached until the chunk boundary).
		private readonly Dictionary<string, int> _sets = new Dictionary<string, int>(StringComparer.Ordinal);
		private readonly Dictionary<string, int> _pendingSet = new Dictionary<string, int>(StringComparer.Ordinal);

		private static readonly IReadOnlyList<ImportFieldBinding<CustomStateDetail>> Fields = new[]
		{
			new ImportFieldBinding<CustomStateDetail>("button_text", r => r.ButtonText, (r, v) => r.ButtonText = v),
			new ImportFieldBinding<CustomStateDetail>("button_color", r => r.ButtonColor, (r, v) => r.ButtonColor = v),
			new ImportFieldBinding<CustomStateDetail>("text_color", r => r.TextColor, (r, v) => r.TextColor = v),
			new ImportFieldBinding<CustomStateDetail>("gps_required", r => ImportCanonical.Bool(r.GpsRequired), (r, v) => r.GpsRequired = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<CustomStateDetail>("note", r => ImportCanonical.Code(NoteTypes, r.NoteType), (r, v) => r.NoteType = ImportCanonical.Value(NoteTypes, v)),
			new ImportFieldBinding<CustomStateDetail>("destination", r => ImportCanonical.Code(Destinations, r.DetailType), (r, v) => r.DetailType = ImportCanonical.Value(Destinations, v)),
			new ImportFieldBinding<CustomStateDetail>("order", r => ImportCanonical.Int(r.Order), (r, v) => r.Order = ImportCanonical.ToInt(v))
		};

		public CustomStatusesImportWriter(ICustomStateService customStates)
		{
			_customStates = customStates;
		}

		public override string EntityKey => "custom-statuses";
		public override ImportEffects AffectedEffects => ImportEffects.CustomStatesCache | ImportEffects.AppsResync;
		protected override IReadOnlyList<ImportFieldBinding<CustomStateDetail>> Bindings => Fields;

		private async Task<List<CustomState>> SetsAsync(int departmentId) =>
			(await _customStates.GetAllCustomStatesForDepartmentAsync(departmentId) ?? new List<CustomState>())
				.Where(s => s.DepartmentId == departmentId && !s.IsDeleted).ToList();

		protected override async Task<List<CustomStateDetail>> LoadAsync(int departmentId, CancellationToken cancellationToken)
		{
			var details = new List<CustomStateDetail>();
			foreach (var set in await SetsAsync(departmentId))
				foreach (var detail in (set.Details ?? new List<CustomStateDetail>()).Where(d => !d.IsDeleted))
				{
					detail.CustomState = set;
					details.Add(detail);
				}
			return details;
		}

		protected override string KeyOf(CustomStateDetail record) => ImportCanonical.Int(record.CustomStateDetailId);
		protected override string NaturalKeyOf(CustomStateDetail record) => record.ButtonText;
		protected override string NaturalKeyOf(ImportCanonicalRow row) => row.Text("button_text");

		protected override string MatchKeyOf(CustomStateDetail record) =>
			Composite(record.CustomState?.Name, ImportCanonical.Code(SetTypes, record.CustomState?.Type ?? 0, 1), record.ButtonText);

		protected override string MatchKeyOf(ImportCanonicalRow row) => Composite(row.Text("set_name"), row.Text("set_type"), row.Text("button_text"));

		/// <summary>Buttons by set, button and type, plus unit sets by name ("set:{id}") for unit types' status set references.</summary>
		public override async Task<IReadOnlyDictionary<string, string>> FindExistingAsync(int departmentId, CancellationToken cancellationToken)
		{
			var map = new Dictionary<string, string>((await IndexAsync(departmentId, cancellationToken)).Unique, StringComparer.Ordinal);
			foreach (var group in (await SetsAsync(departmentId)).Where(s => s.Type == (int)CustomStateTypes.Unit).GroupBy(s => Norm(s.Name)).Where(g => g.Key != null && g.Count() == 1))
				map.TryAdd(group.Key, SetPrefix + ImportCanonical.Int(group.First().CustomStateId));
			return map;
		}

		protected override CustomStateDetail New(ImportWriteContext context, ImportCanonicalRow row) => new CustomStateDetail
		{
			CustomStateId = _pendingSet.TryGetValue(row.StagedRowId ?? string.Empty, out var setId) ? setId : 0,
			BaseType = -1
		};

		protected override Task<CustomStateDetail> SaveAsync(ImportWriteContext context, CustomStateDetail record, CancellationToken cancellationToken)
		{
			record.CustomState = null;
			return _customStates.SaveDetailAsync(record, context.DepartmentId, context.Effects, cancellationToken);
		}

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, CustomStateDetail record, CancellationToken cancellationToken)
		{
			// Unit states and action logs keep the detail id: buttons are soft-deleted, as the Custom Statuses screen does.
			record.IsDeleted = true;
			record.CustomState = null;
			await _customStates.SaveDetailAsync(record, context.DepartmentId, context.Effects, cancellationToken);
			return ImportRollbackOutcome.SoftDeleted;
		}

		public override async Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			var result = await base.PlanAsync(context, rows, cancellationToken);
			var created = rows.Where(r => result.Rows.Any(p => p.StagedRowId == r.StagedRowId && p.Decision == ImportRowDecision.Create)).ToList();
			var existingSets = new HashSet<string>((await SetsAsync(context.DepartmentId)).Select(s => Composite(s.Name, ImportCanonical.Code(SetTypes, s.Type, 1))), StringComparer.Ordinal);

			// A new personnel or staffing set becomes the one everyone's buttons show (L8, section 11.2.3).
			var replacing = created.Where(r => r.Text("set_type") != "unit").Select(r => Composite(r.Text("set_name"), r.Text("set_type")))
				.Where(k => k != null && !existingSets.Contains(k)).Distinct().Count();
			if (replacing > 0)
				result.Consequences["custom-statuses.replaces-active-set"] = replacing;

			return result;
		}

		public override async Task<ImportRowWriteResult> WriteRowAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal,
			CancellationToken cancellationToken)
		{
			if (row.Decision == ImportRowDecision.Create)
			{
				var setId = await EnsureSetAsync(context, row.Text("set_name"), row.Text("set_type"), journal, cancellationToken);
				if (setId == null)
					return Failed(new ImportRowWriteResult { StagedRowId = row.StagedRowId }, "IMP-REQUIRED-MISSING");
				_pendingSet[row.StagedRowId ?? string.Empty] = setId.Value;
			}

			return await base.WriteRowAsync(context, row, journal, cancellationToken);
		}

		private async Task<int?> EnsureSetAsync(ImportWriteContext context, string name, string type, ImportJournalWriter journal, CancellationToken cancellationToken)
		{
			var key = Composite(name, type);
			if (key == null || string.IsNullOrWhiteSpace(name))
				return null;
			if (_sets.TryGetValue(key, out var known))
				return known;

			var existing = (await SetsAsync(context.DepartmentId)).FirstOrDefault(s => Composite(s.Name, ImportCanonical.Code(SetTypes, s.Type, 1)) == key);
			if (existing != null)
				return _sets[key] = existing.CustomStateId;

			var set = await _customStates.SaveAsync(new CustomState
			{
				DepartmentId = context.DepartmentId,
				Name = name.Trim(),
				Type = ImportCanonical.Value(SetTypes, type, 1, (int)CustomStateTypes.Personnel)
			}, context.Effects, cancellationToken);
			await journal.AppendAsync(EntityKey, SetPrefix + ImportCanonical.Int(set.CustomStateId), ImportJournalOperation.Create, cancellationToken: cancellationToken);
			_customStates.InvalidateCustomStateInCache(context.DepartmentId);
			return _sets[key] = set.CustomStateId;
		}

		public override async Task<ImportRollbackEntryResult> RollbackAsync(ImportWriteContext context, ImportChangeJournalEntry entry, CancellationToken cancellationToken)
		{
			if (entry.TargetKey == null || !entry.TargetKey.StartsWith(SetPrefix, StringComparison.Ordinal))
				return await base.RollbackAsync(context, entry, cancellationToken);

			var result = new ImportRollbackEntryResult { JournalEntryId = entry.Id };
			var setId = ImportCanonical.ToInt(entry.TargetKey.Substring(SetPrefix.Length));
			var set = (await SetsAsync(context.DepartmentId)).FirstOrDefault(s => s.CustomStateId == setId);
			if (set == null)
			{
				result.Outcome = ImportRollbackOutcome.Deleted;
				return result;
			}

			// Buttons the import created were rolled back first (reverse journal order); any left were added since.
			if ((set.Details ?? new List<CustomStateDetail>()).Any(d => !d.IsDeleted))
				return Manual(result, "IMP-ROLLBACK-HAS-ACTIVITY");

			set.Details ??= new List<CustomStateDetail>();
			await _customStates.DeleteAsync(set, cancellationToken);
			result.Outcome = ImportRollbackOutcome.SoftDeleted;
			return result;
		}

		public async Task<int> CompleteDeferredAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> writtenRows, ImportJournalWriter journal,
			CancellationToken cancellationToken)
		{
			var changed = 0;
			var details = (await LoadAsync(context.DepartmentId, cancellationToken)).ToDictionary(KeyOf, StringComparer.Ordinal);

			foreach (var row in writtenRows.Where(r => r.TargetKey != null && r.List("next_statuses").Count > 0))
			{
				if (!details.TryGetValue(row.TargetKey, out var detail))
					continue;

				var siblings = details.Values.Where(d => d.CustomStateId == detail.CustomStateId && d.CustomStateDetailId != detail.CustomStateDetailId)
					.GroupBy(d => Norm(d.ButtonText)).Where(g => g.Key != null).ToDictionary(g => g.Key, g => g.First().CustomStateDetailId, StringComparer.Ordinal);
				var ids = row.List("next_statuses").Select(Norm).Where(n => n != null && siblings.ContainsKey(n)).Select(n => siblings[n]).ToList();
				if (ids.Count == 0)
					continue;

				var before = detail.NextStateDetailIds;
				detail.SetNextStateDetailIds(ids);
				if (before == detail.NextStateDetailIds)
					continue;

				detail.CustomState = null;
				await _customStates.SaveDetailAsync(detail, context.DepartmentId, context.Effects, cancellationToken);
				changed++;
			}

			return changed;
		}

		/// <summary>The custom-status set a reference token names: "set:{id}" for an existing set, or the set of a referenced button.</summary>
		public static async Task<int?> ResolveSetIdAsync(ImportWriteContext context, ICustomStateService customStates, string token, CancellationToken cancellationToken)
		{
			var key = await context.ResolveReference("custom-statuses", token, cancellationToken);
			if (string.IsNullOrWhiteSpace(key))
				return null;
			if (key.StartsWith(SetPrefix, StringComparison.Ordinal))
				return ImportCanonical.ToNullableInt(key.Substring(SetPrefix.Length));

			var detailId = ImportCanonical.ToNullableInt(key);
			if (detailId == null)
				return null;
			return (await customStates.GetCustomDetailForDepartmentAsync(context.DepartmentId, detailId.Value))?.CustomStateId;
		}
	}
}
