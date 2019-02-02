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
		
		public static long PostmarkCallEmailTemplateId = 0;
		public static long PostmarkTroubleAlertTemplateId = 0;
		public static long PostmarkCancelRecieptTemplateId = 0;
		public static long PostmarkChargeFailedTemplateId = 0;
		public static long PostmarkInviteTemplateId = 0;
		public static long PostmarkMessageTemplateId = 0;
		public static long PostmarkResetPasswordTemplateId = 0;
		public static long PostmarkRecieptTemplateId = 0;
		public static long PostmarkWelcomeTemplateId = 0;
		public static long PostmarkNewDepLinkTemplateId = 0;
	}
}
