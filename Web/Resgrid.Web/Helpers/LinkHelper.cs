using System;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Resgrid.Config;
using Resgrid.Framework;

namespace Resgrid.Web.Helpers
{
	public static class LinkHelper
	{
		private const int CallImageTokenLifetimeHours = 24;

		public static string GenerateCallImageToken(int callId, int attachmentId)
		{
			var payload = $"{callId}|{attachmentId}|{DateTimeOffset.UtcNow.AddHours(CallImageTokenLifetimeHours).ToUnixTimeSeconds()}";
			var encrypted = SymmetricEncryption.Encrypt(payload, SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

			return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(encrypted));
		}

		public static string ExtratHref(string url)
		{
			int start = url.IndexOf("href=\"") + 6;
			int end = url.IndexOf("\"", start);

			string fullUrl = url.Substring(start, end - start);

			return fullUrl;
		}
	}
}