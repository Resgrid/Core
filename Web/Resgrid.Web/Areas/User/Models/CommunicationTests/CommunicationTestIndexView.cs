using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CommunicationTests
{
	public class CommunicationTestIndexView : BaseUserModel
	{
		public List<CommunicationTest> Tests { get; set; } = new List<CommunicationTest>();
		public List<CommunicationTestRun> RecentRuns { get; set; } = new List<CommunicationTestRun>();
		public Dictionary<string, string> TestNames { get; set; } = new Dictionary<string, string>();

		/// <summary>
		/// Human readable targeting summary per test id. A missing or empty entry means the test
		/// covers the whole department.
		/// </summary>
		public Dictionary<string, string> TestScopes { get; set; } = new Dictionary<string, string>();
		public bool IsDepartmentAdmin { get; set; }
	}
}
