namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// Input to respond to a message
/// </summary>
public class RespondToMessageInput
{
	/// <summary>
	/// Id of the message
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// Type of response (1 = Yes, 2 = Maybe, 3 = No)
	/// </summary>
	public int Type { get; set; }

	/// <summary>
	/// Note for the response
	/// </summary>
	public string Note { get; set; }
}
