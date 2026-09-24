using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// Pure walks over the DepartmentGroup parent/child tree (ParentDepartmentGroupId). Works on
	/// a flat list of a department's groups so callers can use the cached group list instead of
	/// loading children group by group. Every walk tracks visited ids, so a corrupt parent cycle
	/// terminates instead of spinning.
	/// </summary>
	public static class DepartmentGroupHierarchy
	{
		/// <summary>The root group plus every group beneath it, at any depth. Empty when the root isn't in the list.</summary>
		public static HashSet<int> GetSelfAndDescendantIds(IEnumerable<DepartmentGroup> groups, int rootGroupId)
		{
			var result = new HashSet<int>();

			if (groups == null)
				return result;

			var all = groups.Where(g => g != null).ToList();

			if (all.All(g => g.DepartmentGroupId != rootGroupId))
				return result;

			var childrenByParent = all
				.Where(g => g.ParentDepartmentGroupId.HasValue)
				.GroupBy(g => g.ParentDepartmentGroupId.Value)
				.ToDictionary(g => g.Key, g => g.Select(c => c.DepartmentGroupId).ToList());

			var pending = new Queue<int>();
			pending.Enqueue(rootGroupId);

			while (pending.Count > 0)
			{
				var current = pending.Dequeue();

				if (!result.Add(current))
					continue;

				if (childrenByParent.TryGetValue(current, out var children))
				{
					foreach (var child in children)
						pending.Enqueue(child);
				}
			}

			return result;
		}

		/// <summary>Parent, grandparent, ... of the group, nearest first. Excludes the group itself.</summary>
		public static List<int> GetAncestorIds(IEnumerable<DepartmentGroup> groups, int groupId)
		{
			var ancestors = new List<int>();

			if (groups == null)
				return ancestors;

			var byId = groups.Where(g => g != null)
				.GroupBy(g => g.DepartmentGroupId)
				.ToDictionary(g => g.Key, g => g.First());

			var visited = new HashSet<int> { groupId };
			var current = byId.TryGetValue(groupId, out var start) ? start : null;

			while (current?.ParentDepartmentGroupId != null)
			{
				var parentId = current.ParentDepartmentGroupId.Value;

				if (!visited.Add(parentId))
					break;

				ancestors.Add(parentId);
				current = byId.TryGetValue(parentId, out var parent) ? parent : null;
			}

			return ancestors;
		}

		/// <summary>
		/// Whether <paramref name="parentGroupId"/> can become the parent of <paramref name="groupId"/>:
		/// it must exist in the list (so, the same department), must not be the group itself and must
		/// not sit beneath the group, which would close a cycle.
		/// </summary>
		public static bool IsValidParent(IEnumerable<DepartmentGroup> groups, int groupId, int parentGroupId)
		{
			if (groups == null || groupId == parentGroupId)
				return false;

			var all = groups.Where(g => g != null).ToList();

			if (all.All(g => g.DepartmentGroupId != parentGroupId))
				return false;

			return !GetSelfAndDescendantIds(all, groupId).Contains(parentGroupId);
		}
	}
}
