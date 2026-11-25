using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	/// <summary>Protocols P4: one call protocol's case on View Call (_CallProtocolCase).</summary>
	public class CallProtocolCaseView
	{
		public int CallId { get; set; }
		public CallProtocol Row { get; set; }

		/// <summary>The member may edit the call: override the determinant and text the caller.</summary>
		public bool CanEdit { get; set; }
	}
}
