using System;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Web.Helpers
{
	/// <summary>Short-lived navigation context only. No proposed values, arbitrary URLs or authority to save.</summary>
	public static class AdminAssistReturnLink
	{
		public const string CookieName = "Resgrid.AdminAssist.Return";
		public static string Create(IDataProtectionProvider protection, AdminAssistActor actor, string page, DateTimeOffset now)
		{
			if (page is not ("wizard" or "report")) throw new ArgumentException("Unsupported return page.");
			return Protector(protection, actor).Protect(page + "|" + now.AddMinutes(30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
		}
		public static string Read(IDataProtectionProvider protection, AdminAssistActor actor, string token, DateTimeOffset now)
		{
			if (string.IsNullOrEmpty(token) || token.Length > 2048) return null;
			try
			{
				var fields = Protector(protection, actor).Unprotect(token).Split('|');
				if (fields.Length != 2 || fields[0] is not ("wizard" or "report") ||
					!long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry) ||
					expiry <= now.ToUnixTimeSeconds() || expiry > now.AddMinutes(30).ToUnixTimeSeconds()) return null;
				return fields[0];
			}
			catch (CryptographicException) { return null; }
			catch (FormatException) { return null; }
		}
		private static IDataProtector Protector(IDataProtectionProvider protection, AdminAssistActor actor)
		{
			if (actor?.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor?.UserId)) throw new ArgumentException("An authenticated department member is required.");
			return protection.CreateProtector("Resgrid.AdminAssist.SetupReturn.v1", actor.DepartmentId.ToString(CultureInfo.InvariantCulture), actor.UserId);
		}
	}
}
