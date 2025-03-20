namespace Resgrid.Web.Services.Models
{
	public class DepartmentCreationResult
	{
		public bool Successful { get; set; }
	}

	public class EmailCheckResult
	{
		public bool EmailInUse { get; set; }
	}

	public class DepartmentCheckResult
	{
		public bool DepartmentNameInUse { get; set; }
	}

	public class UsernameCheckResult
	{
		public bool UserNameInUse { get; set; }
	}
}
