using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.PdfProvider.ESign
{
	/// <summary>
	/// Documenso Community Edition adapter (decision 16), headless: Resgrid is the only client, with one team-scoped API token per
	/// department. Uses the v2 envelope API — create from the sealed-input PDF with recipients and one signature field each,
	/// distribute with no platform e-mail (Resgrid sends the links and the codes), re-fetch the envelope, download the completed
	/// PDF and the audit log. Every path is in <see cref="Paths"/>; all of them, the audit-log path included, were exercised end to
	/// end against Documenso v2.22.0 on 2026-11-26 (Fleet deployment test). Optional (decision 16b): a blank
	/// ESignConfig.DocumensoBaseUrl ⇒ <see cref="IsAvailable"/> false.
	/// </summary>
	public sealed class DocumensoESignProvider : IESignProvider
	{
		/// <summary>The v2 API paths (relative to the base URL).</summary>
		public static class Paths
		{
			public const string CreateEnvelope = "api/v2/envelope/create";
			public const string DistributeEnvelope = "api/v2/envelope/distribute";
			public const string DeleteEnvelope = "api/v2/envelope/delete";
			public static string GetEnvelope(string id) => "api/v2/envelope/" + Uri.EscapeDataString(id);
			public static string DownloadItem(string itemId) => "api/v2/envelope/item/" + Uri.EscapeDataString(itemId) + "/download?version=signed";
			public static string AuditLog(string id) => "api/v2/envelope/" + Uri.EscapeDataString(id) + "/audit-log";
			public static string EmbedSign(string token) => "embed/sign/" + Uri.EscapeDataString(token);
		}

		/// <summary>
		/// Every e-mail flag of the v2 envelope meta.emailSettings (Documenso v2.22.0 OpenAPI; each defaults to on). Resgrid sends the
		/// links, codes, reminders and completion notices itself and recipients carry placeholder addresses, so all of them are turned
		/// off: with them on, Documenso queues mail jobs that retry against a deployment with no mail route. A flag Documenso adds later
		/// defaults to on, so check this list on every Documenso upgrade.
		/// </summary>
		public static readonly string[] EmailSettingFlags =
		{
			"recipientSigningRequest", "recipientRemoved", "recipientSigned", "documentPending", "documentCompleted", "documentDeleted",
			"ownerDocumentCompleted", "ownerRecipientExpired", "ownerDocumentCreated"
		};

		private static readonly HttpClient SharedClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		private readonly HttpClient _http;

		public DocumensoESignProvider() : this(SharedClient) { }

		/// <summary>Test seam: a client over a mocked handler.</summary>
		public DocumensoESignProvider(HttpClient http)
		{
			_http = http ?? SharedClient;
		}

		public ESignProviderKinds Kind => ESignProviderKinds.Documenso;

		public bool IsAvailable => !string.IsNullOrWhiteSpace(ESignConfig.DocumensoBaseUrl);

		public string EmbedOrigin
		{
			get
			{
				var url = string.IsNullOrWhiteSpace(ESignConfig.DocumensoPublicUrl) ? ESignConfig.DocumensoBaseUrl : ESignConfig.DocumensoPublicUrl;
				return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
			}
		}

		public async Task<ESignEnvelope> CreateEnvelopeAsync(string apiToken, ESignEnvelopeRequest request, CancellationToken cancellationToken = default)
		{
			if (request?.Pdf == null || request.Recipients == null || request.Recipients.Count == 0)
				throw new ArgumentException("A PDF and at least one recipient are required.", nameof(request));

			var recipients = new JArray();
			foreach (var r in request.Recipients.OrderBy(x => x.SigningOrder))
			{
				recipients.Add(new JObject
				{
					["email"] = string.IsNullOrWhiteSpace(r.Email) ? PlaceholderEmail(r.Reference) : r.Email,
					["name"] = r.Name ?? string.Empty,
					["role"] = "SIGNER",
					["signingOrder"] = Math.Max(1, r.SigningOrder),
					["fields"] = new JArray
					{
						// One signature box per signer on the sealed-input PDF's signature page, stacked down the page (percent units).
						new JObject
						{
							["type"] = "SIGNATURE",
							["identifier"] = 0,
							["page"] = Math.Max(1, request.SignaturePage),
							["positionX"] = 10,
							["positionY"] = Math.Min(85, 20 + (Math.Max(1, r.SigningOrder) - 1) * 18),
							["width"] = 40,
							["height"] = 8
						}
					}
				});
			}

			var payload = new JObject
			{
				["type"] = "DOCUMENT",
				["title"] = string.IsNullOrWhiteSpace(request.Title) ? "Document" : request.Title,
				["externalId"] = request.ExternalId,
				["recipients"] = recipients,
				["meta"] = new JObject
				{
					["distributionMethod"] = "NONE",
					["signingOrder"] = request.Ordered ? "SEQUENTIAL" : "PARALLEL",
					["language"] = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
					["emailSettings"] = NoEmails(),
					// Reminders are Resgrid's too (worker 90); never inherit a team or organisation default.
					["reminderSettings"] = new JObject
					{
						["sendAfter"] = new JObject { ["disabled"] = true },
						["repeatEvery"] = new JObject { ["disabled"] = true }
					}
				}
			};

			using var content = new MultipartFormDataContent();
			content.Add(new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json"), "payload");
			var file = new ByteArrayContent(request.Pdf);
			file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
			content.Add(file, "files", string.IsNullOrWhiteSpace(request.FileName) ? "document.pdf" : request.FileName);

			var created = await SendAsync(HttpMethod.Post, Paths.CreateEnvelope, apiToken, content, cancellationToken);
			var envelopeId = (string)(created?["id"] ?? created?["envelopeId"]);
			if (string.IsNullOrWhiteSpace(envelopeId))
				throw new InvalidOperationException("esign_provider_bad_response");

			var distributed = await SendAsync(HttpMethod.Post, Paths.DistributeEnvelope, apiToken,
				Json(new JObject { ["envelopeId"] = envelopeId, ["meta"] = new JObject { ["distributionMethod"] = "NONE", ["emailSettings"] = NoEmails() } }), cancellationToken);

			// The distribute answer carries the recipients' signing tokens; the envelope is re-read for anything it omitted.
			var envelope = ParseEnvelope(distributed, envelopeId);
			if (envelope.Recipients.Count == 0 || envelope.Recipients.Any(x => string.IsNullOrWhiteSpace(x.SigningToken)))
				envelope = await GetEnvelopeAsync(apiToken, envelopeId, cancellationToken) ?? envelope;
			envelope.EnvelopeId ??= envelopeId;
			envelope.ExternalId ??= request.ExternalId;

			// Match recipients back by order (and e-mail when present).
			foreach (var r in request.Recipients)
			{
				var match = envelope.Recipients.FirstOrDefault(x => !string.IsNullOrWhiteSpace(r.Email) && string.Equals(x.Email, r.Email, StringComparison.OrdinalIgnoreCase))
					?? envelope.Recipients.FirstOrDefault(x => string.Equals(x.Email, PlaceholderEmail(r.Reference), StringComparison.OrdinalIgnoreCase))
					?? envelope.Recipients.FirstOrDefault(x => x.SigningOrder == r.SigningOrder);
				if (match != null)
					match.Reference = r.Reference;
			}

			return envelope;
		}

		public async Task<ESignEnvelope> GetEnvelopeAsync(string apiToken, string envelopeId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(envelopeId))
				return null;
			var response = await SendAsync(HttpMethod.Get, Paths.GetEnvelope(envelopeId), apiToken, null, cancellationToken, allowNotFound: true);
			return response == null ? null : ParseEnvelope(response, envelopeId);
		}

		public async Task<byte[]> DownloadCompletedPdfAsync(string apiToken, ESignEnvelope envelope, CancellationToken cancellationToken = default)
		{
			var itemId = envelope?.ItemIds?.FirstOrDefault();
			if (string.IsNullOrWhiteSpace(itemId))
				return null;
			return await SendForBytesAsync(Paths.DownloadItem(itemId), apiToken, cancellationToken);
		}

		public async Task<byte[]> DownloadAuditCertificateAsync(string apiToken, ESignEnvelope envelope, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(envelope?.EnvelopeId))
				return null;
			try
			{
				return await SendForBytesAsync(Paths.AuditLog(envelope.EnvelopeId), apiToken, cancellationToken);
			}
			catch (InvalidOperationException)
			{
				// The platform's certificate page is also inside the completed PDF; a missing audit endpoint is not a failure.
				return null;
			}
		}

		public async Task<bool> DeleteEnvelopeAsync(string apiToken, string envelopeId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(envelopeId))
				return false;
			try
			{
				await SendAsync(HttpMethod.Post, Paths.DeleteEnvelope, apiToken, Json(new JObject { ["envelopeId"] = envelopeId }), cancellationToken, allowNotFound: true);
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		public string BuildEmbedUrl(string signingToken, string signerName)
		{
			var origin = EmbedOrigin;
			if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(signingToken))
				return null;

			// The embed SDK's options travel in the URL fragment as base64 JSON (never sent to the server).
			var options = new JObject { ["name"] = signerName ?? string.Empty, ["lockName"] = true };
			var hash = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.ToString(Formatting.None)));
			return origin.TrimEnd('/') + "/" + Paths.EmbedSign(signingToken) + "#" + hash;
		}

		/// <summary>Reads an envelope from the v2 shape (recipients, signing status, items), tolerating the older document shape.</summary>
		public static ESignEnvelope ParseEnvelope(JObject json, string fallbackId)
		{
			var root = json?["envelope"] as JObject ?? json ?? new JObject();
			var envelope = new ESignEnvelope
			{
				EnvelopeId = (string)root["id"] ?? (string)root["envelopeId"] ?? fallbackId,
				Status = (string)root["status"],
				ExternalId = (string)root["externalId"]
			};

			foreach (var item in (root["envelopeItems"] as JArray ?? root["items"] as JArray ?? new JArray()).OfType<JObject>())
			{
				var id = (string)item["id"];
				if (!string.IsNullOrWhiteSpace(id))
					envelope.ItemIds.Add(id);
			}

			foreach (var r in (root["recipients"] as JArray ?? json?["recipients"] as JArray ?? new JArray()).OfType<JObject>())
			{
				envelope.Recipients.Add(new ESignRecipient
				{
					ProviderRecipientId = r["id"]?.ToString(),
					Email = (string)r["email"],
					Name = (string)r["name"],
					SigningOrder = (int?)r["signingOrder"] ?? 1,
					SigningToken = (string)r["token"],
					SigningStatus = (string)r["signingStatus"],
					SignedAtUtc = r["signedAt"] == null || r["signedAt"].Type == JTokenType.Null ? (DateTime?)null :
						(r["signedAt"].Type == JTokenType.Date ? DateTime.SpecifyKind(((DateTime)r["signedAt"]).ToUniversalTime(), DateTimeKind.Utc) :
						DateTimeOffset.TryParse((string)r["signedAt"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.UtcDateTime : (DateTime?)null),
					RejectionReason = (string)r["rejectionReason"]
				});
			}

			return envelope;
		}

		private static string PlaceholderEmail(string reference) =>
			"signer-" + (string.IsNullOrWhiteSpace(reference) ? Guid.NewGuid().ToString("N") : reference) + "@" + (ESignConfig.PlaceholderEmailDomain ?? "signers.resgrid.invalid");

		private static JObject NoEmails() => new JObject(EmailSettingFlags.Select(flag => new JProperty(flag, false)));

		private static StringContent Json(JObject body) => new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

		private async Task<JObject> SendAsync(HttpMethod method, string path, string apiToken, HttpContent content, CancellationToken cancellationToken, bool allowNotFound = false)
		{
			using var request = Request(method, path, apiToken);
			request.Content = content;
			using var timeout = Deadline(cancellationToken);
			using var response = await _http.SendAsync(request, timeout.Token);
			if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
				return null;
			var text = await response.Content.ReadAsStringAsync(timeout.Token);
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException("esign_provider_http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
			return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
		}

		private async Task<byte[]> SendForBytesAsync(string path, string apiToken, CancellationToken cancellationToken)
		{
			using var request = Request(HttpMethod.Get, path, apiToken);
			using var timeout = Deadline(cancellationToken);
			using var response = await _http.SendAsync(request, timeout.Token);
			if (response.StatusCode == HttpStatusCode.NotFound)
				return null;
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException("esign_provider_http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
			return await response.Content.ReadAsByteArrayAsync(timeout.Token);
		}

		private HttpRequestMessage Request(HttpMethod method, string path, string apiToken)
		{
			if (!IsAvailable)
				throw new InvalidOperationException("esign_provider_not_configured");
			if (string.IsNullOrWhiteSpace(apiToken))
				throw new InvalidOperationException("esign_account_not_configured");

			var request = new HttpRequestMessage(method, ESignConfig.DocumensoBaseUrl.TrimEnd('/') + "/" + path);
			// Documenso API tokens are sent as the Authorization value itself (not a Bearer scheme).
			request.Headers.TryAddWithoutValidation("Authorization", apiToken.Trim());
			request.Headers.Accept.ParseAdd("application/json");
			return request;
		}

		private static CancellationTokenSource Deadline(CancellationToken cancellationToken)
		{
			var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			source.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, ESignConfig.DocumensoTimeoutSeconds)));
			return source;
		}
	}
}
