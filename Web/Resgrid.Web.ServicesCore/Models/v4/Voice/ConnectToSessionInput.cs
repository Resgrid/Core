namespace Resgrid.Web.Services.Models.v4.Voice
{
	/// <summary>
	/// Connects to a voip session
	/// </summary>
	public class ConnectToSessionInput
	{
		/// <summary>
		/// Session id to connect to
		/// </summary>
		public string SessionId { get; set; }

		/// <summary>
		/// Name of the person or unit connecting
		/// </summary>
		public string Name { get; set; }
	}
}
