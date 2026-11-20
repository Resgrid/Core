using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Signed webhook intake (call-intake-and-ai-destinations-plan.md D3): any other system (CAD bridge, alarm panel, Zapier)
	/// posts a JSON request to a department's webhook channel and it becomes a PENDING call a dispatcher reviews. The URL token
	/// names the channel; the body is authenticated with the channel's secret: header
	/// <c>X-Resgrid-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "{t}.{raw body}"&gt;</c>, within
	/// IntakeConfig.WebhookTimestampToleranceSeconds. Body: <c>{ "subject"?, "message", "sender"?, "externalId"?, "latitude"?,
	/// "longitude"?, "address"? }</c>, at most IntakeConfig.MaxWebhookBodyBytes. The same externalId (or, without one, the same
	/// signed request) never creates a second call.
	/// </summary>
	[AllowAnonymous]
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Produces("application/json")]
	[RequestSizeLimit(1048576)]
	public class IntakeController : ControllerBase
	{
		private readonly IIntakeChannelsService _channels;
		private readonly IIntakeService _intake;

		public IntakeController(IIntakeChannelsService channels, IIntakeService intake)
		{
			_channels = channels;
			_intake = intake;
		}

		/// <summary>Creates a pending call from a signed webhook request.</summary>
		/// <param name="token">The channel's webhook token (from its URL).</param>
		/// <param name="cancellationToken">Request cancellation.</param>
		/// <returns>
		/// 200 with the call id when the call was created (or already had been, for a repeated request); 400 for a body that is not
		/// the expected JSON; 401 for a missing, stale or wrong signature; 403 when the department or channel refuses it; 404 for an
		/// unknown or disabled channel; 413 for a body over the limit; 429 when the channel sends too fast; 503 when creating the
		/// call failed (retry).
		/// </returns>
		[HttpPost("Webhook/{token}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
		[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
		[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
		public async Task<ActionResult<IntakeWebhookResult>> Webhook(string token, CancellationToken cancellationToken)
		{
			if (!IntakeConfig.Enabled)
				return NotFound();

			var address = IntakeChannelAddresses.Normalize(IntakeChannelTypes.Webhook, token);
			if (address == null)
				return NotFound();

			if (!IntakeWebhookRateLimiter.TryAcquire(address, DateTime.UtcNow, IntakeConfig.WebhookRequestsPerMinute))
				return StatusCode(StatusCodes.Status429TooManyRequests, new IntakeWebhookResult { Status = "TooManyRequests" });

			var max = Math.Clamp(IntakeConfig.MaxWebhookBodyBytes, 256, 1048576);
			if (Request.ContentLength.HasValue && Request.ContentLength.Value > max)
				return StatusCode(StatusCodes.Status413PayloadTooLarge, new IntakeWebhookResult { Status = "TooLarge" });

			var rawBody = await ReadBodyAsync(Request.Body, max, cancellationToken);
			if (rawBody == null)
				return StatusCode(StatusCodes.Status413PayloadTooLarge, new IntakeWebhookResult { Status = "TooLarge" });

			var channel = await _channels.ResolveChannelAsync(IntakeChannelTypes.Webhook, address);
			if (channel == null)
				return NotFound();

			var signature = Request.Headers[IntakeWebhookSignature.HeaderName].ToString();
			var check = IntakeWebhookSignature.Verify(signature, _channels.GetWebhookSecret(channel), rawBody, DateTime.UtcNow,
				IntakeConfig.WebhookTimestampToleranceSeconds);
			if (check != IntakeWebhookSignatureResults.Valid)
				return Unauthorized(new IntakeWebhookResult { Status = check == IntakeWebhookSignatureResults.Stale ? "StaleSignature" : "InvalidSignature" });

			var request = Parse(rawBody);
			if (request == null)
				return BadRequest(new IntakeWebhookResult { Status = "InvalidBody", Message = "Expected a JSON object with a non-empty \"message\"." });

			var result = await _intake.ReceiveAsync(new IntakeEnvelope
			{
				ChannelType = IntakeChannelTypes.Webhook,
				IntakeChannelId = channel.IntakeChannelId,
				ChannelAddress = address,
				// A signed request without its own id is identified by its signature (one per timestamp and body).
				ProviderMessageId = !string.IsNullOrWhiteSpace(request.ExternalId) ? "ext:" + request.ExternalId.Trim() : "sig:" + FirstSignature(signature),
				Sender = string.IsNullOrWhiteSpace(request.Sender) ? "webhook" : request.Sender.Trim(),
				SenderName = string.IsNullOrWhiteSpace(request.Sender) ? null : request.Sender.Trim(),
				Subject = request.Subject,
				Body = request.Message,
				ExternalId = request.ExternalId,
				Address = request.Address,
				Latitude = request.Latitude,
				Longitude = request.Longitude,
				ReceivedOnUtc = DateTime.UtcNow
			}, cancellationToken);

			var body = new IntakeWebhookResult { Status = result.State.ToString(), CallId = result.CallId, Message = result.Acknowledgement };
			switch (result.State)
			{
				case IntakeMessageStates.CallCreated:
				case IntakeMessageStates.Duplicate:
					return Ok(body);
				case IntakeMessageStates.Failed:
					return StatusCode(StatusCodes.Status503ServiceUnavailable, body);
				default:
					body.Message = result.Reason;
					return StatusCode(StatusCodes.Status403Forbidden, body);
			}
		}

		/// <summary>The body as UTF-8 text, or null when it is longer than <paramref name="max"/> bytes.</summary>
		public static async Task<string> ReadBodyAsync(Stream body, int max, CancellationToken cancellationToken)
		{
			using var buffer = new MemoryStream();
			var chunk = new byte[8192];
			int read;
			while ((read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
			{
				if (buffer.Length + read > max)
					return null;
				buffer.Write(chunk, 0, read);
			}

			return Encoding.UTF8.GetString(buffer.ToArray());
		}

		/// <summary>The request fields, or null when the body is not a JSON object with a non-empty message.</summary>
		public static IntakeWebhookRequest Parse(string rawBody)
		{
			if (string.IsNullOrWhiteSpace(rawBody))
				return null;

			try
			{
				var root = JToken.Parse(rawBody) as JObject;
				if (root == null)
					return null;

				string Text(string name, int max)
				{
					var token = root.GetValue(name, StringComparison.OrdinalIgnoreCase);
					if (token == null || token.Type == JTokenType.Null)
						return null;
					if (token.Type != JTokenType.String && token.Type != JTokenType.Integer)
						throw new JsonException(name);
					var value = token.ToString().Trim();
					return value.Length == 0 ? null : value.Length <= max ? value : value.Substring(0, max);
				}

				double? Number(string name)
				{
					var token = root.GetValue(name, StringComparison.OrdinalIgnoreCase);
					if (token == null || token.Type == JTokenType.Null)
						return null;
					if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
						return token.Value<double>();
					if (token.Type == JTokenType.String && double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out var parsed))
						return parsed;
					throw new JsonException(name);
				}

				var request = new IntakeWebhookRequest
				{
					Subject = Text("subject", 200),
					Message = Text("message", Math.Max(100, IntakeConfig.MaxMessageLength)),
					Sender = Text("sender", 256),
					ExternalId = Text("externalId", 200),
					Address = Text("address", 500),
					Latitude = Number("latitude"),
					Longitude = Number("longitude")
				};

				return string.IsNullOrWhiteSpace(request.Message) ? null : request;
			}
			catch (JsonException)
			{
				return null;
			}
			catch (FormatException)
			{
				return null;
			}
			catch (InvalidCastException)
			{
				return null;
			}
		}

		private static string FirstSignature(string header) =>
			(header ?? string.Empty).Split(',').Select(p => p.Trim()).Where(p => p.StartsWith("v1=", StringComparison.Ordinal))
				.Select(p => p.Substring(3)).FirstOrDefault() ?? Guid.NewGuid().ToString("N");
	}

	/// <summary>The webhook intake body.</summary>
	public sealed class IntakeWebhookRequest
	{
		public string Subject { get; set; }

		public string Message { get; set; }

		public string Sender { get; set; }

		public string ExternalId { get; set; }

		public string Address { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }
	}

	/// <summary>The webhook intake answer.</summary>
	public sealed class IntakeWebhookResult
	{
		/// <summary>CallCreated, Duplicate, Refused, SenderNotAllowed, Failed, InvalidSignature, StaleSignature, InvalidBody, TooLarge, TooManyRequests.</summary>
		public string Status { get; set; }

		public int? CallId { get; set; }

		public string Message { get; set; }
	}

	/// <summary>
	/// Per webhook token fixed one-minute window, per API process (an unsigned flood is cut off before any database read). Old
	/// windows are dropped as they are passed.
	/// </summary>
	public static class IntakeWebhookRateLimiter
	{
		private static readonly ConcurrentDictionary<string, Window> Windows = new ConcurrentDictionary<string, Window>(StringComparer.Ordinal);

		private sealed class Window
		{
			public long Minute;
			public int Count;
		}

		public static bool TryAcquire(string token, DateTime nowUtc, int perMinute)
		{
			if (perMinute <= 0)
				return true;

			var minute = nowUtc.Ticks / TimeSpan.TicksPerMinute;
			if (Windows.Count > 10000)
				foreach (var stale in Windows.Where(w => w.Value.Minute < minute).Select(w => w.Key).ToList())
					Windows.TryRemove(stale, out _);

			var window = Windows.GetOrAdd(token, _ => new Window { Minute = minute });
			lock (window)
			{
				if (window.Minute != minute)
				{
					window.Minute = minute;
					window.Count = 0;
				}

				window.Count++;
				return window.Count <= perMinute;
			}
		}

		/// <summary>Tests: forget every window.</summary>
		public static void Reset() => Windows.Clear();
	}
}
