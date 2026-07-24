using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Tracking;
using Resgrid.Model.Services;
using Resgrid.Web.Services.ApplicationCore.UnitTracking;
using Resgrid.Web.Services.Middleware;
using Resgrid.Web.Services.Models.v4.UnitTracking;

namespace Resgrid.Web.Services.Controllers.v4
{
	[ApiController]
	[AllowAnonymous]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Route("api/v4/unit-trackers")]
	public class UnitTrackingIngressController : ControllerBase
	{
		private readonly UnitTrackingHttpAuthenticationService _httpAuthenticationService;
		private readonly UnitTrackingJsonPayloadParser _payloadParser;
		private readonly UnitTrackingRateLimiter _rateLimiter;
		private readonly IUnitTrackingIngressService _ingressService;

		public UnitTrackingIngressController(
			UnitTrackingHttpAuthenticationService httpAuthenticationService,
			UnitTrackingJsonPayloadParser payloadParser,
			UnitTrackingRateLimiter rateLimiter,
			IUnitTrackingIngressService ingressService)
		{
			_httpAuthenticationService = httpAuthenticationService;
			_payloadParser = payloadParser;
			_rateLimiter = rateLimiter;
			_ingressService = ingressService;
		}

		[HttpPost("{unitTrackingDeviceId}/positions")]
		[ProducesResponseType(typeof(UnitTrackingIngressResponse), StatusCodes.Status202Accepted)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
		[ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
		[ProducesResponseType(typeof(UnitTrackingIngressErrorResponse), StatusCodes.Status422UnprocessableEntity)]
		[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
		[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
		public async Task<IActionResult> PostPositions(string unitTrackingDeviceId)
		{
			if (!TrackingHttpsEnabled())
				return NotFound();

			ApplyRequestBodyLimit();
			UnitTrackingHttpAuthenticationResult authentication;
			try
			{
				authentication = await _httpAuthenticationService.AuthenticateEndpointAsync(
					Request,
					unitTrackingDeviceId,
					HttpContext.RequestAborted);
			}
			catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				return Unavailable(ex, "Unit tracking endpoint authentication is unavailable.");
			}

			if (authentication.Status == UnitTrackingHttpAuthenticationStatus.NotFound)
				return UnknownEndpointResponse();
			if (authentication.Status != UnitTrackingHttpAuthenticationStatus.Authenticated)
				return Unauthorized();

			return await AcceptAsync(authentication.Source);
		}

		[HttpPost("c/{capabilityToken}")]
		[ProducesResponseType(typeof(UnitTrackingIngressResponse), StatusCodes.Status202Accepted)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
		[ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
		[ProducesResponseType(typeof(UnitTrackingIngressErrorResponse), StatusCodes.Status422UnprocessableEntity)]
		[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
		[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
		public async Task<IActionResult> PostCapability(string capabilityToken)
		{
			if (!TrackingHttpsEnabled())
				return NotFound();

			ApplyRequestBodyLimit();
			var rawCapabilityToken =
				HttpContext.Items[CapabilityPathRedactionMiddleware.CapabilityTokenItemKey] as string;
			UnitTrackingHttpAuthenticationResult authentication;
			try
			{
				authentication = await _httpAuthenticationService.AuthenticateCapabilityAsync(
					rawCapabilityToken,
					HttpContext.RequestAborted);
			}
			catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				return Unavailable(ex, "Unit tracking capability authentication is unavailable.");
			}

			if (authentication.Status != UnitTrackingHttpAuthenticationStatus.Authenticated)
				return UnknownEndpointResponse();

			return await AcceptAsync(authentication.Source);
		}

		private async Task<IActionResult> AcceptAsync(
			Resgrid.Model.Tracking.AuthenticatedTrackingSource source)
		{
			if (!UnitTrackingNetworkPolicy.IsAllowed(
				    HttpContext.Connection.RemoteIpAddress,
				    source.Device.AllowedSourceCidrs))
				return NotFound();

			var requestLimit = _rateLimiter.CheckRequest(
				source.Device.UnitTrackingDeviceId,
				source.Credential.UnitTrackingCredentialId);
			if (!requestLimit.Allowed)
				return RateLimited(requestLimit);

			if (!_payloadParser.Supports(source.Device.PayloadAdapterKey))
				return Invalid("The configured payload adapter is not supported.");

			var receivedOn = DateTime.UtcNow;
			var parsed = await _payloadParser.ParseAsync(
				Request,
				receivedOn,
				HttpContext.RequestAborted);
			var parseResponse = MapParseFailure(parsed);
			if (parseResponse != null)
				return parseResponse;

			var recordLimit = _rateLimiter.CheckRecords(
				source.Device.UnitTrackingDeviceId,
				source.Credential.UnitTrackingCredentialId,
				parsed.Positions.Count);
			if (!recordLimit.Allowed)
				return RateLimited(recordLimit);

			source.ReportedDeviceIdentifier = parsed.ReportedDeviceIdentifier;
			TrackingIngressResult result;
			try
			{
				result = await _ingressService.AcceptAsync(
					source,
					parsed.Positions,
					HttpContext.RequestAborted);
			}
			catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				return Unavailable(ex, "Unit tracking ingress is unavailable.");
			}

			return result.Status switch
			{
				TrackingIngressStatus.Accepted => Accepted(new UnitTrackingIngressResponse
				{
					Accepted = result.Accepted,
					DuplicatesPossible = result.DuplicatesPossible,
					ReceivedOn = result.ReceivedOn
				}),
				TrackingIngressStatus.Invalid => UnprocessableEntity(new UnitTrackingIngressErrorResponse
				{
					Errors = result.Errors?.ToArray() ?? Array.Empty<string>()
				}),
				_ => StatusCode(StatusCodes.Status503ServiceUnavailable)
			};
		}

		private IActionResult MapParseFailure(UnitTrackingPayloadParseResult parsed)
		{
			return parsed.Status switch
			{
				UnitTrackingPayloadParseStatus.Success => null,
				UnitTrackingPayloadParseStatus.Malformed => BadRequest(),
				UnitTrackingPayloadParseStatus.Invalid => UnprocessableEntity(
					new UnitTrackingIngressErrorResponse
					{
						Errors = parsed.Errors?.ToArray() ?? Array.Empty<string>()
					}),
				UnitTrackingPayloadParseStatus.TooLarge => StatusCode(
					StatusCodes.Status413PayloadTooLarge),
				_ => StatusCode(StatusCodes.Status415UnsupportedMediaType)
			};
		}

		private IActionResult UnknownEndpointResponse()
		{
			var limit = _rateLimiter.CheckUnknownEndpoint(
				HttpContext.Connection.RemoteIpAddress?.ToString());
			return limit.Allowed ? NotFound() : RateLimited(limit);
		}

		private IActionResult Invalid(string error) =>
			UnprocessableEntity(new UnitTrackingIngressErrorResponse
			{
				Errors = new[] { error }
			});

		private IActionResult RateLimited(UnitTrackingRateLimitResult limit)
		{
			Response.Headers.RetryAfter = Math.Max(1, limit.RetryAfterSeconds).ToString();
			return StatusCode(StatusCodes.Status429TooManyRequests);
		}

		private IActionResult Unavailable(Exception exception, string message)
		{
			Logging.LogException(exception, message);
			return StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		private void ApplyRequestBodyLimit()
		{
			var feature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
			if (feature != null && !feature.IsReadOnly)
				feature.MaxRequestBodySize = Math.Max(1, UnitTrackingConfig.MaxRequestBytes);
		}

		private static bool TrackingHttpsEnabled() =>
			UnitTrackingConfig.Enabled && UnitTrackingConfig.HttpsIngressEnabled;
	}
}
