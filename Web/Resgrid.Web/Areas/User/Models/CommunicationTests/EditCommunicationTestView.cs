using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CommunicationTests
{
	public class EditCommunicationTestView : BaseUserModel
	{
		public CommunicationTest Test { get; set; }
		public string Message { get; set; }
	}
}
