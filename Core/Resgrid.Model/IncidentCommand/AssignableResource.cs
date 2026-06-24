namespace Resgrid.Model
{
	/// <summary>
	/// A resource (unit or person) that the Commander can assign to a lane — sourced from the own department
	/// or a linked (mutual-aid) department. Color-coded per the mutual-aid link.
	/// </summary>
	public class AssignableResource
	{
		/// <summary>Maps to <see cref="ResourceAssignmentKind"/> (RealUnit/RealPersonnel/LinkedDeptUnit/LinkedDeptPersonnel).</summary>
		public int ResourceKind { get; set; }

		/// <summary>Unit id (as string) or personnel user id.</summary>
		public string ResourceId { get; set; }

		/// <summary>Display name of the unit or person.</summary>
		public string Name { get; set; }

		/// <summary>The department this resource belongs to.</summary>
		public int DepartmentId { get; set; }

		/// <summary>True when the resource comes from a linked (mutual-aid) department.</summary>
		public bool IsMutualAid { get; set; }

		/// <summary>Map/marker color from the mutual-aid link (null for own-department resources).</summary>
		public string Color { get; set; }
	}
}
