using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.AdminAssist
{
	public enum DispatchRouteKind { Direct, Group, Shift, UnitCrew, UnitGroup, Role }
	public sealed record DispatchRoute(DispatchRouteKind Kind, string SourceId, IReadOnlyList<string> Members,
		bool UseResolvedShift = false, IReadOnlyList<string> OnDutyMembers = null);
	public sealed record DispatchSelection(string UserId, DispatchRouteKind Kind, string SourceId, bool Selected, bool EmptyShiftFallback);
	public sealed record DispatchResolution(DateTime AsOfUtc, IReadOnlyList<DispatchSelection> Decisions,
		IReadOnlyList<string> SelectedUserIds);

	/// <summary>
	/// Pure routing, used incrementally by the broadcaster and over isolated snapshots by previews.
	/// Channel eligibility remains the communication service's responsibility. Inputs must already be
	/// department scoped and filtered by the call's dispatch-scope decision. No I/O, wall clock or mutation.
	/// </summary>
	public static class DispatchRecipientResolver
	{
		public const string Version = "1";
		public static DispatchResolution Resolve(DateTime asOfUtc, IEnumerable<DispatchRoute> routes,
			IEnumerable<string> previouslySelected = null)
		{
			if (asOfUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("An explicit UTC simulation time is required.", nameof(asOfUtc));
			var prior = previouslySelected as IReadOnlySet<string> ?? new HashSet<string>(previouslySelected ?? Array.Empty<string>(), StringComparer.Ordinal);
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var decisions = new List<DispatchSelection>();
			var selected = new List<string>();
			foreach (var route in routes)
			{
				if (route.Members == null || route.UseResolvedShift && route.OnDutyMembers == null)
					throw new ArgumentException("Missing routing evidence must not be treated as an empty roster.");
				var shift = route.Kind == DispatchRouteKind.Group && route.UseResolvedShift && route.OnDutyMembers.Count > 0;
				var fallback = route.Kind == DispatchRouteKind.Group && route.UseResolvedShift && !shift;
				foreach (var userId in shift ? route.OnDutyMembers : route.Members)
				{
					// Direct dispatch rows historically remain separate attempts, including duplicates. Expanded
					// routes deduplicate against every earlier route even when that earlier send failed.
					var added = !prior.Contains(userId) && seen.Add(userId);
					var include = route.Kind == DispatchRouteKind.Direct || added;
					decisions.Add(new DispatchSelection(userId, shift ? DispatchRouteKind.Shift : route.Kind, route.SourceId, include, fallback));
					if (include) selected.Add(userId);
				}
			}
			return new DispatchResolution(asOfUtc, decisions.AsReadOnly(), selected.AsReadOnly());
		}
	}
}
