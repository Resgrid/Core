using System.Collections.Generic;
using Resgrid.Model.Security;

namespace Resgrid.Web.Areas.User.Models.Security
{
	public class ActiveSessionsView
	{
		public string CurrentSessionId { get; set; }
		public IReadOnlyList<UserSessionSummary> Sessions { get; set; }
	}
}
