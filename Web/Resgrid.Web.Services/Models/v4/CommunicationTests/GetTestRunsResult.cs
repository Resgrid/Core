using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CommunicationTests;

/// <summary>
/// Result of getting test runs
/// </summary>
public class GetTestRunsResult : StandardApiResponseV4Base
{
	public List<TestRunData> Data { get; set; } = new List<TestRunData>();
}

/// <summary>
/// Test run summary data
/// </summary>
public class TestRunData
{
	public string Id { get; set; }
	public string CommunicationTestId { get; set; }
	public string StartedOn { get; set; }
	public string CompletedOn { get; set; }
	public int Status { get; set; }
	public string RunCode { get; set; }
	public int TotalUsersTested { get; set; }
	public int TotalResponses { get; set; }
}
