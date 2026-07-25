using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;

namespace Resgrid.Web.Services.ApplicationCore.UnitTracking
{
	public enum UnitTrackingHttpAuthenticationStatus
	{
		Authenticated = 0,
		NotFound = 1,
		Unauthorized = 2
	}

	public sealed class UnitTrackingHttpAuthenticationResult
	{
		public UnitTrackingHttpAuthenticationStatus Status { get; set; }
		public AuthenticatedTrackingSource Source { get; set; }
	}

	public class UnitTrackingHttpAuthenticationService
	{
		private const int MaximumAuthorizationHeaderLength = 2048;
		private readonly IUnitTrackingAuthenticationService _authenticationService;

		public UnitTrackingHttpAuthenticationService(
			IUnitTrackingAuthenticationService authenticationService)
		{
			_authenticationService = authenticationService;
		}

		public async Task<UnitTrackingHttpAuthenticationResult> AuthenticateEndpointAsync(
			HttpRequest request,
			string unitTrackingDeviceId,
			CancellationToken cancellationToken = default)
		{
			var device = await _authenticationService.GetEnabledDeviceByEndpointIdAsync(
				unitTrackingDeviceId,
				cancellationToken);
			if (device == null)
				return Result(UnitTrackingHttpAuthenticationStatus.NotFound);

			var presented = await ExtractCredentialAsync(request, device, cancellationToken);
			if (presented == null)
				return Result(UnitTrackingHttpAuthenticationStatus.Unauthorized);

			var authenticated = await _authenticationService.AuthenticateAsync(
				presented.Token,
				cancellationToken: cancellationToken);
			if (!MatchesEndpointCredential(device, authenticated, presented))
				return Result(UnitTrackingHttpAuthenticationStatus.Unauthorized);

			return Authenticated(authenticated);
		}

		public async Task<UnitTrackingHttpAuthenticationResult> AuthenticateCapabilityAsync(
			string capabilityToken,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(capabilityToken))
				return Result(UnitTrackingHttpAuthenticationStatus.NotFound);

			var authenticated = await _authenticationService.AuthenticateAsync(
				capabilityToken,
				cancellationToken: cancellationToken);
			if (authenticated?.Credential == null ||
			    authenticated.Credential.AuthMode != (int)UnitTrackingAuthMode.CapabilityPath)
				return Result(UnitTrackingHttpAuthenticationStatus.NotFound);

			return Authenticated(authenticated);
		}

		private async Task<PresentedCredential> ExtractCredentialAsync(
			HttpRequest request,
			UnitTrackingDevice device,
			CancellationToken cancellationToken)
		{
			var authorization = request.Headers[HeaderNames.Authorization].ToString();
			if (!string.IsNullOrWhiteSpace(authorization))
			{
				if (authorization.Length > MaximumAuthorizationHeaderLength)
					return null;

				if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
				{
					var token = authorization.Substring("Bearer ".Length).Trim();
					return string.IsNullOrWhiteSpace(token)
						? null
						: new PresentedCredential(UnitTrackingAuthMode.Bearer, token, null, null);
				}

				if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
					return ParseBasic(authorization.Substring("Basic ".Length).Trim());

				return null;
			}

			var credentials = await _authenticationService.GetActiveCredentialsForDeviceAsync(
				device.UnitTrackingDeviceId,
				cancellationToken: cancellationToken);
			foreach (var credential in credentials.Where(item =>
				         item.AuthMode == (int)UnitTrackingAuthMode.CustomHeader &&
				         IsValidHeaderName(item.HeaderName)))
			{
				if (!request.Headers.TryGetValue(credential.HeaderName, out var values))
					continue;

				var token = values.FirstOrDefault()?.Trim();
				if (!string.IsNullOrWhiteSpace(token))
				{
					return new PresentedCredential(
						UnitTrackingAuthMode.CustomHeader,
						token,
						null,
						credential.HeaderName);
				}
			}

			return null;
		}

		private static PresentedCredential ParseBasic(string encoded)
		{
			if (string.IsNullOrWhiteSpace(encoded))
				return null;

			var decodedBytes = new byte[(encoded.Length * 3 / 4) + 3];
			if (!Convert.TryFromBase64String(encoded, decodedBytes, out var bytesWritten))
				return null;

			var decoded = Encoding.UTF8.GetString(decodedBytes, 0, bytesWritten);
			var separator = decoded.IndexOf(':');
			if (separator <= 0 || separator == decoded.Length - 1)
				return null;

			return new PresentedCredential(
				UnitTrackingAuthMode.Basic,
				decoded.Substring(separator + 1),
				decoded.Substring(0, separator),
				null);
		}

		private static bool MatchesEndpointCredential(
			UnitTrackingDevice requestedDevice,
			UnitTrackingAuthenticationResult authenticated,
			PresentedCredential presented)
		{
			if (authenticated?.Device == null || authenticated.Credential == null)
				return false;
			if (!string.Equals(
				    requestedDevice.UnitTrackingDeviceId,
				    authenticated.Device.UnitTrackingDeviceId,
				    StringComparison.OrdinalIgnoreCase))
				return false;
			if (authenticated.Credential.AuthMode != (int)presented.Mode)
				return false;

			if (presented.Mode == UnitTrackingAuthMode.Basic &&
			    !string.Equals(
				    authenticated.Credential.BasicUsername,
				    presented.Username,
				    StringComparison.Ordinal))
				return false;

			if (presented.Mode == UnitTrackingAuthMode.CustomHeader &&
			    !string.Equals(
				    authenticated.Credential.HeaderName,
				    presented.HeaderName,
				    StringComparison.OrdinalIgnoreCase))
				return false;

			return true;
		}

		private static bool IsValidHeaderName(string headerName)
		{
			if (string.IsNullOrWhiteSpace(headerName) || headerName.Length > 128)
				return false;

			return headerName.All(character =>
				char.IsLetterOrDigit(character) ||
				"!#$%&'*+-.^_`|~".Contains(character));
		}

		private static UnitTrackingHttpAuthenticationResult Authenticated(
			UnitTrackingAuthenticationResult authenticated) =>
			new()
			{
				Status = UnitTrackingHttpAuthenticationStatus.Authenticated,
				Source = new AuthenticatedTrackingSource
				{
					Device = authenticated.Device,
					Credential = authenticated.Credential
				}
			};

		private static UnitTrackingHttpAuthenticationResult Result(
			UnitTrackingHttpAuthenticationStatus status) =>
			new() { Status = status };

		private sealed class PresentedCredential
		{
			public PresentedCredential(
				UnitTrackingAuthMode mode,
				string token,
				string username,
				string headerName)
			{
				Mode = mode;
				Token = token;
				Username = username;
				HeaderName = headerName;
			}

			public UnitTrackingAuthMode Mode { get; }
			public string Token { get; }
			public string Username { get; }
			public string HeaderName { get; }
		}
	}
}
