using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Helpers;

namespace Resgrid.Model.Reporting
{
	/// <summary>How a call's group was decided, most direct first.</summary>
	public enum CallGroupAssignmentMethods
	{
		/// <summary>No group: the call is outside every boundary and nothing with a group worked it, or the vote tied.</summary>
		None = 0,

		/// <summary>The call's coordinates fall inside the group's geofence.</summary>
		Geofence = 1,

		/// <summary>The group holds the plurality of the units and personnel dispatched to the call.</summary>
		Dispatch = 2,

		/// <summary>Nothing with a group was dispatched; the group holds the plurality of the units and personnel with a status on the call.</summary>
		Response = 3
	}

	/// <summary>The one group a call belongs to for reporting, and how that was decided.</summary>
	public class CallGroupAssignment
	{
		public int CallId { get; set; }

		public int? DepartmentGroupId { get; set; }

		public CallGroupAssignmentMethods Method { get; set; }
	}

	/// <summary>The units, personnel and groups on one side of a call (dispatched to it, or working it).</summary>
	public class CallGroupResources
	{
		public HashSet<int> UnitIds { get; } = new HashSet<int>();

		public HashSet<string> UserIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Groups dispatched as a whole; each counts through its members.</summary>
		public HashSet<int> GroupIds { get; } = new HashSet<int>();

		public bool IsEmpty => UnitIds.Count == 0 && UserIds.Count == 0 && GroupIds.Count == 0;
	}

	/// <summary>
	/// Places each call in one department group, for reports that break calls down by group (station).
	/// <para>
	/// A call located inside a group's geofence belongs to that group. When boundaries overlap the innermost group in the
	/// hierarchy wins, then a station over an organizational group, then the smaller boundary.
	/// </para>
	/// <para>
	/// A call with no usable location, or outside every boundary, belongs to the group holding the plurality of what was
	/// dispatched to it: each unit votes for its station group and each person for their group. A dispatched group counts
	/// through its members, and one with no members votes once for itself; role dispatches do not vote, since a role
	/// cuts across stations. Groups tied on votes are split by unit votes, then by the station nearest the call; a tie
	/// that survives leaves the call unassigned rather than guessing. When nothing dispatched has a group, the units and
	/// personnel that set a status on the call vote the same way.
	/// </para>
	/// <para>Group membership and unit station assignments are today's, not as they were when the call ran.</para>
	/// </summary>
	public sealed class CallGroupAssigner
	{
		private sealed class Boundary
		{
			public int GroupId { get; set; }
			public List<GeoMath.GeoPoint> Polygon { get; set; }
			public int Depth { get; set; }
			public bool IsStation { get; set; }
			public double Area { get; set; }
		}

		private readonly List<Boundary> _boundaries = new List<Boundary>();
		private readonly Dictionary<int, int> _unitGroups = new Dictionary<int, int>();
		private readonly Dictionary<string, List<int>> _userGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<int, List<string>> _groupMembers = new Dictionary<int, List<string>>();
		private readonly Dictionary<int, GeoMath.GeoPoint> _stationPoints = new Dictionary<int, GeoMath.GeoPoint>();

		public CallGroupAssigner(IEnumerable<DepartmentGroup> groups, IEnumerable<Unit> units)
		{
			var groupList = (groups ?? Enumerable.Empty<DepartmentGroup>()).Where(g => g != null)
				.GroupBy(g => g.DepartmentGroupId).Select(g => g.First()).ToList();

			foreach (var group in groupList)
			{
				var polygon = GeoMath.ParseGeofence(group.Geofence);
				if (polygon != null)
				{
					_boundaries.Add(new Boundary
					{
						GroupId = group.DepartmentGroupId,
						Polygon = polygon,
						Depth = DepartmentGroupHierarchy.GetAncestorIds(groupList, group.DepartmentGroupId).Count,
						IsStation = group.Type == (int)DepartmentGroupTypes.Station,
						Area = PlanarArea(polygon)
					});
				}

				var stationPoint = GeoMath.ParseCoordinatePair(group.Latitude, group.Longitude)
					?? (polygon != null ? GeoMath.Centroid(polygon) : (GeoMath.GeoPoint?)null);
				if (stationPoint.HasValue)
					_stationPoints[group.DepartmentGroupId] = stationPoint.Value;

				var members = (group.Members ?? Enumerable.Empty<DepartmentGroupMember>())
					.Where(m => m != null && !string.IsNullOrWhiteSpace(m.UserId))
					.Select(m => m.UserId)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
				_groupMembers[group.DepartmentGroupId] = members;

				foreach (var userId in members)
				{
					if (!_userGroups.TryGetValue(userId, out var userGroups))
						_userGroups[userId] = userGroups = new List<int>();

					userGroups.Add(group.DepartmentGroupId);
				}
			}

			foreach (var unit in (units ?? Enumerable.Empty<Unit>()).Where(u => u != null))
			{
				if (unit.StationGroupId.HasValue && _groupMembers.ContainsKey(unit.StationGroupId.Value))
					_unitGroups[unit.UnitId] = unit.StationGroupId.Value;
			}
		}

		/// <param name="callId">Echoed onto the result.</param>
		/// <param name="geoLocationData">The call's "lat,long" (Call.GeoLocationData); null or unparseable skips the boundary test.</param>
		/// <param name="dispatched">What was dispatched to the call.</param>
		/// <param name="responded">What set a status on the call; only votes when nothing dispatched has a group.</param>
		public CallGroupAssignment Assign(int callId, string geoLocationData, CallGroupResources dispatched, CallGroupResources responded = null)
		{
			var result = new CallGroupAssignment { CallId = callId, Method = CallGroupAssignmentMethods.None };
			var point = GeoMath.ParseLatLonString(geoLocationData);

			if (point.HasValue)
			{
				var containing = _boundaries
					.Where(b => GeoMath.IsPointInPolygon(point.Value.Latitude, point.Value.Longitude, b.Polygon))
					.OrderByDescending(b => b.Depth)
					.ThenByDescending(b => b.IsStation)
					.ThenBy(b => b.Area)
					.ThenBy(b => b.GroupId)
					.FirstOrDefault();

				if (containing != null)
				{
					result.DepartmentGroupId = containing.GroupId;
					result.Method = CallGroupAssignmentMethods.Geofence;
					return result;
				}
			}

			var votes = Tally(dispatched);
			var method = CallGroupAssignmentMethods.Dispatch;

			if (votes.Count == 0)
			{
				votes = Tally(responded);
				method = CallGroupAssignmentMethods.Response;
			}

			var winner = Plurality(votes, point);
			if (winner.HasValue)
			{
				result.DepartmentGroupId = winner;
				result.Method = method;
			}

			return result;
		}

		private Dictionary<int, (int Total, int Units)> Tally(CallGroupResources resources)
		{
			var votes = new Dictionary<int, (int Total, int Units)>();
			if (resources == null || resources.IsEmpty)
				return votes;

			void Vote(int groupId, bool isUnit)
			{
				votes.TryGetValue(groupId, out var current);
				votes[groupId] = (current.Total + 1, current.Units + (isUnit ? 1 : 0));
			}

			foreach (var unitId in resources.UnitIds)
			{
				if (_unitGroups.TryGetValue(unitId, out var groupId))
					Vote(groupId, true);
			}

			var users = new HashSet<string>(resources.UserIds.Where(u => !string.IsNullOrWhiteSpace(u)), StringComparer.OrdinalIgnoreCase);
			foreach (var groupId in resources.GroupIds)
			{
				if (!_groupMembers.TryGetValue(groupId, out var members))
					continue;

				if (members.Count == 0)
					Vote(groupId, false);
				else
					users.UnionWith(members);
			}

			foreach (var userId in users)
			{
				if (!_userGroups.TryGetValue(userId, out var userGroups))
					continue;

				foreach (var groupId in userGroups)
					Vote(groupId, false);
			}

			return votes;
		}

		private int? Plurality(Dictionary<int, (int Total, int Units)> votes, GeoMath.GeoPoint? point)
		{
			if (votes.Count == 0)
				return null;

			var topTotal = votes.Values.Max(v => v.Total);
			var tied = votes.Where(v => v.Value.Total == topTotal).ToList();

			if (tied.Count > 1)
			{
				var topUnits = tied.Max(v => v.Value.Units);
				tied = tied.Where(v => v.Value.Units == topUnits).ToList();
			}

			if (tied.Count == 1)
				return tied[0].Key;

			// Nearest station breaks what is left, but only when every tied group has a location to measure from.
			if (point.HasValue && tied.All(v => _stationPoints.ContainsKey(v.Key)))
			{
				var byDistance = tied
					.Select(v => (GroupId: v.Key, Meters: GeoMath.HaversineMeters(point.Value.Latitude, point.Value.Longitude,
						_stationPoints[v.Key].Latitude, _stationPoints[v.Key].Longitude)))
					.OrderBy(v => v.Meters)
					.ToList();

				if (byDistance[0].Meters < byDistance[1].Meters)
					return byDistance[0].GroupId;
			}

			return null;
		}

		/// <summary>
		/// Shoelace area in square degrees. Only compares overlapping boundaries, which sit at the same latitude, so the
		/// longitude scale distortion cancels out.
		/// </summary>
		private static double PlanarArea(IReadOnlyList<GeoMath.GeoPoint> polygon)
		{
			double sum = 0;
			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
				sum += (polygon[j].Longitude * polygon[i].Latitude) - (polygon[i].Longitude * polygon[j].Latitude);

			return Math.Abs(sum) / 2d;
		}
	}
}
