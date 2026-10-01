using Resgrid.Model;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// The client application a sign-in request says it is (<c>X-Resgrid-Client</c>). A label for session metadata and for
	/// binding a login transaction to the app that started it; never proof of the app's identity.
	/// </summary>
	public static class ApiClientApplication
	{
		public const string Header = "X-Resgrid-Client";

		public static UserSessionClientApplication Resolve(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return UserSessionClientApplication.Api;

			return value.Trim().ToLowerInvariant() switch
			{
				"web" => UserSessionClientApplication.Web,
				"responder" => UserSessionClientApplication.Responder,
				"unit" => UserSessionClientApplication.Unit,
				"dispatch" => UserSessionClientApplication.Dispatch,
				"bigboard" => UserSessionClientApplication.BigBoard,
				"command" => UserSessionClientApplication.Command,
				"ic" => UserSessionClientApplication.Command,
				"mcp" => UserSessionClientApplication.Mcp,
				_ => UserSessionClientApplication.Api
			};
		}
	}
}
