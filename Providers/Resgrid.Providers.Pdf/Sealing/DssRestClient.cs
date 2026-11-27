using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.PdfProvider.Sealing
{
	/// <summary>
	/// Client for the resgrid-sealing sidecar, the EU DSS webapp's REST services (plan §22.2). Signing is DSS's two-step remote
	/// flow: getDataToSign returns the bytes to be signed, the key provider signs their SHA-256 (the key never leaves it), and
	/// signDocument assembles the PAdES signature with the same parameters and signing date; DSS adds the RFC 3161 timestamp from
	/// its own TSP source. Request shapes follow the DSS 6.x REST documentation (RemoteSignatureParameters, RemoteDocument,
	/// WSReportsDTO); an end-to-end run against the deployed sidecar is owed with the Fleet deployment.
	/// </summary>
	public sealed class DssRestClient : IDssClient
	{
		private static readonly HttpClient SharedClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		private readonly HttpClient _http;

		public DssRestClient() : this(SharedClient) { }

		/// <summary>Test seam: a client over a mocked handler.</summary>
		public DssRestClient(HttpClient http)
		{
			_http = http ?? SharedClient;
		}

		public bool IsConfigured => !string.IsNullOrWhiteSpace(SealingConfig.SidecarBaseUrl);

		public async Task<byte[]> GetDataToSignAsync(byte[] pdf, string fileName, string padesLevel, SigningCertificateInfo certificate, long signingDateUnixMs, CancellationToken cancellationToken = default)
		{
			var body = new JObject
			{
				["parameters"] = Parameters(padesLevel, certificate, signingDateUnixMs),
				["toSignDocument"] = Document(pdf, fileName)
			};
			var response = await PostAsync("signature/one-document/getDataToSign", body, cancellationToken);
			return Bytes(response?["bytes"]) ?? throw new InvalidOperationException("sealing_sidecar_bad_response");
		}

		public async Task<byte[]> SignDocumentAsync(byte[] pdf, string fileName, string padesLevel, SigningCertificateInfo certificate, long signingDateUnixMs, byte[] signatureValue, CancellationToken cancellationToken = default)
		{
			var body = new JObject
			{
				["parameters"] = Parameters(padesLevel, certificate, signingDateUnixMs),
				["signatureValue"] = new JObject
				{
					["algorithm"] = (certificate?.KeyAlgorithm == "ECDSA" ? "ECDSA" : "RSA") + "_SHA256",
					["value"] = Convert.ToBase64String(signatureValue ?? Array.Empty<byte>())
				},
				["toSignDocument"] = Document(pdf, fileName)
			};
			var response = await PostAsync("signature/one-document/signDocument", body, cancellationToken);
			return Bytes(response?["bytes"]) ?? throw new InvalidOperationException("sealing_sidecar_bad_response");
		}

		public async Task<byte[]> ExtendDocumentAsync(byte[] pdf, string fileName, string padesLevel, CancellationToken cancellationToken = default)
		{
			var body = new JObject
			{
				["toExtendDocument"] = Document(pdf, fileName),
				["parameters"] = new JObject
				{
					["signatureLevel"] = padesLevel,
					["signaturePackaging"] = "ENVELOPED",
					["digestAlgorithm"] = "SHA256"
				}
			};
			var response = await PostAsync("signature/one-document/extendDocument", body, cancellationToken);
			return Bytes(response?["bytes"]) ?? throw new InvalidOperationException("sealing_sidecar_bad_response");
		}

		public async Task<DssValidationReport> ValidateAsync(byte[] pdf, string fileName, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured)
				return new DssValidationReport { Completed = false, Error = "sealing_sidecar_not_configured" };

			try
			{
				var body = new JObject
				{
					["signedDocument"] = Document(pdf, fileName),
					["originalDocuments"] = new JArray(),
					["policy"] = null,
					["signatureId"] = null
				};
				var response = await PostAsync("validation/validateSignature", body, cancellationToken);
				return ParseValidation(response);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				return new DssValidationReport { Completed = false, Error = ex.Message };
			}
		}

		/// <summary>Reads the simple report's counts and per-signature results (DSS 5.x "signatureOrTimestamp" and 6.x shapes).</summary>
		public static DssValidationReport ParseValidation(JObject response)
		{
			var report = new DssValidationReport { Completed = response != null };
			if (response == null)
				return report;

			var simple = Property(response, "simpleReport") as JObject ?? response;
			report.SignaturesCount = (int?)Property(simple, "SignaturesCount") ?? 0;
			report.ValidSignaturesCount = (int?)Property(simple, "ValidSignaturesCount") ?? 0;

			foreach (var signature in simple.DescendantsAndSelf().OfType<JObject>().Where(o => Property(o, "SignatureFormat") != null && Property(o, "Indication") != null))
			{
				var result = new DssSignatureResult
				{
					Indication = (string)Property(signature, "Indication"),
					SubIndication = (string)Property(signature, "SubIndication"),
					SignatureFormat = (string)Property(signature, "SignatureFormat"),
					SignedBy = (string)Property(signature, "SignedBy"),
					SigningTime = Time(Property(signature, "SigningTime"))
				};

				// A signature timestamp shows as a nested Timestamp with a production time (and its TSA); BestSignatureTime is the
				// proven time DSS derived. Either is accepted as timestamp evidence; a signature without one has none.
				var timestamp = signature.Descendants().OfType<JObject>().FirstOrDefault(o => Property(o, "ProductionTime") != null);
				if (timestamp != null)
				{
					result.TimestampTime = Time(Property(timestamp, "ProductionTime"));
					result.TimestampAuthority = (string)Property(timestamp, "ProducedBy");
				}
				else if (result.SignatureFormat != null && !result.SignatureFormat.EndsWith("-B", StringComparison.OrdinalIgnoreCase) && !result.SignatureFormat.EndsWith("_B", StringComparison.OrdinalIgnoreCase))
				{
					result.TimestampTime = Time(Property(signature, "BestSignatureTime"));
				}

				report.Signatures.Add(result);
			}

			if (report.SignaturesCount == 0)
				report.SignaturesCount = report.Signatures.Count;
			if (report.ValidSignaturesCount == 0)
				report.ValidSignaturesCount = report.Signatures.Count(s => s.Passed);

			return report;
		}

		private static JToken Property(JObject obj, string name) =>
			obj?.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value is JToken value && value.Type != JTokenType.Null ? value : null;

		private static DateTime? Time(JToken token)
		{
			if (token == null)
				return null;
			if (token.Type == JTokenType.Date)
				return DateTime.SpecifyKind(((DateTime)token).ToUniversalTime(), DateTimeKind.Utc);
			if (token.Type == JTokenType.Integer)
				return DateTimeOffset.FromUnixTimeMilliseconds((long)token).UtcDateTime;
			return DateTimeOffset.TryParse((string)token, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.UtcDateTime : (DateTime?)null;
		}

		private static JObject Parameters(string padesLevel, SigningCertificateInfo certificate, long signingDateUnixMs)
		{
			var chain = new JArray();
			foreach (var der in certificate?.Chain ?? new List<byte[]>())
				chain.Add(new JObject { ["encodedCertificate"] = Convert.ToBase64String(der) });

			return new JObject
			{
				["signingCertificate"] = new JObject { ["encodedCertificate"] = Convert.ToBase64String(certificate?.Certificate ?? Array.Empty<byte>()) },
				["certificateChain"] = chain,
				["signatureLevel"] = padesLevel,
				["signaturePackaging"] = "ENVELOPED",
				["digestAlgorithm"] = "SHA256",
				["encryptionAlgorithm"] = certificate?.KeyAlgorithm == "ECDSA" ? "ECDSA" : "RSA",
				["generateTBSWithoutCertificate"] = false,
				["blevelParams"] = new JObject
				{
					["trustAnchorBPPolicy"] = true,
					["signingDate"] = signingDateUnixMs
				}
			};
		}

		private static JObject Document(byte[] bytes, string fileName) => new JObject
		{
			["bytes"] = Convert.ToBase64String(bytes ?? Array.Empty<byte>()),
			["digestAlgorithm"] = null,
			["name"] = string.IsNullOrWhiteSpace(fileName) ? "document.pdf" : fileName
		};

		private static byte[] Bytes(JToken token)
		{
			var value = token?.Type == JTokenType.String ? (string)token : null;
			if (string.IsNullOrWhiteSpace(value))
				return null;
			try { return Convert.FromBase64String(value); }
			catch (FormatException) { return null; }
		}

		private async Task<JObject> PostAsync(string path, JObject body, CancellationToken cancellationToken)
		{
			if (!IsConfigured)
				throw new InvalidOperationException("sealing_sidecar_not_configured");

			var url = SealingConfig.SidecarBaseUrl.TrimEnd('/') + "/" + path;
			using var request = new HttpRequestMessage(HttpMethod.Post, url)
			{
				Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
			};
			request.Headers.Accept.ParseAdd("application/json");
			if (!string.IsNullOrWhiteSpace(SealingConfig.SidecarApiKey))
				request.Headers.TryAddWithoutValidation("X-Resgrid-Sealing-Key", SealingConfig.SidecarApiKey.Trim());

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, SealingConfig.SidecarTimeoutSeconds)));
			using var response = await _http.SendAsync(request, timeout.Token);
			var text = await response.Content.ReadAsStringAsync(timeout.Token);
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException("sealing_sidecar_http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));

			return string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
		}
	}
}
