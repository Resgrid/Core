namespace Resgrid.Web.Services.Models.v4.CallProtocols
{
	/// <summary>CallProtocols/RemoveCallProtocol: takes a protocol off a call with a reason; the row stays, marked removed.</summary>
	public class RemoveCallProtocolInput
	{
		public int CallId { get; set; }

		/// <summary>The call's protocol row (CallProtocolId from Calls/GetCallExtraData).</summary>
		public int CallProtocolId { get; set; }

		/// <summary>Why it was removed (required, up to 500 characters).</summary>
		public string Reason { get; set; }
	}

	public class RemoveCallProtocolResult : StandardApiResponseV4Base
	{
		/// <summary>The removed row's id.</summary>
		public string Id { get; set; }
	}
}
