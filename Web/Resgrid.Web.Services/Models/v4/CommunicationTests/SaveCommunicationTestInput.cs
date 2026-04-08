namespace Resgrid.Web.Services.Models.v4.CommunicationTests;

/// <summary>
/// Input model for creating or updating a communication test
/// </summary>
public class SaveCommunicationTestInput
{
	/// <summary>
	/// The Id of the communication test (empty or null for new tests)
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// Name of the communication test
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Description of the communication test
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Schedule type (0=OnDemand, 1=Weekly, 2=Monthly)
	/// </summary>
	public int ScheduleType { get; set; }

	public bool Sunday { get; set; }
	public bool Monday { get; set; }
	public bool Tuesday { get; set; }
	public bool Wednesday { get; set; }
	public bool Thursday { get; set; }
	public bool Friday { get; set; }
	public bool Saturday { get; set; }

	/// <summary>
	/// Day of month for monthly schedule (1-28)
	/// </summary>
	public int? DayOfMonth { get; set; }

	/// <summary>
	/// Time of day for scheduled tests (e.g. "09:00 AM")
	/// </summary>
	public string Time { get; set; }

	public bool TestSms { get; set; }
	public bool TestEmail { get; set; }
	public bool TestVoice { get; set; }
	public bool TestPush { get; set; }

	public bool Active { get; set; }

	/// <summary>
	/// Response window in minutes (default 60)
	/// </summary>
	public int ResponseWindowMinutes { get; set; }
}
