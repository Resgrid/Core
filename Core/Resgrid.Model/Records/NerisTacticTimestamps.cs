using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// The NERIS <c>tactic_timestamps</c> block (contract 1.4.78 <c>IncidentTacticTimestampsPayload</c>) and the rules that
	/// read it from Incident Command. Command objectives are free text, so a completed objective lands on a field only when
	/// its name says unambiguously which milestone it was; anything else stays a lookup for the officer, never a guess.
	/// </summary>
	public static class NerisTacticTimestamps
	{
		public const string CommandEstablished = "command_established";
		public const string CompletedSizeup = "completed_sizeup";
		public const string SuppressionComplete = "suppression_complete";
		public const string PrimarySearchBegin = "primary_search_begin";
		public const string PrimarySearchComplete = "primary_search_complete";
		public const string WaterOnFire = "water_on_fire";
		public const string FireUnderControl = "fire_under_control";
		public const string FireKnockedDown = "fire_knocked_down";
		public const string ExtricationComplete = "extrication_complete";

		/// <summary>The fields in contract order, which is also the order the form shows them.</summary>
		public static readonly IReadOnlyList<string> Fields = new[]
		{
			CommandEstablished, CompletedSizeup, SuppressionComplete, PrimarySearchBegin, PrimarySearchComplete,
			WaterOnFire, FireUnderControl, FireKnockedDown, ExtricationComplete
		};

		/// <summary>Source fact key for a prefilled tactic timestamp.</summary>
		public static string FactKey(string field) => "tactic_timestamps." + field;

		/// <summary>
		/// The tactic timestamp a completed command objective marks, or null when its name does not say. Matching is on
		/// whole words after lower-casing and dropping punctuation, and a negated or secondary milestone never matches
		/// ("secondary search complete" is not the primary search; "not under control" is not under control).
		/// </summary>
		public static string MatchObjective(string name)
		{
			var text = Normalize(name);
			if (text.Length == 0)
				return null;

			bool Has(string phrase) => (" " + text + " ").Contains(" " + phrase + " ");
			bool Done() => Has("complete") || Has("completed") || Has("done") || Has("finished") || Has("all clear") || Has("clear") || Has("negative");

			if (Has("not") || Has("no") || Has("secondary") || Has("failed"))
				return null;

			if (Has("extrication") && Done())
				return ExtricationComplete;

			if (Has("primary") || Has("primaries"))
			{
				if (Has("begin") || Has("began") || Has("begun") || Has("start") || Has("started") || Has("initiated") || Has("underway") || Has("in progress"))
					return PrimarySearchBegin;
				return Done() ? PrimarySearchComplete : null;
			}

			if (Has("water on fire") || Has("water on the fire") || Has("first water") || Has("water applied"))
				return WaterOnFire;

			if (Has("knockdown") || Has("knock down") || Has("knocked down"))
				return FireKnockedDown;

			if (Has("under control") || Has("fire controlled") || Has("fire control"))
				return FireUnderControl;

			if ((Has("suppression") || Has("overhaul")) && Done() || Has("fire out"))
				return SuppressionComplete;

			if ((Has("size up") || Has("sizeup") || Has("360")) && (Done() || Has("360")))
				return CompletedSizeup;

			return null;
		}

		/// <summary>
		/// The earliest completion per tactic timestamp from the command's completed objectives, plus command established.
		/// A field no objective names is absent rather than null.
		/// </summary>
		public static Dictionary<string, DateTime> FromCommand(DateTime? commandEstablishedOn, IEnumerable<(string Name, DateTime? CompletedOn)> completedObjectives)
		{
			var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
			if (commandEstablishedOn.HasValue)
				result[CommandEstablished] = commandEstablishedOn.Value;

			foreach (var (name, completedOn) in (completedObjectives ?? Enumerable.Empty<(string, DateTime?)>()).Where(o => o.Item2.HasValue).OrderBy(o => o.Item2))
			{
				var field = MatchObjective(name);
				if (field != null && field != CommandEstablished && !result.ContainsKey(field))
					result[field] = completedOn.Value;
			}

			return result;
		}

		private static string Normalize(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;

			var builder = new StringBuilder(value.Length);
			foreach (var c in value.ToLowerInvariant())
				builder.Append(char.IsLetterOrDigit(c) ? c : ' ');

			return string.Join(" ", builder.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
		}
	}
}
