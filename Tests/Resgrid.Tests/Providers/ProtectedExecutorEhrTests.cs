using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Workflow.Executors;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The protected HTTP executor for EHR integration: success rules (HL7 ACK, FHIR OperationOutcome, JSON path),
	/// response capture, the response size cap, the idempotency and If-None-Exist headers, and OAuth2 private_key_jwt.
	/// </summary>
	[TestFixture]
	public class ProtectedExecutorEhrTests
	{
		private const string Host = "ehr.example.org";
		private const string Url = "https://ehr.example.org/fhir/r4";
		private const string TokenUrl = "https://ehr.example.org/oauth2/token";
		private const string Payload = "{\"resourceType\":\"Bundle\"}";

		private sealed class RecordingHandler : HttpMessageHandler
		{
			private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
			public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
			public List<string> Bodies { get; } = new List<string>();

			public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Requests.Add(request);
				Bodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
				return _respond(request);
			}
		}

		[SetUp]
		public void SetUp() => HttpApiExecutor.ClearTokenCache();

		private static (HttpApiExecutor Executor, RecordingHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			var handler = new RecordingHandler(respond);
			return (new HttpApiExecutor(_ => handler, url => Task.FromResult((true, (string)null))), handler);
		}

		private static WorkflowActionContext Context(object options, string credentialJson = "{\"token\":\"tok-1\"}",
			int credentialType = (int)WorkflowCredentialType.HttpBearer, string pinnedAuthMethod = null, string idempotencyKey = "0123456789abcdef0123456789abcdef")
		{
			var config = JObject.FromObject(new { Url, ContentType = "application/fhir+json" });
			if (options != null)
				config.Merge(JObject.FromObject(options));
			return new WorkflowActionContext
			{
				RenderedContent = Payload,
				ActionConfigJson = config.ToString(Formatting.None),
				DecryptedCredentialJson = credentialJson,
				CredentialType = credentialType,
				ActionType = (int)WorkflowActionType.CallApiPost,
				ProtectedMode = true,
				PinnedHost = Host,
				PinnedTokenHost = credentialType == (int)WorkflowCredentialType.OAuth2ClientCredentials ? Host : null,
				PinnedAuthMethod = pinnedAuthMethod,
				IdempotencyKey = idempotencyKey
			};
		}

		private static HttpResponseMessage Respond(HttpStatusCode status, string body = "", string mediaType = "application/json", Action<HttpResponseMessage> headers = null)
		{
			var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
			headers?.Invoke(response);
			return response;
		}

		private const string Hl7Ack = "MSH|^~\\&|EHR|FAC|RESGRID|DEPT|20260924120000||ACK^T02^ACK|99|P|2.5.1\rMSA|{0}|0123456789abcdef0123456789abcdef|note\r";

		// ── Success rules ───────────────────────────────────────────────────────────────────────────

		[TestCase("AA", true)]
		[TestCase("CA", true)]
		[TestCase("AE", false)]
		[TestCase("AR", false)]
		public async Task an_hl7_acknowledgement_decides_success(string code, bool accepted)
		{
			var (executor, _) = Build(_ => Respond(HttpStatusCode.OK, string.Format(Hl7Ack, code), "x-application/hl7-v2+er7"));

			var result = await executor.ExecuteAsync(Context(new { ContentType = "x-application/hl7-v2+er7", SuccessRule = new { Type = "hl7_ack" } }), CancellationToken.None);

			result.Success.Should().Be(accepted);
			if (!accepted)
			{
				result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedAck);
				result.ErrorDetail.Should().Be($"ack_rejected: rule=hl7_ack ack={code}");
				ProtectedWorkflowRetryPolicy.IsRetryable(result.ProtectedOutcome, result.HttpStatus).Should().BeFalse("an AE or AR is a data problem: stop and alert");
			}
		}

		[Test]
		public async Task a_fhir_operation_outcome_error_is_a_rejection_even_inside_a_transaction_response()
		{
			const string bundle = "{\"resourceType\":\"Bundle\",\"type\":\"transaction-response\",\"entry\":[{\"response\":{\"status\":\"400\",\"outcome\":" +
				"{\"resourceType\":\"OperationOutcome\",\"issue\":[{\"severity\":\"error\",\"code\":\"invalid\",\"diagnostics\":\"SECRET-ECHO\"}]}}}]}";
			var (executor, _) = Build(_ => Respond(HttpStatusCode.OK, bundle));

			var result = await executor.ExecuteAsync(Context(new { SuccessRule = new { Type = "fhir_operation_outcome" } }), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedAck);
			result.ErrorDetail.Should().NotContain("SECRET-ECHO", "response content never reaches the run log");

			var (fine, _) = Build(_ => Respond(HttpStatusCode.OK, "{\"resourceType\":\"OperationOutcome\",\"issue\":[{\"severity\":\"information\"}]}"));
			(await fine.ExecuteAsync(Context(new { SuccessRule = new { Type = "fhir_operation_outcome" } }), CancellationToken.None)).Success.Should().BeTrue();
		}

		[Test]
		public async Task a_json_path_mismatch_is_a_rejection()
		{
			var (executor, _) = Build(_ => Respond(HttpStatusCode.OK, "{\"status\":\"queued\"}"));

			var result = await executor.ExecuteAsync(Context(new { SuccessRule = new { Type = "json_path", Path = "$.status", Expected = "ok" } }), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedAck);

			var (ok, _) = Build(_ => Respond(HttpStatusCode.OK, "{\"status\":\"ok\"}"));
			(await ok.ExecuteAsync(Context(new { SuccessRule = new { Type = "json_path", Path = "$.status", Expected = "ok" } }), CancellationToken.None)).Success.Should().BeTrue();
		}

		[Test]
		public async Task an_xpath_rule_reads_namespaced_xml_and_refuses_dtds()
		{
			const string soap = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><r xmlns=\"urn:ehr\"><status>accepted</status></r></s:Body></s:Envelope>";
			var (executor, _) = Build(_ => Respond(HttpStatusCode.OK, soap, "application/soap+xml"));
			var options = new { ContentType = "application/soap+xml", SuccessRule = new { Type = "xpath", Path = "//*[local-name()='status']", Expected = "accepted" } };

			(await executor.ExecuteAsync(Context(options), CancellationToken.None)).Success.Should().BeTrue();

			var (dtd, _) = Build(_ => Respond(HttpStatusCode.OK, "<!DOCTYPE r [<!ENTITY s \"accepted\">]><r><status>&s;</status></r>", "application/xml"));
			(await dtd.ExecuteAsync(Context(options), CancellationToken.None)).ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedAck);
		}

		[TestCase(HttpStatusCode.ServiceUnavailable, true)]
		[TestCase((HttpStatusCode)429, true)]
		[TestCase(HttpStatusCode.BadRequest, false)]
		[TestCase(HttpStatusCode.Conflict, false)]
		public async Task only_5xx_and_429_are_retryable(HttpStatusCode status, bool retryable)
		{
			var (executor, _) = Build(_ => Respond(status, "{}"));

			var result = await executor.ExecuteAsync(Context(new { SuccessRule = new { Type = "fhir_operation_outcome" } }), CancellationToken.None);

			result.Success.Should().BeFalse();
			ProtectedWorkflowRetryPolicy.IsRetryable(result.ProtectedOutcome, result.HttpStatus).Should().Be(retryable);
		}

		// ── Capture ─────────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task values_are_captured_from_headers_json_hl7_and_fhir_locations()
		{
			var (fhir, _) = Build(_ => Respond(HttpStatusCode.OK,
				"{\"resourceType\":\"Bundle\",\"entry\":[{\"response\":{\"location\":\"Observation/77/_history/1\"}},{\"response\":{\"location\":\"Encounter/E-551/_history/1\"}}]}",
				headers: r => r.Headers.Add("X-Correlation", "corr-9")));
			var fhirResult = await fhir.ExecuteAsync(Context(new
			{
				ResponseCapture = new object[]
				{
					new { Source = "fhir_location_id", Expression = "Encounter", Key = "ehr_encounter_id" },
					new { Source = "header", Expression = "X-Correlation", Key = "ehr_correlation" },
					new { Source = "json_path", Expression = "$.entry[0].response.location", Key = "first_location" }
				}
			}), CancellationToken.None);

			fhirResult.Success.Should().BeTrue();
			fhirResult.CapturedValues.Should().BeEquivalentTo(new Dictionary<string, string>
			{
				["ehr_encounter_id"] = "E-551",
				["ehr_correlation"] = "corr-9",
				["first_location"] = "Observation/77/_history/1"
			});
			fhirResult.ResultMessage.Should().Be("HTTP 200 OK", "captured values never go into the result message");

			var (created, _) = Build(_ => Respond(HttpStatusCode.Created, "", headers: r => r.Headers.Location = new Uri("https://ehr.example.org/fhir/r4/Encounter/12345/_history/1")));
			(await created.ExecuteAsync(Context(new { ResponseCapture = new[] { new { Source = "fhir_location_id", Key = "ehr_encounter_id" } } }), CancellationToken.None))
				.CapturedValues["ehr_encounter_id"].Should().Be("12345");

			var (hl7, _) = Build(_ => Respond(HttpStatusCode.OK, string.Format(Hl7Ack, "AA"), "x-application/hl7-v2+er7"));
			var hl7Result = await hl7.ExecuteAsync(Context(new
			{
				ContentType = "x-application/hl7-v2+er7",
				SuccessRule = new { Type = "hl7_ack" },
				ResponseCapture = new[] { new { Source = "hl7_field", Expression = "MSA-2", Key = "ehr_message_id" }, new { Source = "hl7_field", Expression = "MSH-10", Key = "ehr_ack_id" } }
			}), CancellationToken.None);
			hl7Result.CapturedValues["ehr_message_id"].Should().Be("0123456789abcdef0123456789abcdef");
			hl7Result.CapturedValues["ehr_ack_id"].Should().Be("99");
		}

		[Test]
		public async Task a_missing_capture_is_reported_by_key_only()
		{
			var (executor, _) = Build(_ => Respond(HttpStatusCode.OK, "{\"status\":\"ok\"}"));

			var result = await executor.ExecuteAsync(Context(new { ResponseCapture = new[] { new { Source = "json_path", Expression = "$.id", Key = "ehr_encounter_id" } } }), CancellationToken.None);

			result.Success.Should().BeTrue();
			result.CapturedValues.Should().BeNull();
			result.ResultMessage.Should().Be("HTTP 200 OK capture_missing=[ehr_encounter_id]");
		}

		[Test]
		public async Task a_response_over_the_cap_fails_without_being_read_into_anything()
		{
			var big = new string('x', Resgrid.Config.DataProtectionConfig.ProtectedWorkflowMaxResponseBytes + 1);
			var (executor, _) = Build(_ => Respond(HttpStatusCode.OK, "{\"id\":\"" + big + "\"}"));

			var result = await executor.ExecuteAsync(Context(new { ResponseCapture = new[] { new { Source = "json_path", Expression = "$.id", Key = "ehr_encounter_id" } } }), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedResponseTooLarge);
			result.ErrorDetail.Should().Be(ProtectedWorkflowErrorCodes.ResponseTooLarge);
			result.CapturedValues.Should().BeNull();
		}

		[Test]
		public async Task without_a_rule_or_capture_the_body_is_never_read()
		{
			var (executor, _) = Build(_ => Respond(HttpStatusCode.OK, new string('x', Resgrid.Config.DataProtectionConfig.ProtectedWorkflowMaxResponseBytes * 2)));

			(await executor.ExecuteAsync(Context(null), CancellationToken.None)).Success.Should().BeTrue("an oversized body is only a problem when it has to be read");
		}

		// ── Idempotency ─────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task the_idempotency_key_and_if_none_exist_travel_as_headers()
		{
			var (executor, handler) = Build(_ => Respond(HttpStatusCode.Created));

			await executor.ExecuteAsync(Context(new
			{
				IdempotencyHeader = "Idempotency-Key",
				IfNoneExist = "identifier=https://resgrid.com/call|1001",
				Headers = new Dictionary<string, string> { ["Idempotency-Key"] = "overridden?", ["If-None-Exist"] = "overridden?" }
			}), CancellationToken.None);

			var request = handler.Requests.Single();
			request.Headers.GetValues("Idempotency-Key").Should().Equal("0123456789abcdef0123456789abcdef");
			request.Headers.GetValues("If-None-Exist").Should().Equal("identifier=https://resgrid.com/call|1001");
			request.Content.Headers.ContentType.MediaType.Should().Be("application/fhir+json");
		}

		[Test]
		public async Task a_content_type_outside_the_allowlist_is_refused_before_sending()
		{
			var (executor, handler) = Build(_ => Respond(HttpStatusCode.OK));

			var result = await executor.ExecuteAsync(Context(new { ContentType = "text/html" }), CancellationToken.None);

			handler.Requests.Should().BeEmpty();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedValidation);
		}

		// ── OAuth2 private_key_jwt ──────────────────────────────────────────────────────────────────

		[TestCase(WorkflowJwtKeys.Rs384)]
		[TestCase(WorkflowJwtKeys.Es384)]
		public async Task private_key_jwt_sends_a_signed_client_assertion_instead_of_a_secret(string alg)
		{
			var (signing, published) = WorkflowJwtKeys.Generate(alg, DateTime.UtcNow);
			var credential = JsonConvert.SerializeObject(new
			{
				tokenUrl = TokenUrl, clientId = "resgrid-client", scope = "system/Encounter.write system/Observation.write",
				authMethod = "private_key_jwt", signingKeys = new[] { signing }
			});
			var (executor, handler) = Build(request => request.RequestUri.AbsolutePath.EndsWith("/token")
				? Respond(HttpStatusCode.OK, "{\"access_token\":\"at-1\",\"expires_in\":300}")
				: Respond(HttpStatusCode.Created));

			var result = await executor.ExecuteAsync(Context(null, credential, (int)WorkflowCredentialType.OAuth2ClientCredentials, "private_key_jwt"), CancellationToken.None);

			result.Success.Should().BeTrue(result.ErrorDetail);
			var form = handler.Bodies[0].Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
			form["grant_type"].Should().Be("client_credentials");
			form["client_assertion_type"].Should().Be(WorkflowJwtKeys.AssertionType);
			form["scope"].Should().Be("system/Encounter.write system/Observation.write");
			form.Should().NotContainKey("client_secret");

			var assertion = form["client_assertion"];
			WorkflowJwtKeys.Verify(assertion, published.Jwk).Should().BeTrue("the assertion is signed with the published key");
			var parts = assertion.Split('.');
			var header = JObject.Parse(Encoding.UTF8.GetString(WorkflowJwtKeys.FromBase64Url(parts[0])));
			var claims = JObject.Parse(Encoding.UTF8.GetString(WorkflowJwtKeys.FromBase64Url(parts[1])));
			header.Value<string>("alg").Should().Be(alg);
			header.Value<string>("kid").Should().Be(signing.Kid);
			claims.Value<string>("iss").Should().Be("resgrid-client");
			claims.Value<string>("sub").Should().Be("resgrid-client");
			claims.Value<string>("aud").Should().Be(TokenUrl);
			(claims.Value<long>("exp") - claims.Value<long>("iat")).Should().BeInRange(1, 300);
			claims.Value<string>("jti").Should().NotBeNullOrWhiteSpace();
			handler.Requests[1].Headers.Authorization.Parameter.Should().Be("at-1");
		}

		[Test]
		public async Task an_auth_method_other_than_the_pinned_one_is_refused()
		{
			var (executor, handler) = Build(_ => Respond(HttpStatusCode.OK));
			var credential = JsonConvert.SerializeObject(new { tokenUrl = TokenUrl, clientId = "c", clientSecret = "s", authMethod = "client_secret" });

			var result = await executor.ExecuteAsync(Context(null, credential, (int)WorkflowCredentialType.OAuth2ClientCredentials, "private_key_jwt"), CancellationToken.None);

			handler.Requests.Should().BeEmpty("the secret goes nowhere when the approval pinned a signed assertion");
			result.ErrorDetail.Should().Be(ProtectedWorkflowErrorCodes.AuthMethodMismatch);
		}
	}
}
