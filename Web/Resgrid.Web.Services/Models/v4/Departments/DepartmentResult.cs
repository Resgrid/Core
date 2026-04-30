namespace Resgrid.Web.Services.Models.v4.Departments
{
	/// <summary>
	/// Result of a department lookup by dispatch email code.
	/// </summary>
	public class DepartmentResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public DepartmentResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public DepartmentResult()
		{
			Data = new DepartmentResultData();
		}
	}

	/// <summary>
	/// The core department information returned by the lookup.
	/// </summary>
	public class DepartmentResultData
	{
		/// <summary>
		/// Id of the department
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// Name of the department
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Department code (short identifier)
		/// </summary>
		public string Code { get; set; }
	}
}
