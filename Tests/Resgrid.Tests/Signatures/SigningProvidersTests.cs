extern alias Eventing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.PdfProvider.ESign;
using Resgrid.Providers.PdfProvider.Sealing;
using ESignWebhooksController = Eventing::Resgrid.Web.Eventing.Controllers.ESignWebhooksController;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Signatures
{
	/// <summary>
	/// e-Signature S1/S2 providers and edges: the DSS client's two-step request shapes and validation parsing, the signing-key
	/// providers (PKCS#12 signs a digest that verifies; blank or "remote" with no signer fails closed), the Documenso adapter
	/// (no platform mail, embed URL, envelope parsing), the Eventing webhook (cluster allowlist, idempotency key), and worker 90
	/// keeping its steps independent.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SigningProvidersTests
	{
		private sealed class RecordingHandler : HttpMessageHandler
		{
			public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new List<(HttpRequestMessage, string)>();
			public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
				Calls.Add((request, body));
				return Respond(request);
			}
		}

		private static HttpResponseMessage Json(object body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json") };

		private string _sidecar, _documenso, _documensoPublic, _source;

		[SetUp]
		public void SetUp()
		{
			_sidecar = SealingConfig.SidecarBaseUrl;
			_documenso = ESignConfig.DocumensoBaseUrl;
			_documensoPublic = ESignConfig.DocumensoPublicUrl;
			_source = SealingConfig.CertificateSource;
		}

		[TearDown]
		public void TearDown()
		{
			SealingConfig.SidecarBaseUrl = _sidecar;
			ESignConfig.DocumensoBaseUrl = _documenso;
			ESignConfig.DocumensoPublicUrl = _documensoPublic;
			SealingConfig.CertificateSource = _source;
		}

		#region DSS sidecar

		[Test]
		public async Task Data_to_sign_and_sign_document_carry_the_same_level_certificate_and_signing_date()
		{
			SealingConfig.SidecarBaseUrl = "http://sealing.test/services/rest";
			var handler = new RecordingHandler { Respond = r => Json(new { bytes = Convert.ToBase64String(new byte[] { 7, 8 }) }) };
			var client = new DssRestClient(new HttpClient(handler));
			var certificate = new SigningCertificateInfo { Certificate = new byte[] { 1, 2 }, Chain = { new byte[] { 3 } }, KeyAlgorithm = "RSA" };

			await client.GetDataToSignAsync(new byte[] { 0x25 }, "doc.pdf", "PAdES_BASELINE_LTA", certificate, 1700000000000);
			await client.SignDocumentAsync(new byte[] { 0x25 }, "doc.pdf", "PAdES_BASELINE_LTA", certificate, 1700000000000, new byte[] { 9 });

			handler.Calls.Select(c => c.Request.RequestUri.ToString()).Should().Equal(
				"http://sealing.test/services/rest/signature/one-document/getDataToSign",
				"http://sealing.test/services/rest/signature/one-document/signDocument");
			foreach (var call in handler.Calls)
			{
				var parameters = JObject.Parse(call.Body)["parameters"];
				((string)parameters["signatureLevel"]).Should().Be("PAdES_BASELINE_LTA");
				((string)parameters["signaturePackaging"]).Should().Be("ENVELOPED");
				((long)parameters["blevelParams"]["signingDate"]).Should().Be(1700000000000);
				((string)parameters["signingCertificate"]["encodedCertificate"]).Should().Be(Convert.ToBase64String(new byte[] { 1, 2 }));
			}
			((string)JObject.Parse(handler.Calls[1].Body)["signatureValue"]["algorithm"]).Should().Be("RSA_SHA256");
		}

		[Test]
		public void An_unconfigured_sidecar_refuses_and_validation_reports_not_completed()
		{
			SealingConfig.SidecarBaseUrl = "";
			var client = new DssRestClient(new HttpClient(new RecordingHandler { Respond = _ => throw new InvalidOperationException("no call expected") }));

			client.IsConfigured.Should().BeFalse();
			FluentActions.Awaiting(() => client.GetDataToSignAsync(new byte[1], "d.pdf", "PAdES_BASELINE_T", new SigningCertificateInfo(), 0)).Should().ThrowAsync<InvalidOperationException>();
			client.ValidateAsync(new byte[1], "d.pdf").Result.Completed.Should().BeFalse();
		}

		[Test]
		public void The_simple_report_is_read_from_the_6x_and_5x_shapes()
		{
			var six = JObject.Parse(@"{ ""simpleReport"": { ""ValidSignaturesCount"": 1, ""SignaturesCount"": 2, ""signatureOrTimestampOrEvidenceRecord"": [
				{ ""Signature"": { ""Indication"": ""TOTAL_PASSED"", ""SignatureFormat"": ""PAdES-BASELINE-T"", ""SignedBy"": ""Resgrid US"", ""SigningTime"": ""2026-11-24T10:00:00Z"",
				  ""Timestamps"": { ""Timestamp"": [ { ""Indication"": ""PASSED"", ""ProductionTime"": ""2026-11-24T10:00:02Z"", ""ProducedBy"": ""Test TSA"" } ] } } },
				{ ""Signature"": { ""Indication"": ""TOTAL_FAILED"", ""SubIndication"": ""HASH_FAILURE"", ""SignatureFormat"": ""PAdES-BASELINE-B"" } } ] } }");
			var report = DssRestClient.ParseValidation(six);

			report.SignaturesCount.Should().Be(2);
			report.ValidSignaturesCount.Should().Be(1);
			report.Signatures[0].HasTimestamp.Should().BeTrue();
			report.Signatures[0].TimestampAuthority.Should().Be("Test TSA");
			report.Signatures[0].TimestampTime.Should().Be(new DateTime(2026, 11, 24, 10, 0, 2, DateTimeKind.Utc));
			report.Signatures[1].HasTimestamp.Should().BeFalse("B-B has no timestamp");
			report.AllPassed.Should().BeFalse();

			var five = JObject.Parse(@"{ ""simpleReport"": { ""signatureOrTimestamp"": [ { ""Signature"": { ""Indication"": ""INDETERMINATE"", ""SignatureFormat"": ""PAdES-BASELINE-LTA"", ""BestSignatureTime"": ""2026-01-01T00:00:00Z"" } } ] } }");
			var older = DssRestClient.ParseValidation(five);
			older.SignaturesCount.Should().Be(1);
			older.Signatures.Single().HasTimestamp.Should().BeTrue();
		}

		#endregion

		#region Signing keys

		private static byte[] SelfSignedPkcs12(string password)
		{
			using var rsa = RSA.Create(2048);
			var request = new CertificateRequest("CN=Resgrid Sealing Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
			return certificate.Export(X509ContentType.Pkcs12, password);
		}

		[Test]
		public async Task A_pkcs12_key_signs_a_digest_that_verifies_with_its_certificate()
		{
			using var provider = new Pkcs12SigningKeyProvider(SelfSignedPkcs12("pw"), "pw");
			var digest = SHA256.HashData(Encoding.UTF8.GetBytes("data to sign"));

			var signature = await provider.SignDigestAsync(digest);
			var info = await provider.GetCertificateAsync();

			provider.IsConfigured.Should().BeTrue();
			info.KeyAlgorithm.Should().Be("RSA");
			info.Thumbprint.Should().HaveLength(64);
			using var certificate = X509CertificateLoader.LoadCertificate(info.Certificate);
			certificate.GetRSAPublicKey().VerifyHash(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Should().BeTrue();
		}

		[Test]
		public void A_blank_or_remote_source_without_a_signer_fails_closed()
		{
			ConfiguredSigningKeyProvider.Select("", new NotConfiguredRemoteHashSigner()).IsConfigured.Should().BeFalse();
			ConfiguredSigningKeyProvider.Select("something", new NotConfiguredRemoteHashSigner()).IsConfigured.Should().BeFalse();
			var remote = ConfiguredSigningKeyProvider.Select("remote", new NotConfiguredRemoteHashSigner());
			remote.IsConfigured.Should().BeFalse();
			FluentActions.Awaiting(() => remote.SignDigestAsync(new byte[32])).Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task A_remote_hash_signer_receives_only_the_digest()
		{
			var signer = new Mock<IRemoteHashSigner>();
			signer.SetupGet(x => x.IsConfigured).Returns(true);
			signer.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SigningCertificateInfo { KeyAlgorithm = "ECDSA" });
			signer.Setup(x => x.SignHashAsync(It.IsAny<byte[]>(), "ECDSA", It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 5 });
			var provider = new RemoteSigningKeyProvider(signer.Object);
			var digest = new byte[32];

			(await provider.SignDigestAsync(digest)).Should().Equal(5);
			signer.Verify(x => x.SignHashAsync(digest, "ECDSA", It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion

		#region Documenso adapter

		[Test]
		public async Task An_envelope_is_created_without_platform_mail_and_its_recipient_token_is_returned()
		{
			ESignConfig.DocumensoBaseUrl = "http://documenso.test";
			var handler = new RecordingHandler
			{
				Respond = r => r.RequestUri.AbsolutePath.EndsWith("/create") ? Json(new { id = "envelope_9" })
					: Json(new { id = "envelope_9", status = "PENDING", recipients = new[] { new { id = 31, email = "cust@example.com", name = "Casey", signingOrder = 1, token = "tok-31", signingStatus = "NOT_SIGNED" } } })
			};
			var provider = new DocumensoESignProvider(new HttpClient(handler));

			var envelope = await provider.CreateEnvelopeAsync("api_abc", new ESignEnvelopeRequest
			{
				Title = "DTR 7", ExternalId = "resgrid:11:4", Pdf = Encoding.ASCII.GetBytes("%PDF-1.4"), FileName = "dtr.pdf", SignaturePage = 2,
				Recipients = { new ESignRecipient { Reference = "100", Name = "Casey", Email = "cust@example.com", SigningOrder = 1 } }
			});

			envelope.EnvelopeId.Should().Be("envelope_9");
			envelope.Recipients.Single().SigningToken.Should().Be("tok-31");
			envelope.Recipients.Single().Reference.Should().Be("100");
			handler.Calls[0].Request.Headers.GetValues("Authorization").Single().Should().Be("api_abc");
			handler.Calls[0].Body.Should().Contain("\"distributionMethod\":\"NONE\"").And.Contain("\"page\":2").And.Contain("resgrid:11:4");
			JObject.Parse(handler.Calls[1].Body)["meta"]["distributionMethod"].ToString().Should().Be("NONE");
		}

		[Test]
		public async Task Documenso_sends_no_mail_and_no_reminders_of_its_own()
		{
			ESignConfig.DocumensoBaseUrl = "http://documenso.test";
			var handler = new RecordingHandler
			{
				Respond = r => r.RequestUri.AbsolutePath.EndsWith("/create") ? Json(new { id = "envelope_9" })
					: Json(new { id = "envelope_9", status = "PENDING", recipients = new[] { new { id = 31, email = "signer-100@signers.resgrid.invalid", name = "Casey", signingOrder = 1, token = "tok-31", signingStatus = "NOT_SIGNED" } } })
			};
			var provider = new DocumensoESignProvider(new HttpClient(handler));

			await provider.CreateEnvelopeAsync("api_abc", new ESignEnvelopeRequest
			{
				Title = "DTR 7", ExternalId = "resgrid:11:4", Pdf = Encoding.ASCII.GetBytes("%PDF-1.4"), FileName = "dtr.pdf",
				Recipients = { new ESignRecipient { Reference = "100", Name = "Casey", SigningOrder = 1 } }
			});

			// The create call is multipart: the "payload" part is one line of JSON.
			var payload = JObject.Parse(handler.Calls[0].Body.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("{\"type\":\"DOCUMENT\"")));
			var createMeta = (JObject)payload["meta"];
			var distributeMeta = (JObject)JObject.Parse(handler.Calls[1].Body)["meta"];

			// Documenso v2.22.0's flags, each on by default; documentCompleted / ownerDocumentCompleted were the retries seen live.
			DocumensoESignProvider.EmailSettingFlags.Should().HaveCount(9).And.Contain(new[] { "documentCompleted", "ownerDocumentCompleted", "recipientSigningRequest" });
			foreach (var meta in new[] { createMeta, distributeMeta })
			{
				var settings = (JObject)meta["emailSettings"];
				settings.Properties().Select(p => p.Name).Should().BeEquivalentTo(DocumensoESignProvider.EmailSettingFlags);
				settings.Properties().Should().OnlyContain(p => p.Value.Type == JTokenType.Boolean && !(bool)p.Value);
			}
			createMeta["reminderSettings"]["sendAfter"]["disabled"].Value<bool>().Should().BeTrue();
			createMeta["reminderSettings"]["repeatEvery"]["disabled"].Value<bool>().Should().BeTrue();
		}

		[Test]
		public void An_unset_base_url_means_remote_signing_is_unavailable()
		{
			ESignConfig.DocumensoBaseUrl = "";
			var provider = new DocumensoESignProvider(new HttpClient(new RecordingHandler()));
			provider.IsAvailable.Should().BeFalse();
			FluentActions.Awaiting(() => provider.GetEnvelopeAsync("api", "e1")).Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public void The_embed_url_is_on_the_public_origin_with_the_name_locked()
		{
			ESignConfig.DocumensoBaseUrl = "http://documenso:3000";
			ESignConfig.DocumensoPublicUrl = "https://sign.resgrid.test/some/path";
			var provider = new DocumensoESignProvider(new HttpClient(new RecordingHandler()));

			provider.EmbedOrigin.Should().Be("https://sign.resgrid.test");
			var url = provider.BuildEmbedUrl("tok-31", "Casey");
			url.Should().StartWith("https://sign.resgrid.test/embed/sign/tok-31#");
			var options = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(url.Substring(url.IndexOf('#') + 1))));
			((bool)options["lockName"]).Should().BeTrue();
		}

		[Test]
		public void An_envelope_is_parsed_with_statuses_items_and_signing_times()
		{
			var envelope = DocumensoESignProvider.ParseEnvelope(JObject.Parse(@"{ ""id"": ""envelope_1"", ""status"": ""COMPLETED"", ""envelopeItems"": [ { ""id"": ""item_1"" } ],
				""recipients"": [ { ""id"": 3, ""email"": ""a@b.c"", ""signingStatus"": ""SIGNED"", ""signedAt"": ""2026-11-24T15:00:00Z"", ""signingOrder"": 1 },
				{ ""id"": 4, ""signingStatus"": ""REJECTED"", ""rejectionReason"": ""no"" } ] }"), null);

			envelope.Completed.Should().BeTrue();
			envelope.ItemIds.Should().Equal("item_1");
			envelope.Recipients[0].Signed.Should().BeTrue();
			envelope.Recipients[0].SignedAtUtc.Should().Be(new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc));
			envelope.Recipients[1].Rejected.Should().BeTrue();
		}

		#endregion

		#region Webhook hints (Resgrid.Web.Eventing)

		[TestCase("10.42.0.7", "10.42.0.0/16", true)]
		[TestCase("::ffff:10.42.0.7", "10.42.0.0/16", true)]
		[TestCase("10.43.0.7", "10.42.0.0/16", false)]
		[TestCase("203.0.113.9", "10.42.0.0/16, 192.168.1.5", false)]
		[TestCase("192.168.1.5", "10.42.0.0/16, 192.168.1.5", true)]
		[TestCase("10.42.0.7", "", false)]
		[TestCase("10.42.0.7", "not-a-network", false)]
		public void Hints_are_accepted_only_from_the_configured_cluster_networks(string address, string networks, bool allowed) =>
			ESignWebhooksController.IsAllowed(IPAddress.Parse(address), networks).Should().Be(allowed);

		[Test]
		public async Task A_hint_from_outside_the_allowlist_is_refused_and_never_recorded()
		{
			var allowed = ESignConfig.WebhookAllowedNetworks;
			try
			{
				ESignConfig.WebhookAllowedNetworks = "10.42.0.0/16";
				var webhooks = new Mock<IESignWebhookService>(MockBehavior.Strict);
				var controller = new ESignWebhooksController(webhooks.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
				controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
				controller.HttpContext.Request.Body = new System.IO.MemoryStream(Encoding.UTF8.GetBytes("{\"event\":\"DOCUMENT_COMPLETED\",\"payload\":{\"id\":\"envelope_1\"}}"));

				var result = await controller.Documenso(CancellationToken.None);

				result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
				webhooks.VerifyNoOtherCalls();
			}
			finally
			{
				ESignConfig.WebhookAllowedNetworks = allowed;
			}
		}

		[Test]
		public async Task A_hint_from_the_cluster_is_recorded_under_its_event_id()
		{
			var allowed = ESignConfig.WebhookAllowedNetworks;
			var secret = ESignConfig.WebhookSecret;
			try
			{
				ESignConfig.WebhookAllowedNetworks = "10.42.0.0/16";
				ESignConfig.WebhookSecret = "shh";
				var webhooks = new Mock<IESignWebhookService>();
				var controller = new ESignWebhooksController(webhooks.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
				controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.42.3.3");
				controller.HttpContext.Request.Headers["X-Documenso-Secret"] = "shh";
				controller.HttpContext.Request.Headers["X-Documenso-Event-Id"] = "evt-77";
				controller.HttpContext.Request.Body = new System.IO.MemoryStream(Encoding.UTF8.GetBytes("{\"event\":\"DOCUMENT_SIGNED\",\"payload\":{\"id\":\"envelope_1\",\"externalId\":\"resgrid:11:4\"}}"));

				(await controller.Documenso(CancellationToken.None)).Should().BeOfType<OkResult>();

				webhooks.Verify(x => x.RecordHintAsync(ESignProviderKinds.Documenso, "evt-77", "DOCUMENT_SIGNED", "resgrid:11:4", It.IsAny<CancellationToken>()), Times.Once);
			}
			finally
			{
				ESignConfig.WebhookAllowedNetworks = allowed;
				ESignConfig.WebhookSecret = secret;
			}
		}

		[Test]
		public async Task Security_review_L3_a_blank_shared_secret_refuses_every_hint_even_from_the_cluster()
		{
			var allowed = ESignConfig.WebhookAllowedNetworks;
			var secret = ESignConfig.WebhookSecret;
			try
			{
				ESignConfig.WebhookAllowedNetworks = "10.42.0.0/16";
				ESignConfig.WebhookSecret = "  ";
				var webhooks = new Mock<IESignWebhookService>(MockBehavior.Strict);
				var controller = new ESignWebhooksController(webhooks.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
				controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.42.3.3");
				controller.HttpContext.Request.Headers["X-Documenso-Event-Id"] = "evt-78";
				controller.HttpContext.Request.Body = new System.IO.MemoryStream(Encoding.UTF8.GetBytes("{\"event\":\"DOCUMENT_SIGNED\",\"payload\":{\"id\":\"envelope_1\"}}"));

				(await controller.Documenso(CancellationToken.None)).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403, "fail closed, like a blank network list");
				webhooks.VerifyNoOtherCalls();
			}
			finally
			{
				ESignConfig.WebhookAllowedNetworks = allowed;
				ESignConfig.WebhookSecret = secret;
			}
		}

		[Test]
		public async Task A_wrong_shared_secret_is_refused_even_from_the_cluster()
		{
			var allowed = ESignConfig.WebhookAllowedNetworks;
			var secret = ESignConfig.WebhookSecret;
			try
			{
				ESignConfig.WebhookAllowedNetworks = "10.42.0.0/16";
				ESignConfig.WebhookSecret = "shh";
				var controller = new ESignWebhooksController(new Mock<IESignWebhookService>(MockBehavior.Strict).Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
				controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.42.3.3");
				controller.HttpContext.Request.Headers["X-Documenso-Secret"] = "guess";

				(await controller.Documenso(CancellationToken.None)).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(401);
			}
			finally
			{
				ESignConfig.WebhookAllowedNetworks = allowed;
				ESignConfig.WebhookSecret = secret;
			}
		}

		[Test]
		public void A_redelivered_hint_without_an_event_id_gets_the_same_derived_id()
		{
			const string body = "{\"event\":\"DOCUMENT_COMPLETED\",\"createdAt\":\"2026-11-24T15:00:00Z\",\"payload\":{\"id\":\"envelope_1\"}}";
			var first = ESignWebhooksController.ParseHint(body, null);
			var again = ESignWebhooksController.ParseHint(body, "");

			first.EventId.Should().HaveLength(64).And.Be(again.EventId);
			first.EnvelopeKey.Should().Be("envelope_1");
			ESignWebhooksController.ParseHint("not json", null).EnvelopeKey.Should().BeNull();
			ESignWebhookPolicy.IdFor(ESignProviderKinds.Documenso, "evt-1").Should().Be("1:evt-1");
		}

		#endregion

		#region Worker 90

		[Test]
		public async Task Worker_90_runs_every_step_and_one_failing_step_does_not_stop_the_others()
		{
			var requests = new Mock<ISignatureRequestService>();
			requests.Setup(x => x.ProcessWebhookEventsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("db"));
			requests.Setup(x => x.ReconcileDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
			requests.Setup(x => x.ExpireDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
			var sealing = new Mock<IDocumentSealingService>();
			sealing.Setup(x => x.RetryPendingAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);
			sealing.Setup(x => x.RetimestampDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
			var webhooks = new Mock<IESignWebhookService>();
			webhooks.Setup(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);

			var result = await SignaturesLogic.Process(requests.Object, sealing.Object, webhooks.Object, DateTime.UtcNow);

			result.Hints.Should().Be(0);
			result.Reconciled.Should().Be(2);
			result.Expired.Should().Be(1);
			result.Sealed.Should().Be(3);
			result.Purged.Should().Be(4);
		}

		#endregion
	}
}
