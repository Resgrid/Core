namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// The result/return for closing a call
	/// </summary>
	public class CloseCallResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Id of the call closed
		/// </summary>
		public string Id { get; set; }
	}
}
