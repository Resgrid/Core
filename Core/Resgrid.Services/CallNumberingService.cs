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
	public class CallNumberingService : ICallNumberingService
	{
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly ICallNumberSequencesRepository _sequences;
		private readonly ICallsRepository _calls;

		public CallNumberingService(IDepartmentSettingsService settings, IDepartmentsService departments, ICallNumberSequencesRepository sequences, ICallsRepository calls)
		{
			_settings = settings;
			_departments = departments;
			_sequences = sequences;
			_calls = calls;
		}

		public async Task<string> AllocateCallNumberAsync(int departmentId, DateTime loggedOnUtc, CancellationToken cancellationToken = default(CancellationToken))
		{
			var config = await _settings.GetCallNumberingConfigAsync(departmentId);
			var timeZone = await TimeZoneAsync(departmentId);
			var scope = Resolve(config, loggedOnUtc, timeZone);

			// Only the first call of a scope pays for the seed scan; two first calls racing both seed the same value and the
			// atomic upsert still hands them consecutive sequences.
			var seed = await _sequences.GetSequenceAsync(departmentId, scope.Key) == null ? await HighestIssuedAsync(departmentId, scope, timeZone) : 0;
			var sequence = await _sequences.TakeNextAsync(departmentId, scope.Key, seed, cancellationToken);

			return scope.Format(sequence);
		}

		public async Task<CallNumberSequenceStatus> GetNextAsync(int departmentId, CallNumberingConfig config, DateTime utcDate)
		{
			config ??= await _settings.GetCallNumberingConfigAsync(departmentId);
			var timeZone = await TimeZoneAsync(departmentId);
			var scope = Resolve(config, utcDate, timeZone);

			var existing = await _sequences.GetSequenceAsync(departmentId, scope.Key);
			var last = existing?.LastSequence ?? await HighestIssuedAsync(departmentId, scope, timeZone);
			var next = Math.Max(last + 1, existing?.FloorSequence ?? 0);

			return new CallNumberSequenceStatus
			{
				ScopeKey = scope.Key,
				Period = scope.Period,
				NextSequence = next,
				NextNumber = scope.Format(next),
				FloorSequence = existing?.FloorSequence ?? 0
			};
		}

		public async Task<CallNumberingSaveResult> SaveAsync(int departmentId, string userId, CallNumberingUpdate update, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = new CallNumberingSaveResult();
			var pattern = CallNumberFormat.Normalize(update?.Pattern);
			if (pattern == null)
			{
				result.PatternRejected = true;
				return result;
			}

			var config = await _settings.GetCallNumberingConfigAsync(departmentId, true) ?? new CallNumberingConfig();
			config.Pattern = pattern;
			config.SequenceWidth = CallNumberFormat.EffectiveWidth(update.SequenceWidth);
			await _settings.SetCallNumberingConfigAsync(departmentId, config, cancellationToken);

			if (update.NextSequence.HasValue)
			{
				// Checked against the pattern just saved: a number typed against the old pattern's sequence is not carried to a new one.
				var current = await GetNextAsync(departmentId, config, DateTime.UtcNow);
				if (!string.Equals(current.ScopeKey, update.ScopeKey, StringComparison.Ordinal))
					result.NextNotApplied = true;
				else if (update.NextSequence.Value < current.NextSequence)
					result.BelowCurrent = current;
				else
					await _sequences.RaiseFloorAsync(departmentId, current.ScopeKey, Math.Min(update.NextSequence.Value, CallNumberFormat.MaxSequence), userId, DateTime.UtcNow, cancellationToken);
			}

			return result;
		}

		public async Task<bool> RenumberCallsForYearAsync(int departmentId, int year, CancellationToken cancellationToken = default(CancellationToken))
		{
			var config = await _settings.GetCallNumberingConfigAsync(departmentId, true);
			var pattern = CallNumberFormat.EffectivePattern(config);
			if (CallNumberFormat.ResetPeriod(pattern) == CallNumberResetPeriod.Never)
				return false;

			var timeZone = await TimeZoneAsync(departmentId);
			var start = ToUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), timeZone);
			var end = ToUtc(new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), timeZone);
			var calls = (await _calls.GetAllCallsByDepartmentDateRangeAsync(departmentId, start, end) ?? Enumerable.Empty<Call>())
				.Where(c => c.LoggedOn < end).OrderBy(c => c.LoggedOn).ThenBy(c => c.CallId).ToList();

			// Each sequence starts again at its raised starting point, so a department continuing earlier numbers keeps them.
			var issued = new Dictionary<string, (CallNumberScope Scope, int Last)>(StringComparer.Ordinal);
			foreach (var call in calls)
			{
				var scope = Resolve(config, call.LoggedOn, timeZone);
				if (!issued.TryGetValue(scope.Key, out var entry))
				{
					var floor = (await _sequences.GetSequenceAsync(departmentId, scope.Key))?.FloorSequence ?? 0;
					entry = (scope, Math.Max(0, floor - 1));
				}

				entry.Last++;
				issued[scope.Key] = entry;
				call.Number = scope.Format(entry.Last);
				await _calls.SaveOrUpdateAsync(call, cancellationToken);
			}

			// Deleted calls keep their numbers, so a sequence resumes above whatever its scope still holds.
			foreach (var entry in issued.Values)
			{
				var highest = await HighestIssuedAsync(departmentId, entry.Scope, timeZone);
				await _sequences.SetLastSequenceAsync(departmentId, entry.Scope.Key, Math.Max(entry.Last, highest), cancellationToken);
			}

			return true;
		}

		private static CallNumberScope Resolve(CallNumberingConfig config, DateTime utcDate, string timeZone)
		{
			var local = string.IsNullOrWhiteSpace(timeZone) ? utcDate : DateTimeHelpers.GetLocalDateTime(utcDate, timeZone);
			return CallNumberFormat.Resolve(CallNumberFormat.EffectivePattern(config), config?.SequenceWidth ?? 0, local);
		}

		private Task<int> HighestIssuedAsync(int departmentId, CallNumberScope scope, string timeZone)
		{
			DateTime? from = scope.PeriodStart.HasValue ? ToUtc(scope.PeriodStart.Value, timeZone) : (DateTime?)null;
			DateTime? to = scope.PeriodEnd.HasValue ? ToUtc(scope.PeriodEnd.Value, timeZone) : (DateTime?)null;
			return _sequences.GetHighestIssuedAsync(departmentId, scope.Prefix, scope.Suffix, from, to);
		}

		private async Task<string> TimeZoneAsync(int departmentId)
		{
			return (await _departments.GetDepartmentByIdAsync(departmentId, false))?.TimeZone;
		}

		/// <summary>Local midnight can fall in a DST gap in some zones, so period bounds resolve leniently.</summary>
		private static DateTime ToUtc(DateTime local, string timeZone)
		{
			return string.IsNullOrWhiteSpace(timeZone)
				? DateTime.SpecifyKind(local, DateTimeKind.Utc)
				: DateTimeHelpers.ConvertToUtc(local, timeZone, lenient: true);
		}
	}
}
