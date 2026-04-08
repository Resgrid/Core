using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CommunicationTests;

/// <summary>
/// Result of getting all communication tests for a department
/// </summary>
public class GetCommunicationTestsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// List of communication test definitions
	/// </summary>
	public List<CommunicationTestData> Data { get; set; } = new List<CommunicationTestData>();
}

/// <summary>
/// Communication test definition data
/// </summary>
public class CommunicationTestData
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Description { get; set; }
	public int ScheduleType { get; set; }
	public bool Sunday { get; set; }
	public bool Monday { get; set; }
	public bool Tuesday { get; set; }
	public bool Wednesday { get; set; }
	public bool Thursday { get; set; }
	public bool Friday { get; set; }
	public bool Saturday { get; set; }
	public int? DayOfMonth { get; set; }
	public string Time { get; set; }
	public bool TestSms { get; set; }
	public bool TestEmail { get; set; }
	public bool TestVoice { get; set; }
	public bool TestPush { get; set; }
	public bool Active { get; set; }
	public int ResponseWindowMinutes { get; set; }
	public string CreatedOn { get; set; }
}

/// <summary>
/// Result of getting a single communication test
/// </summary>
public class GetCommunicationTestResult : StandardApiResponseV4Base
{
	public CommunicationTestData Data { get; set; }
}
