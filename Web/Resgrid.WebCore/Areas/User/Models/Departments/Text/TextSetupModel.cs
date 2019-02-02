namespace Resgrid.Web.Areas.User.Models.Departments.Text
{
	public class TextSetupModel
	{
		public string Message { get; set; }
		public bool EnableTextToCall { get; set; }
		public string DepartmentTextToCallNumber { get; set; }
		public bool CanProvisionNumber { get; set; }
		public int TextCallType { get; set; }
		public string DepartmentTextToCallSourceNumbers { get; set; }
		public bool EnableTextCommand { get; set; }
	}
}