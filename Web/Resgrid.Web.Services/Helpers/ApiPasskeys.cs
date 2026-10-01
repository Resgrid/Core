using System;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Shared v4 plumbing for passkey ceremonies: the caller comes from the session that request validation accepted, and
	/// each service outcome maps to one status and one code from the shared vocabulary (workbook section 7.6).
	/// </summary>
	public static class ApiPasskeys
	{
		public static PasskeyCaller Caller(HttpContext httpContext, string userId, string userName, int? departmentId) =>
			PasskeyCaller.From(HttpProtectedGrantContext.SessionOf(httpContext), userId, userName, departmentId, SystemAuditSystems.Api,
				IpAddressHelper.GetRequestIP(httpContext.Request, true));

		/// <summary>
		/// The WebAuthn credential JSON as the client sent it: a JSON object (PublicKeyCredential.toJSON()) or a string
		/// holding one. Null when absent.
		/// </summary>
		public static string CredentialJson(JToken credential) => credential switch
		{
			null => null,
			{ Type: JTokenType.Null } => null,
			{ Type: JTokenType.String } => credential.Value<string>(),
			_ => credential.ToString(Formatting.None)
		};

		/// <summary>Embeds the server's options JSON unchanged, as an object rather than an escaped string.</summary>
		public static JRaw Options(string optionsJson) => string.IsNullOrWhiteSpace(optionsJson) ? null : new JRaw(optionsJson);

		public static int StatusFor(PasskeyOutcome outcome) => outcome switch
		{
			PasskeyOutcome.InvalidRequest or PasskeyOutcome.Unavailable or PasskeyOutcome.NotRegisteredForClient => StatusCodes.Status400BadRequest,
			PasskeyOutcome.StepUpRequired or PasskeyOutcome.ReauthenticationRequired or PasskeyOutcome.VerificationFailed => StatusCodes.Status401Unauthorized,
			PasskeyOutcome.NotFound => StatusCodes.Status404NotFound,
			PasskeyOutcome.TooManyRequests or PasskeyOutcome.TooManyAttempts => StatusCodes.Status429TooManyRequests,
			PasskeyOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
			_ => StatusCodes.Status409Conflict
		};

		public static string TitleFor(PasskeyOutcome outcome) => outcome switch
		{
			PasskeyOutcome.Unavailable => "Passkeys are not available here.",
			PasskeyOutcome.SessionRequired => "Sign in again to manage passkeys.",
			PasskeyOutcome.ReauthenticationRequired => "Confirm your password (or sign in with SSO) again first.",
			PasskeyOutcome.StepUpRequired => "Verify with your authenticator app or a passkey for this app first.",
			PasskeyOutcome.EnrollmentRequired => "Set up an authenticator app and recovery codes before adding a passkey.",
			PasskeyOutcome.LimitReached => "This app already has the maximum number of passkeys. Remove one first.",
			PasskeyOutcome.TooManyRequests or PasskeyOutcome.TooManyAttempts => "Too many attempts. Wait a few minutes and try again.",
			PasskeyOutcome.ChallengeExpired => "The passkey request expired. Start again.",
			PasskeyOutcome.ChallengeConsumed => "The passkey request was already used. Start again.",
			PasskeyOutcome.VerificationFailed => "The passkey could not be verified.",
			PasskeyOutcome.NotRegisteredForClient => "You have no passkey for this app.",
			PasskeyOutcome.NotFound => "Passkey not found.",
			PasskeyOutcome.InvalidRequest => "The request is not valid.",
			PasskeyOutcome.ServiceUnavailable => "The passkey service is unavailable. Try again.",
			_ => "The passkey request failed."
		};

		/// <summary>The API name of a client, as the relying-party configuration uses it.</summary>
		public static string ClientName(UserSessionClientApplication client) => client switch
		{
			UserSessionClientApplication.Web => "web",
			UserSessionClientApplication.Responder => "responder",
			UserSessionClientApplication.Unit => "unit",
			UserSessionClientApplication.Dispatch => "dispatch",
			UserSessionClientApplication.Command => "ic",
			_ => null
		};

		public static UserSessionClientApplication? ClientFromName(string name) => name?.Trim().ToLowerInvariant() switch
		{
			"web" => UserSessionClientApplication.Web,
			"responder" => UserSessionClientApplication.Responder,
			"unit" => UserSessionClientApplication.Unit,
			"dispatch" => UserSessionClientApplication.Dispatch,
			"ic" or "command" => UserSessionClientApplication.Command,
			_ => null
		};

		/// <summary>The clients a passkey can be bound to, in display order (plan section 6.5).</summary>
		public static readonly UserSessionClientApplication[] PasskeyClients =
		{
			UserSessionClientApplication.Web,
			UserSessionClientApplication.Responder,
			UserSessionClientApplication.Unit,
			UserSessionClientApplication.Dispatch,
			UserSessionClientApplication.Command
		};

		public static string Iso(DateTime? utc) =>
			utc == null ? null : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToString("O");
	}
}
