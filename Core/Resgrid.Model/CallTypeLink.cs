using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Links a call to the department's call type it was logged with (<see cref="Call.CallTypeId"/>). Calls store the type's
	/// name (<see cref="Call.Type"/>), which every intake path sets; the id is worked out from it so a later rename of the
	/// type doesn't cut the call off from what is keyed on the type.
	/// </summary>
	public static class CallTypeLink
	{
		/// <summary>
		/// The call type id for a call: the department's type with the call's type name (case-insensitive). A call already
		/// linked to a type that has since been renamed keeps its link while its stored name matches no other type.
		/// </summary>
		public static int? Resolve(string typeName, int? currentCallTypeId, IEnumerable<CallType> departmentTypes)
		{
			if (String.IsNullOrWhiteSpace(typeName))
				return null;

			var types = (departmentTypes ?? Enumerable.Empty<CallType>()).Where(x => x != null).ToList();
			var current = currentCallTypeId.HasValue ? types.FirstOrDefault(x => x.CallTypeId == currentCallTypeId.Value) : null;

			if (current != null && SameName(current.Type, typeName))
				return current.CallTypeId;

			var byName = FindByName(types, typeName);
			if (byName != null)
				return byName.CallTypeId;

			// The type the call was linked to was renamed (its stored name is the old one): keep the link.
			return current?.CallTypeId;
		}

		/// <summary>The department's call type for a call: by its link, else by its type name.</summary>
		public static CallType Find(Call call, IEnumerable<CallType> departmentTypes)
		{
			if (call == null)
				return null;

			var types = (departmentTypes ?? Enumerable.Empty<CallType>()).Where(x => x != null).ToList();

			if (call.CallTypeId.HasValue)
			{
				var linked = types.FirstOrDefault(x => x.CallTypeId == call.CallTypeId.Value);
				if (linked != null)
					return linked;
			}

			return FindByName(types, call.Type);
		}

		public static CallType FindByName(IEnumerable<CallType> types, string typeName)
		{
			if (String.IsNullOrWhiteSpace(typeName))
				return null;

			return (types ?? Enumerable.Empty<CallType>())
				.Where(x => x != null && SameName(x.Type, typeName))
				.OrderBy(x => x.CallTypeId)
				.FirstOrDefault();
		}

		public static bool SameName(string a, string b) =>
			String.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
