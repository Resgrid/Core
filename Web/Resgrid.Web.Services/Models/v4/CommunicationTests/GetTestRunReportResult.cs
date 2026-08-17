using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CommunicationTests;

/// <summary>
/// Result of getting a test run report
/// </summary>
public class GetTestRunReportResult : StandardApiResponseV4Base
{
	public List<CommunicationTestResultData> Data { get; set; } = new List<CommunicationTestResultData>();
}

/// <summary>
/// Individual test result data for report
/// </summary>
public class CommunicationTestResultData
{
	public string Id { get; set; }
	public string UserId { get; set; }
	public string UserName { get; set; }

	/// <summary>
	/// Channel type (0=Sms, 1=Email, 2=Voice, 3=Push)
	/// </summary>
	public int Channel { get; set; }

	public string ContactValue { get; set; }
	public string ContactCarrier { get; set; }

	/// <summary>
	/// Contact verification state of this channel (0=Grandfathered, 1=Pending/Unverified, 2=Verified).
	/// Always 2 for push, which has no verifiable contact method — read VerificationStatusText
	/// instead of interpreting this value for the push channel.
	/// </summary>
	public int VerificationStatus { get; set; }

	/// <summary>
	/// Display label for <see cref="VerificationStatus"/>: "Verified", "Unverified",
	/// "Grandfathered", or "N/A" for push. Supplied so every client shows the same wording.
	/// </summary>
	public string VerificationStatusText { get; set; }
	public bool SendAttempted { get; set; }
	public bool SendSucceeded { get; set; }
	public string SentOn { get; set; }
	public bool Responded { get; set; }
	public string RespondedOn { get; set; }
}
