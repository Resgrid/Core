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
	public int VerificationStatus { get; set; }
	public bool SendAttempted { get; set; }
	public bool SendSucceeded { get; set; }
	public string SentOn { get; set; }
	public bool Responded { get; set; }
	public string RespondedOn { get; set; }
}
