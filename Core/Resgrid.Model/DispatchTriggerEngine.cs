using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// What a trigger is built on (Protocols P4 shared trigger engine). The first three are the values both trigger tables already store
	/// (ProtocolTriggerTypes and RunCardTriggerTypes agree on them); <see cref="Conditions"/> matches any call and relies on its P4
	/// criteria alone. Stored; never renumber.
	/// </summary>
	public enum DispatchTriggerBases
	{
		Priority = 0,
		CallType = 1,
		PriorityAndType = 2,

		/// <summary>Any call, narrowed only by the trigger's conditions (zone, schedule, keywords, POI type, intake source); at least one is required.</summary>
		Conditions = 3
	}

	/// <summary>How a trigger's call type is compared with the call's.</summary>
	public enum DispatchTriggerTypeMatching
	{
		/// <summary>Run cards: the type id only; a trigger or call without one never matches a type trigger.</summary>
		IdOnly = 0,

		/// <summary>
		/// Protocols (P0, M0336): the id when the trigger has one; a trigger with no id and no name (or the old English "No Type" label)
		/// matches a call that has no type; a trigger written before M0336 with only a name matches the call's type name.
		/// </summary>
		IdNameOrNoType = 1
	}

	/// <summary>
	/// The P4 criteria a trigger can add to its basis. Both <see cref="DispatchProtocolTrigger"/> and <see cref="RunCardTrigger"/> store them
	/// (M0399) and the one engine reads them. Every criterion that is set must hold (AND); triggers on one protocol or card are OR'd.
	/// </summary>
	public interface IDispatchTriggerCriteria
	{
		/// <summary>The call's location lies inside this station or group's geofence (DepartmentGroups.Geofence; areas are drawn once, decision 4).</summary>
		int? GeofenceGroupId { get; }

		/// <summary>Department-local days the trigger applies on: bit (1 &lt;&lt; (int)DayOfWeek), Sunday = 1 … Saturday = 64. Null or 0 = every day.</summary>
		int? DaysOfWeek { get; }

		/// <summary>Department-local start of the daily window, minutes after midnight (0–1439). With <see cref="EndMinute"/>; a start after the end runs overnight.</summary>
		int? StartMinute { get; }

		/// <summary>Department-local end of the daily window, minutes after midnight (exclusive, 1–1440).</summary>
		int? EndMinute { get; }

		/// <summary>Comma- or line-separated words or phrases; any one of them in the call's nature or notes (case-insensitive) matches.</summary>
		string Keywords { get; }

		/// <summary>The POI type of the POI the call's location was picked from (Call.LocationPoiId), e.g. a nursing home or a school (occupancy type).</summary>
		int? PoiTypeId { get; }

		/// <summary>Comma-separated <see cref="CallSources"/> values (the intake classification: dispatcher form, e-mail, voice, intake channel…).</summary>
		string CallSources { get; }
	}

	/// <summary>
	/// What is known about a call when triggers are evaluated. Built once per request by the facts builder (ProtocolsService /
	/// DispatchTriggerFactsBuilder); a fact left unknown (null) never satisfies a criterion that needs it.
	/// </summary>
	public class DispatchTriggerFacts
	{
		public int Priority { get; set; }

		/// <summary>The department's call type id, or null when the call has no type.</summary>
		public int? CallTypeId { get; set; }

		/// <summary>The call's type name as posted; only used for protocol triggers written before M0336 that have no type id.</summary>
		public string CallTypeName { get; set; }

		/// <summary>The call's logged time in UTC; trigger windows are compared with it (not with the current time).</summary>
		public DateTime AtUtc { get; set; }

		/// <summary>The call's logged time in the department's time zone (day/time schedules); null when it could not be worked out.</summary>
		public DateTime? LocalTime { get; set; }

		public string Nature { get; set; }

		public string Notes { get; set; }

		/// <summary>Groups whose geofence contains the call's location; null when the call has no usable location.</summary>
		public HashSet<int> GeofenceGroupIds { get; set; }

		/// <summary>The POI type of Call.LocationPoiId; null when the location was not picked from a POI.</summary>
		public int? LocationPoiTypeId { get; set; }

		/// <summary>Call.CallSource; 0 (a form that leaves it unset) is read as <see cref="CallSources.User"/>.</summary>
		public int? CallSource { get; set; }
	}

	/// <summary>One trigger as the engine reads it, whichever table it came from.</summary>
	public sealed class DispatchTriggerRule
	{
		public int Basis { get; set; }
		public int? Priority { get; set; }
		public int? CallTypeId { get; set; }

		/// <summary>The stored type name (protocol triggers only; display and pre-M0336 matching).</summary>
		public string CallTypeName { get; set; }

		public DispatchTriggerTypeMatching TypeMatching { get; set; }
		public DateTime? StartsOn { get; set; }
		public DateTime? EndsOn { get; set; }
		public IDispatchTriggerCriteria Criteria { get; set; }
	}

	/// <summary>
	/// The one trigger evaluator run cards and protocols share (Protocols P4): call type id (not name), priority, the trigger's UTC
	/// window, and the P4 criteria — zone/geofence, department-local day and time, nature/notes keywords, the POI type at the location,
	/// and the intake classification (call source). Pure: the facts are resolved by the caller, so this is the only place the rules live.
	/// A trigger without P4 criteria matches exactly as P0's protocol matcher and the run card matcher did before (characterization tests).
	/// </summary>
	public static class DispatchTriggerEngine
	{
		public const int KeywordsMaxLength = 500;
		public const int CallSourcesMaxLength = 50;
		public const int MaxKeywords = 25;
		public const int MinutesPerDay = 1440;
		public const int EveryDay = 127;

		/// <summary>The old English label the protocol trigger form stored for "no type".</summary>
		public const string LegacyNoTypeLabel = "No Type";

		public static bool InWindow(DateTime? startsOn, DateTime? endsOn, DateTime atUtc)
		{
			if (startsOn.HasValue && startsOn.Value > atUtc)
				return false;

			if (endsOn.HasValue && endsOn.Value < atUtc)
				return false;

			return true;
		}

		/// <summary>True when the trigger applies to the call.</summary>
		public static bool Matches(DispatchTriggerRule rule, DispatchTriggerFacts call) => Specificity(rule, call).HasValue;

		/// <summary>
		/// How specific a matching trigger is, or null when it does not match: priority 1, type 2, priority and type 3 (the run card
		/// values, unchanged), plus 3 for each P4 criterion it also needs, so a narrower trigger wins over a broader one. A
		/// conditions-only trigger counts its criteria alone.
		/// </summary>
		public static int? Specificity(DispatchTriggerRule rule, DispatchTriggerFacts call)
		{
			if (rule == null || call == null)
				return null;

			if (!InWindow(rule.StartsOn, rule.EndsOn, call.AtUtc))
				return null;

			int specificity;
			switch ((DispatchTriggerBases)rule.Basis)
			{
				case DispatchTriggerBases.Priority:
					if (!PriorityMatches(rule, call))
						return null;
					specificity = 1;
					break;
				case DispatchTriggerBases.CallType:
					if (!TypeMatches(rule, call))
						return null;
					specificity = 2;
					break;
				case DispatchTriggerBases.PriorityAndType:
					if (!PriorityMatches(rule, call) || !TypeMatches(rule, call))
						return null;
					specificity = 3;
					break;
				case DispatchTriggerBases.Conditions:
					if (CriteriaCount(rule.Criteria) == 0)
						return null;
					specificity = 0;
					break;
				default:
					return null;
			}

			var criteria = rule.Criteria;
			if (criteria == null)
				return specificity;

			var count = 0;
			if (criteria.GeofenceGroupId.HasValue)
			{
				if (call.GeofenceGroupIds == null || !call.GeofenceGroupIds.Contains(criteria.GeofenceGroupId.Value))
					return null;
				count++;
			}

			if (HasSchedule(criteria))
			{
				if (!ScheduleMatches(criteria, call.LocalTime))
					return null;
				count++;
			}

			var keywords = ParseKeywords(criteria.Keywords);
			if (keywords.Count > 0)
			{
				if (!KeywordsMatch(keywords, call.Nature, call.Notes))
					return null;
				count++;
			}

			if (criteria.PoiTypeId.HasValue)
			{
				if (!call.LocationPoiTypeId.HasValue || call.LocationPoiTypeId.Value != criteria.PoiTypeId.Value)
					return null;
				count++;
			}

			var sources = ParseCallSources(criteria.CallSources);
			if (sources.Count > 0)
			{
				var source = call.CallSource.GetValueOrDefault() <= 0 ? (int)Model.CallSources.User : call.CallSource.Value;
				if (!sources.Contains(source))
					return null;
				count++;
			}

			return specificity + 3 * count;
		}

		/// <summary>How many P4 criteria the trigger sets.</summary>
		public static int CriteriaCount(IDispatchTriggerCriteria criteria)
		{
			if (criteria == null)
				return 0;

			var count = 0;
			if (criteria.GeofenceGroupId.HasValue) count++;
			if (HasSchedule(criteria)) count++;
			if (ParseKeywords(criteria.Keywords).Count > 0) count++;
			if (criteria.PoiTypeId.HasValue) count++;
			if (ParseCallSources(criteria.CallSources).Count > 0) count++;
			return count;
		}

		/// <summary>
		/// What is wrong with a trigger's P4 criteria, as English sentences for API callers (the web editor shows its own text). Empty when
		/// they are valid. A conditions-only trigger needs at least one criterion.
		/// </summary>
		public static List<string> Validate(int basis, IDispatchTriggerCriteria criteria)
		{
			var errors = new List<string>();
			if (!Enum.IsDefined(typeof(DispatchTriggerBases), basis))
				errors.Add("Unknown trigger type.");

			if (basis == (int)DispatchTriggerBases.Conditions && CriteriaCount(criteria) == 0)
				errors.Add("A trigger for any call needs at least one condition (zone, schedule, keywords, POI type or intake source).");

			if (criteria == null)
				return errors;

			if (criteria.DaysOfWeek.HasValue && (criteria.DaysOfWeek.Value < 0 || criteria.DaysOfWeek.Value > EveryDay))
				errors.Add("The days of the week are not valid.");

			if (criteria.StartMinute.HasValue != criteria.EndMinute.HasValue)
				errors.Add("A time window needs both a start and an end.");
			else if (criteria.StartMinute.HasValue && (criteria.StartMinute.Value < 0 || criteria.StartMinute.Value >= MinutesPerDay
				|| criteria.EndMinute.Value <= 0 || criteria.EndMinute.Value > MinutesPerDay || criteria.StartMinute.Value == criteria.EndMinute.Value))
				errors.Add("The time window is not valid.");

			if ((criteria.Keywords ?? string.Empty).Length > KeywordsMaxLength)
				errors.Add($"Keywords can be at most {KeywordsMaxLength} characters.");

			if (!string.IsNullOrWhiteSpace(criteria.CallSources) && ParseCallSources(criteria.CallSources).Count == 0)
				errors.Add("The intake sources are not valid.");

			return errors;
		}

		public static bool HasSchedule(IDispatchTriggerCriteria criteria) =>
			criteria != null && ((criteria.DaysOfWeek.HasValue && criteria.DaysOfWeek.Value > 0 && criteria.DaysOfWeek.Value != EveryDay)
				|| (criteria.StartMinute.HasValue && criteria.EndMinute.HasValue));

		/// <summary>
		/// The department-local day and time against the schedule. A window whose start is after its end runs overnight: the part after
		/// midnight belongs to the day the window started on (Friday 22:00–06:00 covers Saturday 02:00).
		/// </summary>
		public static bool ScheduleMatches(IDispatchTriggerCriteria criteria, DateTime? localTime)
		{
			if (!HasSchedule(criteria))
				return true;
			if (!localTime.HasValue)
				return false;

			var days = criteria.DaysOfWeek.GetValueOrDefault() <= 0 ? EveryDay : criteria.DaysOfWeek.Value;
			var minute = localTime.Value.Hour * 60 + localTime.Value.Minute;
			var day = localTime.Value.DayOfWeek;

			if (!criteria.StartMinute.HasValue || !criteria.EndMinute.HasValue)
				return DayIn(days, day);

			var start = criteria.StartMinute.Value;
			var end = criteria.EndMinute.Value;

			if (start < end)
				return DayIn(days, day) && minute >= start && minute < end;

			// Overnight.
			if (minute >= start)
				return DayIn(days, day);
			if (minute < end)
				return DayIn(days, (DayOfWeek)(((int)day + 6) % 7));

			return false;
		}

		private static bool DayIn(int days, DayOfWeek day) => (days & (1 << (int)day)) != 0;

		/// <summary>The keywords as a list: split on commas, semicolons and line breaks, trimmed, empty ones dropped, at most <see cref="MaxKeywords"/>.</summary>
		public static List<string> ParseKeywords(string keywords)
		{
			if (string.IsNullOrWhiteSpace(keywords))
				return new List<string>();

			return keywords.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(k => Regex.Replace(k.Trim(), @"\s+", " "))
				.Where(k => k.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Take(MaxKeywords)
				.ToList();
		}

		/// <summary>The keywords stored back as one normalized, comma-separated value (null when there are none).</summary>
		public static string NormalizeKeywords(string keywords)
		{
			var list = ParseKeywords(keywords);
			if (list.Count == 0)
				return null;

			var joined = string.Join(", ", list);
			return joined.Length > KeywordsMaxLength ? joined.Substring(0, KeywordsMaxLength) : joined;
		}

		public static bool KeywordsMatch(IReadOnlyCollection<string> keywords, string nature, string notes)
		{
			if (keywords == null || keywords.Count == 0)
				return true;

			var text = " " + Flatten(nature) + " " + Flatten(notes) + " ";
			return keywords.Any(k => text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		/// <summary>Plain text for keyword matching: tags dropped, entities decoded, whitespace folded.</summary>
		public static string Flatten(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;

			var plain = Regex.Replace(value, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
			plain = System.Net.WebUtility.HtmlDecode(plain);
			return Regex.Replace(plain, @"\s+", " ").Trim();
		}

		public static HashSet<int> ParseCallSources(string sources)
		{
			var set = new HashSet<int>();
			if (string.IsNullOrWhiteSpace(sources))
				return set;

			foreach (var part in sources.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (int.TryParse(part.Trim(), out var value) && Enum.IsDefined(typeof(CallSources), value))
					set.Add(value);
			}

			return set;
		}

		public static string NormalizeCallSources(IEnumerable<int> sources)
		{
			var list = (sources ?? Enumerable.Empty<int>()).Where(s => Enum.IsDefined(typeof(CallSources), s)).Distinct().OrderBy(s => s).ToList();
			return list.Count == 0 ? null : string.Join(",", list);
		}

		private static bool PriorityMatches(DispatchTriggerRule rule, DispatchTriggerFacts call) =>
			rule.Priority.HasValue && rule.Priority.Value == call.Priority;

		private static bool TypeMatches(DispatchTriggerRule rule, DispatchTriggerFacts call)
		{
			if (rule.TypeMatching == DispatchTriggerTypeMatching.IdOnly)
				return rule.CallTypeId.HasValue && call.CallTypeId.HasValue && rule.CallTypeId.Value == call.CallTypeId.Value;

			if (rule.CallTypeId.HasValue)
				return call.CallTypeId.HasValue && call.CallTypeId.Value == rule.CallTypeId.Value;

			if (IsNoTypeName(rule.CallTypeName))
				return !call.CallTypeId.HasValue;

			// Written before M0336 and its type was not found by name then: the name is all there is, as before.
			return CallTypeLink.SameName(rule.CallTypeName, call.CallTypeName);
		}

		public static bool IsNoTypeName(string name) =>
			string.IsNullOrWhiteSpace(name) || CallTypeLink.SameName(name, LegacyNoTypeLabel);

		/// <summary>A protocol trigger as the engine reads it.</summary>
		public static DispatchTriggerRule ForProtocol(DispatchProtocolTrigger trigger) => trigger == null ? null : new DispatchTriggerRule
		{
			Basis = trigger.Type,
			Priority = trigger.Priority,
			CallTypeId = trigger.CallTypeId,
			CallTypeName = trigger.CallType,
			TypeMatching = DispatchTriggerTypeMatching.IdNameOrNoType,
			StartsOn = trigger.StartsOn,
			EndsOn = trigger.EndsOn,
			Criteria = trigger
		};

		/// <summary>A run card trigger as the engine reads it.</summary>
		public static DispatchTriggerRule ForRunCard(RunCardTrigger trigger) => trigger == null ? null : new DispatchTriggerRule
		{
			Basis = trigger.TriggerType,
			Priority = trigger.Priority,
			CallTypeId = trigger.CallTypeId,
			TypeMatching = DispatchTriggerTypeMatching.IdOnly,
			StartsOn = trigger.StartsOn,
			EndsOn = trigger.EndsOn,
			Criteria = trigger
		};
	}
}
