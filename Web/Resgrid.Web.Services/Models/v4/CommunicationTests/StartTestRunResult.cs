namespace Resgrid.Web.Services.Models.v4.CommunicationTests;

/// <summary>
/// Result of starting a communication test run
/// </summary>
public class StartTestRunResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Id of the new test run
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// The run code used for SMS responses
	/// </summary>
	public string RunCode { get; set; }
}
