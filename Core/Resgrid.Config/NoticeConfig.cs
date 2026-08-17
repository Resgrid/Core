namespace Resgrid.Config
{
	/// <summary>
	/// Class NoticeConfig.
	/// </summary>
	public static class NoticeConfig
	{
		/// <summary>
		/// The login page notice
		/// </summary>
		public static string LoginPageNotice = "";

		/// <summary>
		/// The login page notice to display, preferring <see cref="LoginPageNotice"/> and falling back to the
		/// legacy <see cref="SystemBehaviorConfig.LoginPageNotice"/> key so existing on-prem configs keep working.
		/// </summary>
		public static string EffectiveLoginPageNotice =>
			!string.IsNullOrWhiteSpace(LoginPageNotice)
				? LoginPageNotice
				: SystemBehaviorConfig.LoginPageNotice;

		/// <summary>
		/// The dashboard toast notice
		/// </summary>
		public static string DashboardToastNotice = "";

		/// <summary>
		/// The email notice
		/// </summary>
		public static string EmailNotice = "";

		/// <summary>
		/// The push text prefix
		/// </summary>
		public static string PushTextPrefix = "";
	}
}
