namespace Resgrid.Config
{
	public static class OutboundEmailServerConfig
	{
		public static string FromMail = "do-not-reply@yourcompany.local";
		public static string ToMail = "administrator@yourcompany.local";
		public static string Host = "localhost";
		public static bool EnableSsl = false;
		public static int Port = 25;
		public static string UserName = "";
		public static string Password = "";

		public static string PostmarkApiKey = "";
		public static string PostmarkMessageStream = "";

		public static string AwsAccessKey = "";
		public static string AwsSecretKey = "";

		public static long PostmarkCallEmailTemplateId = 1162802;
		public static long PostmarkTroubleAlertTemplateId = 4991122;
		public static long PostmarkCancelRecieptTemplateId = 1161121;
		public static long PostmarkChargeFailedTemplateId = 1161061;
		public static long PostmarkInviteTemplateId = 1160522;
		public static long PostmarkMessageTemplateId = 1162921;
		public static long PostmarkResetPasswordTemplateId = 1158941;
		public static long PostmarkRecieptTemplateId = 1160541;
		public static long PostmarkWelcomeTemplateId = 1158741;
		public static long PostmarkNewDepLinkTemplateId = 1456661;
	}

	public enum OutboundEmailTypes
	{
		Postmark	= 0,
		Smtp		= 1
	}
}
