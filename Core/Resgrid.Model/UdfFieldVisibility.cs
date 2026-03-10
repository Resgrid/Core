namespace Resgrid.Model
{
	/// <summary>
	/// Controls which roles can see and interact with a UDF field.
	/// </summary>
	public enum UdfFieldVisibility
	{
		/// <summary>All authenticated users can see and fill the field.</summary>
		Everyone = 0,

		/// <summary>Only department admins and group admins can see and fill the field.</summary>
		DepartmentAndGroupAdmins = 1,

		/// <summary>Only department admins can see and fill the field.</summary>
		DepartmentAdminsOnly = 2
	}
}

