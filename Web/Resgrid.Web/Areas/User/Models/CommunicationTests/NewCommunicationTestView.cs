using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CommunicationTests
{
	public class NewCommunicationTestView : BaseUserModel
	{
		public CommunicationTest Test { get; set; } = new CommunicationTest();
		public string Message { get; set; }

		public List<int> SelectedGroupIds { get; set; } = new List<int>();
		public List<int> SelectedRoleIds { get; set; } = new List<int>();
		public List<string> SelectedUserIds { get; set; } = new List<string>();

		public CommunicationTestTargetOptions TargetOptions { get; set; } = new CommunicationTestTargetOptions();

		public CommunicationTestPreview Preview { get; set; } = new CommunicationTestPreview();
	}
}
