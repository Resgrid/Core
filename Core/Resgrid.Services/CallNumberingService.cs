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
			var saved = config.YearStart();
			var month = update.YearStartMonth ?? saved.Month;
			var day = update.YearStartDay ?? saved.Day;
			if (!NumberingYearStart.IsValid(month, day))
			{
				result.YearStartRejected = true;
				return result;
			}

			config.Pattern = pattern;
			config.SequenceWidth = CallNumberFormat.EffectiveWidth(update.SequenceWidth);
			config.YearStartMonth = month;
			config.YearStartDay = day;
			config.YearLabel = (int)new NumberingYearStart(month, day, (NumberingYearLabel)(update.YearLabel ?? (int)saved.Label)).Label;
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

		public async Task<bool> RenumberCallsForYearAsync(int departmentId, DateTime inYearUtc, CancellationToken cancellationToken = default(CancellationToken))
		{
			var config = await _settings.GetCallNumberingConfigAsync(departmentId, true);
			var pattern = CallNumberFormat.EffectivePattern(config);
			var period = CallNumberFormat.ResetPeriod(pattern);
			if (period == CallNumberResetPeriod.Never)
				return false;

			// The numbering year for a yearly pattern (a fiscal year when the department set one), the calendar year for a pattern
			// with {MM} or {DD}: either way every scope inside it is renumbered whole.
			var timeZone = await TimeZoneAsync(departmentId);
			var yearStart = period == CallNumberResetPeriod.Yearly ? config.YearStart() : NumberingYearStart.Calendar;
			var localStart = yearStart.StartOf(ToLocal(inYearUtc, timeZone));
			var start = ToUtc(localStart, timeZone);
			var end = ToUtc(localStart.AddYears(1), timeZone);
			var calls = (await _calls.GetAllCallsByDepartmentDateRangeAsync(departmentId, start, end) ?? Enumerable.Empty<Call>())
				.Where(c => c.LoggedOn < end).OrderBy(c => c.LoggedOn).ThenBy(c => c.CallId).ToList();

			// Deleted calls keep their numbers, so no active call is renumbered onto one of them.
			var held = new HashSet<string>(await _sequences.GetDeletedCallNumbersAsync(departmentId, start, end) ?? new List<string>(), StringComparer.Ordinal);

			// So do the calls either side of a yearly period inside the window its name can also have been written in (see
			// HighestIssuedAsync): after a year start change, last year's calendar numbers can read the same as this fiscal year's.
			if (period == CallNumberResetPeriod.Yearly)
			{
				var year = yearStart.YearOf(localStart);
				var windowStart = ToUtc(new DateTime(year - 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), timeZone);
				var windowEnd = ToUtc(new DateTime(year + 2, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), timeZone);
				if (windowStart < start)
					held.UnionWith(await _sequences.GetCallNumbersAsync(departmentId, windowStart, start) ?? new List<string>());
				if (end < windowEnd)
					held.UnionWith(await _sequences.GetCallNumbersAsync(departmentId, end, windowEnd) ?? new List<string>());
			}

			// Every number is worked out before any call changes. Each sequence starts again at its raised starting point, so a
			// department continuing earlier numbers keeps them.
			var issued = new Dictionary<string, (CallNumberScope Scope, int Last)>(StringComparer.Ordinal);
			var renumbered = new List<(Call Call, string Number)>(calls.Count);
			foreach (var call in calls)
			{
				var scope = Resolve(config, call.LoggedOn, timeZone);
				if (!issued.TryGetValue(scope.Key, out var entry))
				{
					var floor = (await _sequences.GetSequenceAsync(departmentId, scope.Key))?.FloorSequence ?? 0;
					entry = (scope, Math.Max(0, floor - 1));
				}

				string number;
				do
				{
					entry.Last++;
					number = scope.Format(entry.Last);
				} while (held.Contains(number));

				issued[scope.Key] = entry;
				renumbered.Add((call, number));
			}

			// Move each counter past every number its period holds now or will hold afterwards before rewriting anything: a call
			// created while the year is rewritten then takes a sequence this renumbering never hands out.
			var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var entry in issued.Values)
			{
				var highest = await HighestIssuedAsync(departmentId, entry.Scope, timeZone);
				reserved[entry.Scope.Key] = await _sequences.RaiseLastSequenceAsync(departmentId, entry.Scope.Key, Math.Max(entry.Last, highest), cancellationToken);
			}

			foreach (var (call, number) in renumbered)
			{
				if (string.Equals(call.Number, number, StringComparison.Ordinal))
					continue;

				call.Number = number;
				await _calls.SaveOrUpdateAsync(call, cancellationToken);
			}

			// The counter comes back down to the highest number the scope now holds (deleted calls included), but only if no call
			// took a sequence meanwhile; otherwise it stays above that call's number.
			foreach (var entry in issued.Values)
			{
				var highest = await HighestIssuedAsync(departmentId, entry.Scope, timeZone);
				await _sequences.TrySetLastSequenceAsync(departmentId, entry.Scope.Key, Math.Max(entry.Last, highest), reserved[entry.Scope.Key], cancellationToken);
			}

			return true;
		}

		private static CallNumberScope Resolve(CallNumberingConfig config, DateTime utcDate, string timeZone)
		{
			return CallNumberFormat.Resolve(CallNumberFormat.EffectivePattern(config), config?.SequenceWidth ?? 0, ToLocal(utcDate, timeZone), config?.YearStart());
		}

		private Task<int> HighestIssuedAsync(int departmentId, CallNumberScope scope, string timeZone)
		{
			DateTime? from = scope.PeriodStart, to = scope.PeriodEnd;
			// A yearly scope's text can also have been written outside its own period: the same name under the calendar year, or
			// under a year start or naming the department used before. Every year named Y starts after January 1 of Y-1 and ends
			// before January 1 of Y+2, so searching that window finds them and a new scope never re-issues one of their numbers.
			if (scope.Period == CallNumberResetPeriod.Yearly && scope.Year.HasValue && scope.Year.Value > 1 && scope.Year.Value < 9998)
			{
				from = new DateTime(scope.Year.Value - 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
				to = new DateTime(scope.Year.Value + 2, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
			}

			return _sequences.GetHighestIssuedAsync(departmentId, scope.Prefix, scope.Suffix,
				from.HasValue ? ToUtc(from.Value, timeZone) : (DateTime?)null, to.HasValue ? ToUtc(to.Value, timeZone) : (DateTime?)null);
		}

		private static DateTime ToLocal(DateTime utcDate, string timeZone)
		{
			return string.IsNullOrWhiteSpace(timeZone) ? utcDate : DateTimeHelpers.GetLocalDateTime(utcDate, timeZone);
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
