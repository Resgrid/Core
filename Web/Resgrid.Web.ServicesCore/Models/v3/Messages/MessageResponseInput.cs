namespace Resgrid.Web.Services.Controllers.Version3.Models.Messages
{
	/// <summary>
	/// Responding to a message that is a Poll or Callback
	/// </summary>
	public class MessageResponseInput
	{
		/// <summary>
		/// Id of the message
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Type of response (1 = Yes, 2 = Maybe, 3 = No)
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// Note for the response
		/// </summary>
		public string Not { get; set; }
	}
}