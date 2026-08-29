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

	/// <summary>
	/// Whether the member had this channel switched on in their own notification settings when the
	/// run was built. Null for runs built before the election was recorded — read the member's
	/// current profile there rather than treating null as off.
	/// </summary>
	public bool? ChannelEnabled { get; set; }

	/// <summary>
	/// The member's staffing level when the run was built, or null when they had never set one.
	/// </summary>
	public int? StaffingLevel { get; set; }

	/// <summary>
	/// Display name for <see cref="StaffingLevel"/> as the department had it configured at run time,
	/// or the raw level when it is no longer configured. Empty when no level was recorded.
	/// </summary>
	public string StaffingLevelText { get; set; }

	/// <summary>
	/// Whether the department's Suppress (Mute) Staffing Levels setting muted this member for this
	/// run. A suppressed result was deliberately never sent, so it is not a delivery failure.
	/// </summary>
	public bool Suppressed { get; set; }

	public bool SendAttempted { get; set; }
	public bool SendSucceeded { get; set; }
	public string SentOn { get; set; }
	public bool Responded { get; set; }
	public string RespondedOn { get; set; }
}
