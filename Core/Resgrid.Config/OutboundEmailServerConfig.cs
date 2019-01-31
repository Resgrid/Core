namespace Resgrid.Config
{
	public static class OutboundEmailServerConfig
	{
		public static string FromMail = "do-not-reply@yourcompany.local";
		public static string ToMail = "administrator@yourcompany.local";
		public static string Host = "mail.yourcompany.local";
		public static bool EnableSsl = false;
		public static int Port = 25;
		public static string UserName = "";
		public static string Password = "";

		public static string PostmarkApiKey = "";

		public static string AwsAccessKey = "";
		public static string AwsSecretKey = "";
	}
}
