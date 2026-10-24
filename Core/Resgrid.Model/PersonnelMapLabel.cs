using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Model
{
	/// <summary>
	/// The name a person's map pin carries. A member holding an active role on a unit (a <see cref="UnitActiveRole"/>
	/// row) is labelled with that unit, "Shawn Jackson (Engine 1)". A Not Occupied seat has no row, so it labels no one,
	/// and a seat on a unit that is not in <c>units</c> (deleted, another department) or that the viewer may not see is ignored.
	/// </summary>
	public static class PersonnelMapLabel
	{
		/// <summary>
		/// The unit names each seated user rides on, keyed by user id; two units are joined in name order. Only units
		/// <paramref name="canViewUnit"/> allows are named, and it is asked once per seated unit.
		/// </summary>
		public static async Task<Dictionary<string, string>> BuildUnitNamesByUserAsync(IEnumerable<UnitActiveRole> activeRoles,
			IEnumerable<Unit> units, Func<int, Task<bool>> canViewUnit)
		{
			var seats = (activeRoles ?? Enumerable.Empty<UnitActiveRole>())
				.Where(r => r != null && !String.IsNullOrWhiteSpace(r.UserId))
				.ToList();
			var seatedUnitIds = seats.Select(r => r.UnitId).ToHashSet();

			var unitNames = new Dictionary<int, string>();
			foreach (var unit in (units ?? Enumerable.Empty<Unit>()).Where(u => u != null && seatedUnitIds.Contains(u.UnitId)))
			{
				if (unit.IsDeleted || String.IsNullOrWhiteSpace(unit.Name) || unitNames.ContainsKey(unit.UnitId))
					continue;

				if (await canViewUnit(unit.UnitId))
					unitNames[unit.UnitId] = unit.Name.Trim();
			}

			return seats
				.Where(r => unitNames.ContainsKey(r.UnitId))
				.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key,
					g => String.Join(", ", g.Select(r => unitNames[r.UnitId]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)),
					StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>The pin title: the name, with the unit in parentheses when the user is seated on one.</summary>
		public static string Format(string name, string userId, IReadOnlyDictionary<string, string> unitNamesByUser)
		{
			if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(userId) || unitNamesByUser == null ||
				!unitNamesByUser.TryGetValue(userId, out var unitName) || String.IsNullOrWhiteSpace(unitName))
				return name;

			return $"{name} ({unitName})";
		}
	}
}
