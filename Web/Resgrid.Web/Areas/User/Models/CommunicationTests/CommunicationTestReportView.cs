using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CommunicationTests
{
	public class CommunicationTestReportView : BaseUserModel
	{
		public CommunicationTestRun Run { get; set; }
		public CommunicationTest Test { get; set; }
		public List<CommunicationTestResult> Results { get; set; } = new List<CommunicationTestResult>();
		public Dictionary<string, UserProfile> Profiles { get; set; } = new Dictionary<string, UserProfile>();
	}
}
