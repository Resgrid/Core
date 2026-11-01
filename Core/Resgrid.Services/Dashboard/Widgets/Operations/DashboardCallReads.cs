using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets
{
	/// <summary>
	/// Call reads the operations widgets share, with the Dispatch module's own rules: the active list is the dispatched open
	/// calls (DispatchController.GetActiveCallsList: a scheduled call waiting for its dispatch time is left out), trimmed by
	/// group-scoped dispatch exactly like every dispatcher call list; priority names and colours resolve like
	/// ICallsService.CallPriorityToStringAsync / CallPriorityToColorAsync, from one cached priority read.
	/// </summary>
	public static class DashboardCallReads
	{
		public static string ViewCallUrl(int callId) => "/User/Dispatch/ViewCall?callId=" + callId.ToString(CultureInfo.InvariantCulture);

		/// <summary>True for a call that has gone out (not a scheduled call still waiting for its time).</summary>
		public static bool IsDispatched(Call call, DateTime nowUtc) =>
			!call.DispatchOn.HasValue || call.DispatchOn.Value <= nowUtc || call.HasBeenDispatched == true;

		/// <summary>The viewer's dispatched active calls, in the department's order (scope-filtered).</summary>
		public static async Task<List<Call>> GetScopedActiveCallsAsync(ICallsService callsService, IDispatchScopeService dispatchScopeService,
			int departmentId, string userId, DateTime nowUtc)
		{
			var active = (await callsService.GetActiveCallsByDepartmentAsync(departmentId) ?? new List<Call>())
				.Where(c => c != null && c.DepartmentId == departmentId && !c.IsDeleted && IsDispatched(c, nowUtc))
				.ToList();

			return await ScopeAsync(dispatchScopeService, departmentId, userId, active);
		}

		/// <summary>Group-scoped dispatch over a call list (the same list back when the viewer is department-wide).</summary>
		public static async Task<List<Call>> ScopeAsync(IDispatchScopeService dispatchScopeService, int departmentId, string userId, List<Call> calls)
		{
			if (calls == null || calls.Count == 0)
				return new List<Call>();

			return await dispatchScopeService.FilterCallsForUserAsync(departmentId, userId, calls) ?? new List<Call>();
		}

		/// <summary>Never hands an envelope to a client: names and addresses are operational plaintext; SafeDisplay guards stale rows.</summary>
		public static string Safe(string value) => ProtectedDataEnvelope.SafeDisplay(value);

		public static async Task<Func<int, DashboardCallPriority>> PriorityLookupAsync(ICallsService callsService, int departmentId)
		{
			var priorities = (await callsService.GetCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>())
				.Where(p => p != null)
				.GroupBy(p => p.DepartmentCallPriorityId)
				.ToDictionary(g => g.Key, g => g.First());

			return priority =>
			{
				switch (priority)
				{
					case (int)CallPriority.Low:
						return new DashboardCallPriority { Id = priority, Name = "Low", Color = "#008000" };
					case (int)CallPriority.Medium:
						return new DashboardCallPriority { Id = priority, Name = "Medium", Color = "#DBDB2E" };
					case (int)CallPriority.High:
						return new DashboardCallPriority { Id = priority, Name = "High", Color = "#FFA500" };
					case (int)CallPriority.Emergency:
						return new DashboardCallPriority { Id = priority, Name = "Emergency", Color = "#FF0000" };
					default:
						return priorities.TryGetValue(priority, out var custom)
							? new DashboardCallPriority { Id = priority, Name = custom.Name, Color = DashboardStatusPalette.Hex(custom.Color) ?? "#008000" }
							: new DashboardCallPriority { Id = priority, Name = "Low", Color = "#008000" };
				}
			};
		}

		/// <summary>The department's priority ids (built-in 0-3 when it has none of its own), for re-checking a priority setting.</summary>
		public static async Task<HashSet<int>> PriorityIdsAsync(ICallsService callsService, int departmentId)
		{
			var priorities = await callsService.GetCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
			var ids = new HashSet<int>(priorities.Where(p => p != null && !p.IsDeleted).Select(p => p.DepartmentCallPriorityId));

			if (ids.Count == 0)
			{
				foreach (CallPriority builtIn in Enum.GetValues(typeof(CallPriority)))
					ids.Add((int)builtIn);
			}

			return ids;
		}

		/// <summary>Integer ids from a multi-select setting, ignoring anything that does not parse.</summary>
		public static HashSet<int> ParseIds(IEnumerable<string> values)
		{
			var ids = new HashSet<int>();
			foreach (var value in values ?? Enumerable.Empty<string>())
			{
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
					ids.Add(id);
			}

			return ids;
		}

		public static int? ParseId(string value) =>
			int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null;
	}
}
