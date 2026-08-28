namespace Resgrid.Model
{
	/// <summary>
	/// Outbound notification channel for protected-data egress decisions (ADP plan section 9). Each
	/// maps to its DepartmentProtectedDataEgressPolicies mode column; ChatPlatform (Discord, Slack,
	/// Telegram, ...) is third-party egress with no policy column of its own and is always generic
	/// for a protected department.
	/// </summary>
	public enum ProtectedDataEgressChannel
	{
		Push = 1,
		Sms = 2,
		Email = 3,
		Voice = 4,
		ChatPlatform = 5
	}
}
