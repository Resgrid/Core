namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Gets the calls current active, been dispatched and not closed or deleted
	/// </summary>
	public class GetCallResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CallResultData Data { get; set; }
	}
}
