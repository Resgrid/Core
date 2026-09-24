using System;
using System.Security.Claims;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Who is calling a Protected Workflows v4 command. Approve, renew and the department toggle refuse any caller that
	/// is not an interactive user: the SystemApiKey scheme, client_credentials tokens (ServiceAccount claim) and the
	/// system_* subjects the token endpoint issues to integrations and publishers.
	/// </summary>
	public static class ProtectedWorkflowCallerHelper
	{
		public const string SystemApiKeyScheme = "SystemApiKey";

		public static bool IsInteractiveUser(ClaimsPrincipal principal)
		{
			if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
				return false;

			foreach (var identity in principal.Identities)
			{
				if (string.Equals(identity?.AuthenticationType, SystemApiKeyScheme, StringComparison.OrdinalIgnoreCase))
					return false;
			}

			if (principal.HasClaim(c => c.Type == ResgridClaimTypes.Data.ServiceAccount &&
				string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)))
				return false;

			foreach (var type in new[] { "sub", ClaimTypes.PrimarySid, ClaimTypes.NameIdentifier, ResgridClaimTypes.Data.UserId })
			{
				var value = principal.FindFirst(type)?.Value;
				if (!string.IsNullOrEmpty(value) &&
					(value.StartsWith("system_", StringComparison.OrdinalIgnoreCase) || value.Equals("smtp_relay_system", StringComparison.OrdinalIgnoreCase)))
					return false;
			}

			return true;
		}
	}
}
