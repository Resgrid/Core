namespace Resgrid.Web.Services.Models.v4.CommunicationTests;

/// <summary>
/// Result of saving a communication test
/// </summary>
public class SaveCommunicationTestResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Id of the saved communication test
	/// </summary>
	public string Id { get; set; }
}
