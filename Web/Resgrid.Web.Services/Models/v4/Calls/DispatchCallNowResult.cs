namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Result of dispatching a waiting call
	/// </summary>
	public class DispatchCallNowResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Id of the dispatched call
		/// </summary>
		public string Id { get; set; }
	}
}
