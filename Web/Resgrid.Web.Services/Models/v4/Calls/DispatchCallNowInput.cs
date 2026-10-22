using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Dispatches a pending call, or a scheduled call that has not gone out yet, right now
	/// </summary>
	public class DispatchCallNowInput
	{
		/// <summary>
		/// Id of the call to dispatch
		/// </summary>
		[Required]
		public string CallId { get; set; }

		/// <summary>
		/// Optional. Pipe separated personnel ("P:"), groups ("G:"), roles ("R:") and units ("U:") to send the call to,
		/// replacing the call's current dispatch list; "0" sends it to everyone. Leave empty to use the dispatch list
		/// already on the call.
		/// </summary>
		public string DispatchList { get; set; }

		/// <summary>
		/// Optional (conditional run cards). Pipe separated per-unit response codes, "unitId:codeId", from the department's
		/// response code catalog; "unitId:" clears a unit's code. Units not listed keep the code the run card proposed.
		/// </summary>
		public string UnitResponseCodes { get; set; }
	}
}
