using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.DispatcherDesk
{
	/// <summary>One row of a break plan editor (a shift's plan, or the department's default plan).</summary>
	public class DispatcherBreakPlanRow
	{
		/// <summary><see cref="DispatcherBreakTypes"/>.</summary>
		public int BreakType { get; set; } = (int)DispatcherBreakTypes.Break;

		public int DurationMinutes { get; set; }

		/// <summary>Minutes after the shift day starts; null = spread evenly.</summary>
		public int? TargetOffsetMinutes { get; set; }

		public static DispatcherBreakPlanRow From(DispatcherBreakPlanEntry entry) => new DispatcherBreakPlanRow
		{
			BreakType = entry.BreakType,
			DurationMinutes = entry.DurationMinutes,
			TargetOffsetMinutes = entry.TargetOffsetMinutes
		};
	}

	/// <summary>The break plan editor partial: its rows and the localized break type names.</summary>
	public class DispatcherBreakPlanEditorModel
	{
		/// <summary>Element id prefix, unique on the page.</summary>
		public string Id { get; set; } = "breakPlan";

		public List<DispatcherBreakPlanRow> Rows { get; set; } = new List<DispatcherBreakPlanRow>();
	}

	/// <summary>
	/// Reads the dispatcher desk fields that are posted as repeated rows rather than bound: break plan rows
	/// (breakPlanType_{n}, breakPlanMinutes_{n}, breakPlanOffset_{n}) and the standing dispatch roster (dispatchRoster and
	/// dispatchSupervisor, one value per checked person).
	/// </summary>
	public static class DispatcherDeskForms
	{
		public const int MaxBreakPlanRows = 10;
		public const int MaxBreakMinutes = 240;

		/// <summary>The longest shift offset a plan row may target (a week).</summary>
		public const int MaxTargetOffsetMinutes = 7 * 24 * 60;

		public const string MinutesPrefix = "breakPlanMinutes_";
		public const string TypePrefix = "breakPlanType_";
		public const string OffsetPrefix = "breakPlanOffset_";
		public const string RosterField = "dispatchRoster";
		public const string SupervisorField = "dispatchSupervisor";

		/// <summary>The posted break plan, in row order; rows without a positive length are dropped.</summary>
		public static List<DispatcherBreakPlanEntry> ReadBreakPlan(IFormCollection form)
		{
			var entries = new List<(int Order, DispatcherBreakPlanEntry Entry)>();

			if (form == null)
				return new List<DispatcherBreakPlanEntry>();

			foreach (var key in form.Keys.Where(x => x.StartsWith(MinutesPrefix, StringComparison.OrdinalIgnoreCase)).ToList())
			{
				var suffix = key.Substring(MinutesPrefix.Length);

				if (!int.TryParse(form[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
					continue;

				var type = int.TryParse(form[TypePrefix + suffix], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedType) &&
				           Enum.IsDefined(typeof(DispatcherBreakTypes), parsedType)
					? parsedType
					: (int)DispatcherBreakTypes.Break;

				int? offset = null;
				var offsetText = ((string)form[OffsetPrefix + suffix] ?? "").Trim();

				if (offsetText.Length > 0 && int.TryParse(offsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOffset) && parsedOffset >= 0)
					offset = Math.Min(parsedOffset, MaxTargetOffsetMinutes);

				var order = int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : int.MaxValue;

				entries.Add((order, new DispatcherBreakPlanEntry
				{
					BreakType = type,
					DurationMinutes = Math.Min(minutes, MaxBreakMinutes),
					TargetOffsetMinutes = offset
				}));
			}

			return entries.OrderBy(x => x.Order).Select(x => x.Entry).Take(MaxBreakPlanRows).ToList();
		}

		/// <summary>
		/// The posted standing roster: everyone checked as a dispatcher, with the supervisor flag. Ticking only the supervisor
		/// box puts the person on the roster too.
		/// </summary>
		public static List<(string UserId, bool IsSupervisor)> ReadStandingRoster(IFormCollection form)
		{
			var roster = new List<(string UserId, bool IsSupervisor)>();

			if (form == null)
				return roster;

			var supervisors = new HashSet<string>(Split(form[SupervisorField]), StringComparer.OrdinalIgnoreCase);

			foreach (var userId in Split(form[RosterField]).Concat(supervisors))
			{
				if (roster.Any(x => String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)))
					continue;

				roster.Add((userId, supervisors.Contains(userId)));
			}

			return roster;
		}

		/// <summary>The stored default plan (offset -1 = spread) as editor rows.</summary>
		public static List<DispatcherBreakPlanRow> ToRows(IEnumerable<DispatcherBreakPlanEntry> plan)
		{
			return (plan ?? Enumerable.Empty<DispatcherBreakPlanEntry>()).Where(x => x != null).Select(DispatcherBreakPlanRow.From).ToList();
		}

		public static List<DispatcherDeskBreakPlanEntry> ToConfigPlan(IEnumerable<DispatcherBreakPlanEntry> plan)
		{
			return (plan ?? Enumerable.Empty<DispatcherBreakPlanEntry>()).Where(x => x != null && x.DurationMinutes > 0).Select(x => new DispatcherDeskBreakPlanEntry
			{
				BreakType = x.BreakType,
				DurationMinutes = x.DurationMinutes,
				TargetOffsetMinutes = x.TargetOffsetMinutes ?? -1
			}).ToList();
		}

		private static IEnumerable<string> Split(StringValues values)
		{
			return values
				.SelectMany(x => (x ?? "").Split(','))
				.Select(x => x.Trim())
				.Where(x => x.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase);
		}
	}
}
