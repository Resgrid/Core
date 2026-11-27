using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Eventing.Controllers
{
	/// <summary>
	/// e-Sign platform webhooks (S2, plan §22.3) — hints only. Documenso's webhook secret is a plain shared value, not an HMAC,
	/// so a hint is accepted only from the cluster network (ESignConfig.WebhookAllowedNetworks; blank refuses every hint) and
	/// with the shared secret (ESignConfig.WebhookSecret; blank refuses every hint too). It is recorded once per provider event id (the inbound-email-receipts pattern)
	/// and answered 200; worker 90 re-fetches the envelope over the API and trusts only that state.
	/// </summary>
	[AllowAnonymous]
	[ApiController]
	[Route("webhooks/esign")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public class ESignWebhooksController : ControllerBase
	{
		public const int MaxBodyBytes = 256 * 1024;

		private readonly IESignWebhookService _webhooks;

		public ESignWebhooksController(IESignWebhookService webhooks)
		{
			_webhooks = webhooks;
		}

		[HttpPost("documenso")]
		[RequestSizeLimit(MaxBodyBytes)]
		public async Task<IActionResult> Documenso(CancellationToken cancellationToken)
		{
			// The address is the one ForwardedHeadersSetup accepted (the ingress proxy network only), never a raw header.
			if (!IsAllowed(HttpContext.Connection.RemoteIpAddress, ESignConfig.WebhookAllowedNetworks))
				return StatusCode(403);

			// Security review L3 (2026-11-25): fail closed. A blank secret refuses every hint, like a blank network list.
			if (string.IsNullOrWhiteSpace(ESignConfig.WebhookSecret))
				return StatusCode(403);
			if (!SecretMatches(Request.Headers["X-Documenso-Secret"].ToString(), ESignConfig.WebhookSecret))
				return StatusCode(401);

			string body;
			using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
				body = await ReadBoundedAsync(reader, MaxBodyBytes, cancellationToken);
			if (body == null)
				return StatusCode(413);

			var (eventId, eventType, envelopeKey) = ParseHint(body, Request.Headers["X-Documenso-Event-Id"].ToString());
			if (envelopeKey == null)
				return Ok(); // Nothing to re-fetch; a 200 stops redelivery.

			try
			{
				await _webhooks.RecordHintAsync(ESignProviderKinds.Documenso, eventId, eventType, envelopeKey, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// A lost hint costs nothing but latency: worker 90 re-fetches open envelopes on its own schedule.
				Logging.LogException(ex, "An e-sign webhook hint could not be recorded.");
				return StatusCode(503);
			}

			return Ok();
		}

		/// <summary>
		/// The event id (header, then body id, then a hash of event + envelope + time), the event type, and what identifies the
		/// envelope: the Resgrid external id when present, else the envelope or document id. Nothing else is read.
		/// </summary>
		public static (string EventId, string EventType, string EnvelopeKey) ParseHint(string body, string headerEventId)
		{
			JObject json;
			try { json = JObject.Parse(body ?? string.Empty); }
			catch (Exception) { return (null, null, null); }

			var eventType = (string)json["event"];
			var payload = json["payload"] as JObject ?? new JObject();
			var externalId = (string)payload["externalId"];
			var envelopeKey = !string.IsNullOrWhiteSpace(externalId) && externalId.StartsWith("resgrid:", StringComparison.Ordinal)
				? externalId
				: (payload["envelopeId"] ?? payload["id"])?.ToString();
			if (string.IsNullOrWhiteSpace(envelopeKey))
				return (null, eventType, null);

			var eventId = !string.IsNullOrWhiteSpace(headerEventId) ? headerEventId.Trim()
				: !string.IsNullOrWhiteSpace((string)json["id"]) ? (string)json["id"]
				: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((eventType ?? string.Empty) + "|" + envelopeKey + "|" + ((string)json["createdAt"] ?? body)))).ToLowerInvariant();

			return (eventId, eventType, envelopeKey);
		}

		/// <summary>Whether the address is inside one of the comma separated CIDRs. Blank or unparsable lists allow nothing.</summary>
		public static bool IsAllowed(IPAddress address, string networks)
		{
			if (address == null || string.IsNullOrWhiteSpace(networks))
				return false;
			if (address.IsIPv4MappedToIPv6)
				address = address.MapToIPv4();

			foreach (var entry in networks.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var value = entry.Trim();
				if (!value.Contains('/'))
					value += IPAddress.TryParse(value, out var single) && single.AddressFamily == AddressFamily.InterNetworkV6 ? "/128" : "/32";
				if (System.Net.IPNetwork.TryParse(value, out var network) && network.Contains(address))
					return true;
			}

			return false;
		}

		private static bool SecretMatches(string presented, string expected)
		{
			if (string.IsNullOrEmpty(presented))
				return false;
			var a = SHA256.HashData(Encoding.UTF8.GetBytes(presented.Trim()));
			var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected.Trim()));
			return CryptographicOperations.FixedTimeEquals(a, b);
		}

		private static async Task<string> ReadBoundedAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
		{
			var buffer = new char[8192];
			var sb = new StringBuilder();
			int read;
			while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
			{
				sb.Append(buffer, 0, read);
				if (sb.Length > maxChars)
					return null;
			}
			return sb.ToString();
		}
	}
}
