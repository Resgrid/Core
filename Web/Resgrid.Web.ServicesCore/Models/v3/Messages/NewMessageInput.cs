using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Messages
{
	/// <summary>
	/// Creates a new message to send to other users in the system
	/// </summary>
	public class NewMessageInput
	{
		/// <summary>
		/// The title/subject of the message
		/// </summary>
		public string Ttl { get; set; }

		/// <summary>
		/// The body/content of the message
		/// </summary>
		public string Bdy { get; set; }

		/// <summary>
		/// Type type of the message (0 = Normal\Message, 1 = Callback, 2 = Poll)
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// Who to send the message to
		/// </summary>
		public List<RecipientResult> Rcps { get; set; }
	}
}
