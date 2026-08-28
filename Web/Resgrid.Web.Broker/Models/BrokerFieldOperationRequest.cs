using System.Collections.Generic;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Broker.Models
{
	/// <summary>
	/// One broker field-crypto request (decrypt or encrypt). The department here is CHECKED against
	/// the grant's dept claim and each envelope's AAD — a conflicting value fails, it never selects
	/// a tenant (plan section 2.2). RequestId is single-use per department (replay control).
	/// </summary>
	public class BrokerFieldOperationRequest
	{
		public int DepartmentId { get; set; }

		/// <summary>The caller's Protected Data Grant token (compact JWS).</summary>
		public string GrantToken { get; set; }

		/// <summary>Caller-generated unique id for this request; replays are refused.</summary>
		public string RequestId { get; set; }

		public List<ProtectedFieldOperationItem> Items { get; set; }
	}
}
