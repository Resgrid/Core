using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Records
{
	public sealed class RecordSubmissionView
	{
		public RmsSubmission Submission { get; set; }
		public List<RmsSubmissionExchange> Exchanges { get; set; } = new();
		public bool IsAdministrator { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}
}
