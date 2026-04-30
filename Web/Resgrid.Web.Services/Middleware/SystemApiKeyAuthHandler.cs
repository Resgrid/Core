using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Middleware
{
	/// <summary>
	/// Authentication handler that validates requests bearing the X-Resgrid-SystemApiKey header.
	/// Used by the SMTP Relay in hosted multi-department mode to bypass OAuth 2.0 entirely.
	/// The handler creates a ClaimsPrincipal with full permissions across all departments.
	/// Department scoping is achieved via the DepartmentId field on individual API request models.
	/// </summary>
	public class SystemApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
	{
		public SystemApiKeyAuthHandler(
			IOptionsMonitor<AuthenticationSchemeOptions> options,
			ILoggerFactory logger,
			UrlEncoder encoder,
			ISystemClock clock)
			: base(options, logger, encoder, clock)
		{
		}

		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			// Skip if the endpoint allows anonymous access
			var endpoint = Context.GetEndpoint();
			if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
				return Task.FromResult(AuthenticateResult.NoResult());

			// Only process requests that have the X-Resgrid-SystemApiKey header
			if (!Request.Headers.TryGetValue("X-Resgrid-SystemApiKey", out var apiKeyHeader))
				return Task.FromResult(AuthenticateResult.NoResult());

			var apiKey = apiKeyHeader.ToString();

			if (string.IsNullOrWhiteSpace(apiKey))
				return Task.FromResult(AuthenticateResult.NoResult());

			// Validate the API key against the configured system API key (timing-safe comparison)
			if (string.IsNullOrWhiteSpace(Config.SecurityConfig.SystemApiKey) ||
				!FixedTimeApiKeyEquals(Config.SecurityConfig.SystemApiKey, apiKey))
			{
				return Task.FromResult(AuthenticateResult.Fail("Invalid System API Key"));
			}

			// Create a ClaimsPrincipal with full permissions
			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.Name, "SMTP Relay System"),
				new Claim(ClaimTypes.PrimarySid, "smtp_relay_system"),
				new Claim(ClaimTypes.PrimaryGroupSid, "0"),
				new Claim(ClaimTypes.GivenName, "SMTP Relay"),
				new Claim(ClaimTypes.Email, "smtp-relay@resgrid.local"),
				// Data claims
				new Claim(ResgridClaimTypes.Data.TimeZone, "UTC"),
				new Claim(ResgridClaimTypes.Data.DisplayName, "SMTP Relay"),
				new Claim(ResgridClaimTypes.Data.UserId, "smtp_relay_system"),
				new Claim(ResgridClaimTypes.Data.ServiceAccount, "true")
			};

			// Add all resource claims for full cross-department access
			var resources = new[]
			{
				ResgridClaimTypes.Resources.Department,
				ResgridClaimTypes.Resources.Personnel,
				ResgridClaimTypes.Resources.Call,
				ResgridClaimTypes.Resources.Log,
				ResgridClaimTypes.Resources.Action,
				ResgridClaimTypes.Resources.Staffing,
				ResgridClaimTypes.Resources.Unit,
				ResgridClaimTypes.Resources.Group,
				ResgridClaimTypes.Resources.UnitLog,
				ResgridClaimTypes.Resources.Messages,
				ResgridClaimTypes.Resources.Role,
				ResgridClaimTypes.Resources.Profile,
				ResgridClaimTypes.Resources.Reports,
				ResgridClaimTypes.Resources.GenericGroup,
				ResgridClaimTypes.Resources.Documents,
				ResgridClaimTypes.Resources.Notes,
				ResgridClaimTypes.Resources.Schedule,
				ResgridClaimTypes.Resources.Shift,
				ResgridClaimTypes.Resources.Training,
				ResgridClaimTypes.Resources.PersonalInfo,
				ResgridClaimTypes.Resources.Inventory,
				ResgridClaimTypes.Resources.Command,
				ResgridClaimTypes.Resources.Connect,
				ResgridClaimTypes.Resources.Protocols,
				ResgridClaimTypes.Resources.Forms,
				ResgridClaimTypes.Resources.Voice,
				ResgridClaimTypes.Resources.CustomStates,
				ResgridClaimTypes.Resources.Contacts,
				ResgridClaimTypes.Resources.Workflow,
				ResgridClaimTypes.Resources.WorkflowCredential,
				ResgridClaimTypes.Resources.WorkflowRun,
				ResgridClaimTypes.Resources.Sso,
				ResgridClaimTypes.Resources.Scim,
				ResgridClaimTypes.Resources.Udf,
				ResgridClaimTypes.Resources.Route,
				ResgridClaimTypes.Resources.CommunicationTest,
				ResgridClaimTypes.Resources.WeatherAlert
			};

			var actions = new[]
			{
				ResgridClaimTypes.Actions.View,
				ResgridClaimTypes.Actions.Create,
				ResgridClaimTypes.Actions.Update,
				ResgridClaimTypes.Actions.Delete
			};

			foreach (var resource in resources)
			{
				foreach (var action in actions)
				{
					claims.Add(new Claim(resource, action));
				}
			}

			var identity = new ClaimsIdentity(claims, "SystemApiKey");
			var principal = new ClaimsPrincipal(identity);
			var ticket = new AuthenticationTicket(principal, Scheme.Name);

			return Task.FromResult(AuthenticateResult.Success(ticket));
		}

		/// <summary>
		/// Performs a timing-safe comparison of two API key strings to prevent timing attacks.
		/// Unlike string.Equals with Ordinal, this compares every byte regardless of where
		/// the first mismatch occurs.
		/// </summary>
		private static bool FixedTimeApiKeyEquals(string stored, string provided)
		{
			if (stored == null || provided == null)
				return false;

			var storedBytes = Encoding.UTF8.GetBytes(stored);
			var providedBytes = Encoding.UTF8.GetBytes(provided);

			return CryptographicOperations.FixedTimeEquals(storedBytes, providedBytes);
		}
	}
}
