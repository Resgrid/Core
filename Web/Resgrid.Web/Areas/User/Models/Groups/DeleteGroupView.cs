using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Groups
{
	public class DeleteGroupView : BaseUserModel
	{
		public string Message { get; set; }
		public DepartmentGroup Group { get; set; }
		public int UserCount { get; set; }
		public int ChildGroupCount { get; set; }
		public int UnitsCount { get; set; }
		public int ShiftsCount { get; set; }
		public bool AreYouSure { get; set; }
	}
}
