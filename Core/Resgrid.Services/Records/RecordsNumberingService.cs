using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	public class RecordsNumberingService : IRecordsNumberingService
	{
		private readonly IDepartmentSettingsService _settings;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IDepartmentGroupsService _groups;

		public RecordsNumberingService(IDepartmentSettingsService settings, IRmsOperationalRecordsRepository records, IDepartmentGroupsService groups)
		{
			_settings = settings;
			_records = records;
			_groups = groups;
		}

		/// <summary>
		/// Every record type the department pattern numbers, with the prefix {PREFIX} renders for it under <paramref name="config"/>
		/// (the shipped defaults when null). Department definitions number themselves.
		/// </summary>
		public static IEnumerable<KeyValuePair<string, string>> NumberedTypes(RecordsNumberingConfig config = null)
		{
			config ??= new RecordsNumberingConfig();
			foreach (var key in RmsDefinitionKeys.LockedTypes.Keys)
				yield return new KeyValuePair<string, string>(key, config.PrefixFor(key));
			yield return new KeyValuePair<string, string>(RmsDefinitionKeys.NerisIncidentReport, config.PrefixFor(RmsDefinitionKeys.NerisIncidentReport));
		}

		public static bool IsNumberedType(string definitionKey)
		{
			return definitionKey != null && NumberedTypes().Any(t => string.Equals(t.Key, definitionKey, StringComparison.Ordinal));
		}

		public async Task<List<RecordNumberSequenceStatus>> GetSequencesAsync(int departmentId, RecordsNumberingConfig config, int year)
		{
			config ??= new RecordsNumberingConfig();
			var pattern = RecordNumberFormat.EffectivePattern(config);
			var groups = new List<int?> { null };
			if (RecordNumberFormat.UsesToken(pattern, RecordNumberFormat.GroupToken))
				groups.AddRange((await _groups.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
					.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => (int?)g.DepartmentGroupId));

			var sequences = new List<RecordNumberSequenceStatus>();
			foreach (var group in groups)
			{
				foreach (var type in NumberedTypes(config))
				{
					var scope = RecordNumberFormat.Resolve(pattern, config.SequenceWidth, type.Value, year, group);
					var shared = sequences.FirstOrDefault(s => s.ScopeKey == scope.Key);
					if (shared != null)
					{
						shared.DefinitionKeys.Add(type.Key);
						continue;
					}

					var highest = await _records.GetMaxRecordNumberSequenceAsync(departmentId, scope.Prefix, scope.Suffix);
					var next = config.NextSequence(scope.Key, highest);
					sequences.Add(new RecordNumberSequenceStatus
					{
						ScopeKey = scope.Key,
						DefinitionKeys = new List<string> { type.Key },
						GroupId = group,
						HighestIssued = highest,
						NextSequence = next,
						NextNumber = scope.Format(next)
					});
				}
			}

			return sequences;
		}

		public async Task<RecordsNumberingSaveResult> SaveAsync(int departmentId, string userId, RecordsNumberingUpdate update, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = new RecordsNumberingSaveResult();
			var pattern = RecordNumberFormat.Normalize(update?.Pattern);
			if (pattern == null)
			{
				result.PatternRejected = true;
				return result;
			}

			var config = await _settings.GetRecordsNumberingConfigAsync(departmentId, true) ?? new RecordsNumberingConfig();
			config.Pattern = pattern;
			config.SequenceWidth = RecordNumberFormat.EffectiveWidth(update.SequenceWidth);
			config.NumberAssignment = (int)RmsNumberAssignment.OnFinalize;
			// Kept in step with the pattern for anything that still reads the flags.
			config.IncludeYear = RecordNumberFormat.ResetsYearly(pattern);
			config.ResetYearly = config.IncludeYear;
			config.PerGroupSequence = RecordNumberFormat.UsesToken(pattern, RecordNumberFormat.GroupToken);

			// Applied before the next numbers below: a new prefix is a new sequence, so a number typed against the old one does not carry over.
			foreach (var request in (update.Prefixes ?? new List<RecordNumberPrefixRequest>()).Where(r => r != null && IsNumberedType(r.DefinitionKey)))
			{
				var prefix = string.IsNullOrWhiteSpace(request.Prefix) ? null : request.Prefix.Trim().ToUpperInvariant();
				if (prefix != null && !RecordNumberFormat.IsValidPrefix(prefix))
				{
					result.PrefixesRejected.Add(request.DefinitionKey);
					continue;
				}

				config.SetPrefix(request.DefinitionKey, prefix);
			}

			var requests = (update.NextNumbers ?? new List<RecordNextNumberRequest>()).Where(r => r != null && !string.IsNullOrEmpty(r.ScopeKey)).ToList();
			if (requests.Count > 0)
			{
				// Checked against the pattern being saved: a number typed against the old pattern's sequence is not carried to a new one.
				var sequences = await GetSequencesAsync(departmentId, config, update.Year);
				var now = DateTime.UtcNow;
				foreach (var request in requests)
				{
					var sequence = sequences.FirstOrDefault(s => s.ScopeKey == request.ScopeKey);
					if (sequence == null)
					{
						result.NotApplied++;
						continue;
					}

					if (request.NextSequence < sequence.NextSequence)
					{
						result.BelowCurrent.Add(sequence);
						continue;
					}

					config.RaiseFloor(sequence.ScopeKey, Math.Min(request.NextSequence, RecordNumberFormat.MaxSequence), userId, now);
				}
			}

			await _settings.SetRecordsNumberingConfigAsync(departmentId, config, cancellationToken);
			return result;
		}
	}
}
