namespace Resgrid.Model
{
	/// <summary>
	/// The kinds of data one side of a <see cref="DepartmentLink"/> can share with the other. Each kind has a flag per
	/// direction on the link (DepartmentShare* for the creating department, LinkedDepartmentShare* for the linked one).
	/// </summary>
	public enum DepartmentLinkShareTypes
	{
		Calls = 0,
		Units = 1,
		Personnel = 2,
		Orders = 3,

		/// <summary>
		/// Protocols P5: the side's published protocols, read-only (the version in force only), and its protocol sets a linked
		/// department may subscribe to.
		/// </summary>
		Protocols = 4,

		/// <summary>Training T5: the side's course sets a linked department may subscribe to, and LMS sponsorship over the link.</summary>
		Training = 5
	}
}
