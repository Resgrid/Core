using System.Collections.Generic;

namespace Resgrid.Config
{
	/// <summary>
	/// Security Configuration and Settings
	/// </summary>
	public static class SecurityConfig
	{
		/// <summary>
		/// System level credentials user-name as key, password as value, for system level logins, not department api level
		/// which are configured in the application itself
		/// </summary>
		public static Dictionary<string, string> SystemLoginCredentials = new Dictionary<string, string>()
		{
			
		};
	}
}
