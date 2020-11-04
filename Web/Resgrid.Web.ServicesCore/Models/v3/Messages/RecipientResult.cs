namespace Resgrid.Web.Services.Controllers.Version3.Models.Messages
{
	/// <summary>
	/// The result of getting all recipients for the system
	/// </summary>
	public class RecipientResult
	{
		/// <summary>
		/// The Id value of the recipient, it will be a Guid value for Users and an Int for all others
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// The type of the Recipient
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// The recipient's display name
		/// </summary>
		public string Nme { get; set; }
	}
}
