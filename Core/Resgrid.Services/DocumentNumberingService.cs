using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DocumentNumberingService : IDocumentNumberingService
	{
		/// <summary>The sequence the screens' sample numbers show.</summary>
		private const int SampleSequence = 153;

		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly IDocumentNumberSequencesRepository _sequences;

		public DocumentNumberingService(IDepartmentSettingsService settings, IDepartmentsService departments, IDocumentNumberSequencesRepository sequences)
		{
			_settings = settings;
			_departments = departments;
			_sequences = sequences;
		}

		public async Task<int> GetNumberingYearAsync(int departmentId, string kind, DateTime utcDate)
		{
			var yearStart = await YearStartAsync(departmentId, kind, false);
			return yearStart.YearOf(ToLocal(utcDate, await TimeZoneAsync(departmentId)));
		}

		public async Task<DocumentNumberPattern> GetPatternAsync(int departmentId, string kind, bool bypassCache = false)
		{
			var info = DocumentNumberKinds.Find(kind);
			if (info == null)
				return null;

			var patterns = info.IsRecords
				? (await _settings.GetRecordsNumberingConfigAsync(departmentId, bypassCache))?.DocumentPatterns
				: (await _settings.GetDocumentNumberingConfigAsync(departmentId, bypassCache))?.Patterns;
			return DocumentNumbering.EffectivePattern(patterns, kind);
		}

		public async Task<string> TakeCustomNumberAsync(int departmentId, string kind, DateTime utcDate, CancellationToken cancellationToken = default)
		{
			var pattern = await GetPatternAsync(departmentId, kind);
			if (pattern == null)
				return null;

			var scope = CallNumberFormat.Resolve(pattern.Pattern, pattern.SequenceWidth, ToLocal(utcDate, await TimeZoneAsync(departmentId)), await YearStartAsync(departmentId, kind, false));

			// Only the first document of a scope pays for the seed scan, which reads the numbers already written in that text
			// (built-in ones included), so a pattern that reads like earlier numbers carries on after them.
			var seed = await _sequences.GetSequenceAsync(departmentId, kind, scope.Key) == null
				? await _sequences.GetHighestIssuedAsync(departmentId, kind, scope.Prefix, scope.Suffix)
				: 0;
			var sequence = await _sequences.TakeNextAsync(departmentId, kind, scope.Key, seed, cancellationToken);

			return scope.Format(sequence);
		}

		public async Task<List<DocumentNumberStatus>> GetStatusesAsync(int departmentId, bool records, DateTime utcDate)
		{
			var statuses = new List<DocumentNumberStatus>();
			var timeZone = await TimeZoneAsync(departmentId);
			foreach (var kind in DocumentNumberKinds.All.Where(k => k.IsRecords == records))
			{
				statuses.Add(await StatusAsync(departmentId, kind, await GetPatternAsync(departmentId, kind.Key, true),
					await YearStartAsync(departmentId, kind.Key, true), utcDate, timeZone));
			}

			return statuses;
		}

		public async Task<DocumentNumberingSaveResult> SaveAsync(int departmentId, string userId, DocumentNumberingUpdate update, CancellationToken cancellationToken = default)
		{
			var result = new DocumentNumberingSaveResult();
			update ??= new DocumentNumberingUpdate();

			var config = await _settings.GetDocumentNumberingConfigAsync(departmentId, true) ?? new DocumentNumberingConfig();
			var saved = config.YearStart();
			var month = update.YearStartMonth ?? saved.Month;
			var day = update.YearStartDay ?? saved.Day;
			if (!NumberingYearStart.IsValid(month, day))
			{
				result.YearStartRejected = true;
				return result;
			}

			config.YearStartMonth = month;
			config.YearStartDay = day;
			config.YearLabel = (int)new NumberingYearStart(month, day, (NumberingYearLabel)(update.YearLabel ?? (int)saved.Label)).Label;
			config.Patterns ??= new List<DocumentNumberPattern>();
			ApplyPatterns(config.Patterns, update.Patterns, false, result);
			await _settings.SetDocumentNumberingConfigAsync(departmentId, config, cancellationToken);

			await RaiseAsync(departmentId, userId, update.Patterns, false, result, cancellationToken);
			return result;
		}

		public async Task<DocumentNumberingSaveResult> SaveRecordsPatternsAsync(int departmentId, string userId, List<DocumentNumberPatternUpdate> patterns, CancellationToken cancellationToken = default)
		{
			var result = new DocumentNumberingSaveResult();
			var config = await _settings.GetRecordsNumberingConfigAsync(departmentId, true) ?? new RecordsNumberingConfig();
			config.DocumentPatterns ??= new List<DocumentNumberPattern>();
			ApplyPatterns(config.DocumentPatterns, patterns, true, result);
			await _settings.SetRecordsNumberingConfigAsync(departmentId, config, cancellationToken);

			await RaiseAsync(departmentId, userId, patterns, true, result, cancellationToken);
			return result;
		}

		/// <summary>Writes each posted kind's pattern; a blank one returns the kind to its built-in numbers, a refused one keeps what it had.</summary>
		private static void ApplyPatterns(List<DocumentNumberPattern> target, IEnumerable<DocumentNumberPatternUpdate> updates, bool records, DocumentNumberingSaveResult result)
		{
			foreach (var update in (updates ?? Enumerable.Empty<DocumentNumberPatternUpdate>()).Where(u => u != null))
			{
				var kind = DocumentNumberKinds.Find(update.Kind);
				if (kind == null || kind.IsRecords != records)
					continue;

				if (string.IsNullOrWhiteSpace(update.Pattern))
				{
					DocumentNumbering.SetPattern(target, kind.Key, null, 0);
					continue;
				}

				if (!DocumentNumbering.IsValid(kind, update.Pattern))
				{
					result.PatternsRejected.Add(kind.Key);
					continue;
				}

				DocumentNumbering.SetPattern(target, kind.Key, update.Pattern.Trim(), update.SequenceWidth);
			}
		}

		/// <summary>
		/// Raises each requested next number, checked against what was just saved: a raise typed against a sequence the department
		/// no longer issues from (its pattern or year start changed in the same save, or the kind went back to built-in numbers)
		/// is not applied, and one below what the sequence already issues next is refused.
		/// </summary>
		private async Task RaiseAsync(int departmentId, string userId, IEnumerable<DocumentNumberPatternUpdate> updates, bool records, DocumentNumberingSaveResult result, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var timeZone = await TimeZoneAsync(departmentId);
			foreach (var update in (updates ?? Enumerable.Empty<DocumentNumberPatternUpdate>()).Where(u => u?.NextSequence != null))
			{
				var kind = DocumentNumberKinds.Find(update.Kind);
				if (kind == null || kind.IsRecords != records || result.PatternsRejected.Contains(kind.Key))
					continue;

				var current = await StatusAsync(departmentId, kind, await GetPatternAsync(departmentId, kind.Key, true), await YearStartAsync(departmentId, kind.Key, true), now, timeZone);
				if (!current.Custom || !string.Equals(current.ScopeKey, update.ScopeKey, StringComparison.Ordinal))
					result.NotApplied++;
				else if (update.NextSequence.Value < current.NextSequence)
					result.BelowCurrent.Add(current);
				else
					await _sequences.RaiseFloorAsync(departmentId, kind.Key, current.ScopeKey, Math.Min(update.NextSequence.Value, CallNumberFormat.MaxSequence), userId, now, cancellationToken);
			}
		}

		private async Task<DocumentNumberStatus> StatusAsync(int departmentId, DocumentNumberKind kind, DocumentNumberPattern pattern, NumberingYearStart yearStart, DateTime utcDate, string timeZone)
		{
			var local = ToLocal(utcDate, timeZone);
			if (pattern == null)
			{
				var legacy = CallNumberFormat.Resolve(kind.LegacyPattern, kind.LegacyWidth, local, yearStart);
				return new DocumentNumberStatus
				{
					Kind = kind.Key,
					Custom = false,
					Pattern = kind.LegacyPattern,
					SequenceWidth = kind.LegacyWidth,
					Period = legacy.Period,
					Example = legacy.Format(SampleSequence)
				};
			}

			var scope = CallNumberFormat.Resolve(pattern.Pattern, pattern.SequenceWidth, local, yearStart);
			var existing = await _sequences.GetSequenceAsync(departmentId, kind.Key, scope.Key);
			var last = existing?.LastSequence ?? await _sequences.GetHighestIssuedAsync(departmentId, kind.Key, scope.Prefix, scope.Suffix);
			var next = Math.Max(last + 1, existing?.FloorSequence ?? 0);

			return new DocumentNumberStatus
			{
				Kind = kind.Key,
				Custom = true,
				Pattern = pattern.Pattern,
				SequenceWidth = pattern.SequenceWidth,
				ScopeKey = scope.Key,
				Period = scope.Period,
				NextSequence = next,
				NextNumber = scope.Format(next),
				Example = scope.Format(SampleSequence),
				FloorSequence = existing?.FloorSequence ?? 0
			};
		}

		private async Task<NumberingYearStart> YearStartAsync(int departmentId, string kind, bool bypassCache)
		{
			if (DocumentNumberKinds.Find(kind)?.IsRecords == true)
				return (await _settings.GetRecordsNumberingConfigAsync(departmentId, bypassCache) ?? new RecordsNumberingConfig()).YearStart();

			return (await _settings.GetDocumentNumberingConfigAsync(departmentId, bypassCache) ?? new DocumentNumberingConfig()).YearStart();
		}

		private async Task<string> TimeZoneAsync(int departmentId)
		{
			return (await _departments.GetDepartmentByIdAsync(departmentId, false))?.TimeZone;
		}

		private static DateTime ToLocal(DateTime utcDate, string timeZone)
		{
			return string.IsNullOrWhiteSpace(timeZone) ? utcDate : DateTimeHelpers.GetLocalDateTime(utcDate, timeZone);
		}
	}
}
