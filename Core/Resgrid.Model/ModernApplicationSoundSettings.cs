namespace Resgrid.Model
{
	/// <summary>
	/// Resolves the effective modern application sound setting shared by notification delivery
	/// and API clients.
	/// </summary>
	public static class ModernApplicationSoundSettings
	{
		public static bool IsEnabled(bool departmentEnabled, bool userEnabled)
		{
			return departmentEnabled || userEnabled;
		}
	}
}
