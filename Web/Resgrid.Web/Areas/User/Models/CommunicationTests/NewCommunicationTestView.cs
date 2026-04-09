using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CommunicationTests
{
	public class NewCommunicationTestView : BaseUserModel
	{
		public CommunicationTest Test { get; set; } = new CommunicationTest();
		public string Message { get; set; }
	}
}
