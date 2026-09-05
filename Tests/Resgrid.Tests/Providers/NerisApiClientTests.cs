using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Config;
using Resgrid.Providers.Neris;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Contract fixtures for the NERIS client (RMS plan section 5.5: validate, create, update, status, rejection,
	/// throttling, retry classification) against a scripted handler, so the wire protocol of the pinned contract —
	/// form token, bearer header, 201/204/422/429/500 replies — is exercised without the network.
	/// </summary>
	[TestFixture]
	public class NerisApiClientTests
	{
		private ScriptedHandler _handler;
		private NerisApiClient _client;
		private RmsNerisProfile _profile;
		private NerisCredential _credential;

		[SetUp]
		public void SetUp()
		{
			_handler = new ScriptedHandler();
			_client = new NerisApiClient(new HttpClient(_handler));
			// A fresh RowVersion per test keeps the static token cache from bleeding between tests.
			_profile = NerisMappingTests.Profile();
			_profile.RmsNerisProfileId = Guid.NewGuid().ToString();
			_credential = new NerisCredential { ClientId = "client", ClientSecret = "secret" };
			_handler.Token(HttpStatusCode.OK, "{\"access_token\":\"tok-1\",\"expires_in\":3600,\"token_type\":\"bearer\"}");
		}

		[Test]
		public async Task Create_sends_the_payload_verbatim_with_a_bearer_token_and_reads_the_incident_id()
		{
			_handler.Reply("POST", "/v1/incident/FD24027000", HttpStatusCode.Created, "{\"neris_id\":\"FD24027000I2026000123\",\"incident_status\":{\"status\":\"SUBMITTED\",\"last_modified\":\"2026-09-03T14:00:00Z\",\"created_by\":\"api\"}}");

			var outcome = await _client.CreateIncidentAsync(_profile, _credential, "{\"base\":{}}");

			outcome.Kind.Should().Be(NerisOutcomeKind.Created);
			outcome.ExternalId.Should().Be("FD24027000I2026000123");
			outcome.ExternalStatus.Should().Be("SUBMITTED");
			outcome.StatusCode.Should().Be(201);
			outcome.ResponseJson.Should().Contain("neris_id");

			var token = _handler.Requests[0];
			token.Method.Should().Be("POST");
			token.Path.Should().Be("/v1/token");
			// A client-credentials integration account authenticates with HTTP Basic. The pinned contract's TokenBody
			// declares username/password for the password and MFA flows only, so sending the id and secret there is
			// rejected by the destination.
			token.Body.Should().Contain("grant_type=client_credentials");
			token.Body.Should().NotContain("username").And.NotContain("password=");
			token.Authorization.Should().Be("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("client:secret")));

			var create = _handler.Requests[1];
			create.Authorization.Should().Be("Bearer tok-1");
			create.Body.Should().Be("{\"base\":{}}");
			create.ContentType.Should().StartWith("application/json");
			_handler.Requests.Should().OnlyContain(r => r.UserAgent.Contains("Resgrid-RMS/"), "NERIS requires User-Agent on token and data requests");
		}

		[Test]
		public async Task The_token_is_reused_until_it_expires()
		{
			_handler.Reply("POST", "/v1/incident/FD24027000/validate", HttpStatusCode.NoContent, "");

			(await _client.ValidateAsync(_profile, _credential, "{}")).Kind.Should().Be(NerisOutcomeKind.Accepted);
			(await _client.ValidateAsync(_profile, _credential, "{}")).Kind.Should().Be(NerisOutcomeKind.Accepted);

			_handler.Requests.Should().HaveCount(3, "one token call, two validate calls");
		}

		[TestCase(202, false)]
		[TestCase(204, false)]
		[TestCase(201, false)]
		[TestCase(202, true)]
		[TestCase(204, true)]
		[TestCase(201, true)]
		public async Task A_successful_create_without_receipt_ID_requires_reconciliation(int status, bool analysis)
		{
			var path = analysis ? "/v1/incident_analysis/incident-1" : "/v1/incident/FD24027000";
			_handler.Reply("POST", path, (HttpStatusCode)status, "{\"incident_status\":{\"status\":\"REJECTED\"},\"incident_analysis_status\":{\"status\":\"REJECTED\"}}");
			var result = analysis ? await _client.CreateIncidentAnalysisAsync(_profile, _credential, "incident-1", "{}")
				: await _client.CreateIncidentAsync(_profile, _credential, "{}");
			result.DeliveryUncertain.Should().BeTrue();
		}

		[Test]
		public async Task A_422_becomes_a_rejection_with_field_paths_and_no_payload_echo()
		{
			_handler.Reply("POST", "/v1/incident/FD24027000", (HttpStatusCode)422,
				"{\"detail\":[{\"loc\":[\"body\",\"dispatch\",\"call_answered\"],\"msg\":\"Field required\",\"type\":\"missing\"},{\"loc\":[\"body\",\"incident_types\",0,\"type\"],\"msg\":\"Input should be a valid enumeration member\",\"type\":\"enum\"}]}");

			var outcome = await _client.CreateIncidentAsync(_profile, _credential, "{\"base\":{}}");

			outcome.Kind.Should().Be(NerisOutcomeKind.Rejected);
			outcome.Errors.Should().HaveCount(2);
			outcome.Errors[0].Code.Should().Be("missing");
			outcome.Errors[0].FieldPath.Should().Be("dispatch.call_answered");
			outcome.Errors[1].FieldPath.Should().Be("incident_types.0.type");
			outcome.Message.Should().Be("2 validation issue(s).");
		}

		[TestCase(429)]
		[TestCase(500)]
		[TestCase(503)]
		public async Task Throttling_and_server_errors_are_transient(int status)
		{
			_handler.Reply("POST", "/v1/incident/FD24027000", (HttpStatusCode)status, "{\"error\":\"busy\"}");

			var outcome = await _client.CreateIncidentAsync(_profile, _credential, "{}");

			outcome.Kind.Should().Be(NerisOutcomeKind.Transient);
			outcome.StatusCode.Should().Be(status);
		}

		[Test]
		public async Task A_refused_credential_is_fatal_and_drops_the_cached_token()
		{
			_handler.Reply("POST", "/v1/incident/FD24027000", HttpStatusCode.Unauthorized, "{\"detail\":\"expired\"}");

			var outcome = await _client.CreateIncidentAsync(_profile, _credential, "{}");
			outcome.Kind.Should().Be(NerisOutcomeKind.Fatal);

			_handler.Reply("POST", "/v1/incident/FD24027000", HttpStatusCode.Created, "{\"neris_id\":\"X\",\"incident_status\":{\"status\":\"SUBMITTED\"}}");
			await _client.CreateIncidentAsync(_profile, _credential, "{}");
			_handler.Requests.FindAll(r => r.Path == "/v1/token").Should().HaveCount(2, "the token was fetched again after the refusal");
		}

		[Test]
		public async Task A_token_refusal_is_fatal_without_calling_the_incident_endpoint()
		{
			_handler.Token(HttpStatusCode.OK, "{\"error\":\"invalid_client\"}");

			var outcome = await _client.CreateIncidentAsync(_profile, _credential, "{}");

			outcome.Kind.Should().Be(NerisOutcomeKind.Fatal);
			outcome.Message.Should().Contain("invalid_client");
			_handler.Requests.Should().OnlyContain(r => r.Path == "/v1/token");
		}

		[Test]
		public async Task Update_and_status_map_destination_states()
		{
			_handler.Reply("PUT", "/v1/incident/FD24027000/FD24027000I1", HttpStatusCode.OK, "{\"last_modified\":\"2026-09-03T15:00:00Z\"}");
			_handler.Reply("GET", "/v1/incident/FD24027000/FD24027000I1", HttpStatusCode.OK, "{\"neris_id\":\"FD24027000I1\",\"incident_status\":{\"status\":\"APPROVED\",\"last_modified\":\"x\",\"created_by\":\"y\"}}");

			(await _client.UpdateIncidentAsync(_profile, _credential, "FD24027000I1", "{}")).Kind.Should().Be(NerisOutcomeKind.Updated);

			var status = await _client.GetStatusAsync(_profile, _credential, "FD24027000I1");
			status.Kind.Should().Be(NerisOutcomeKind.Accepted);
			status.ExternalStatus.Should().Be("APPROVED");

			_handler.Reply("GET", "/v1/incident/FD24027000/FD24027000I1", HttpStatusCode.OK, "{\"incident_status\":{\"status\":\"REJECTED\"}}");
			var rejected = await _client.GetStatusAsync(_profile, _credential, "FD24027000I1");
			rejected.Kind.Should().Be(NerisOutcomeKind.Rejected);
			rejected.Errors.Should().ContainSingle(e => e.Code == "REJECTED");

			_handler.Reply("GET", "/v1/incident/FD24027000/FD24027000I1", HttpStatusCode.OK, "{\"incident_status\":{\"status\":\"PENDING_APPROVAL\"}}");
			(await _client.GetStatusAsync(_profile, _credential, "FD24027000I1")).Kind.Should().Be(NerisOutcomeKind.Pending);
		}

		[Test]
		public async Task A_missing_profile_or_credential_is_fatal_before_any_call()
		{
			(await _client.CreateIncidentAsync(new RmsNerisProfile { DepartmentId = 4 }, _credential, "{}")).Kind.Should().Be(NerisOutcomeKind.Fatal);
			(await _client.CreateIncidentAsync(_profile, null, "{}")).Kind.Should().Be(NerisOutcomeKind.Fatal);
			_handler.Requests.Should().BeEmpty();
		}

		[TestCase("APPROVED", NerisOutcomeKind.Accepted)]
		[TestCase("REJECTED", NerisOutcomeKind.Rejected)]
		[TestCase("PENDING_APPROVAL", NerisOutcomeKind.Pending)]
		public async Task Analysis_status_uses_the_query_identifier_and_analysis_status_block(string state, NerisOutcomeKind expected)
		{
			const string id = "IA|FD24027000|incident:42|1729023498";
			var path = "/v1/incident_analysis/FD24027000?neris_id_ia=" + Uri.EscapeDataString(id);
			_handler.Reply("GET", path, HttpStatusCode.OK, "{\"incident_analysis_status\":{\"status\":\"" + state + "\"},\"incident_status\":{\"status\":\"FAILED\"}}");
			var outcome = await _client.GetIncidentAnalysisStatusAsync(_profile, _credential, id);
			outcome.Kind.Should().Be(expected);
			outcome.ExternalId.Should().Be(id);
			outcome.ExternalStatus.Should().Be(state);
			_handler.Requests[1].Path.Should().Be(path);
		}

		[Test]
		public async Task Analysis_create_and_update_use_their_distinct_contract_routes()
		{
			_handler.Reply("POST", "/v1/incident_analysis/incident-1", HttpStatusCode.Created, "{\"neris_id\":\"analysis-1\",\"incident_analysis_status\":{\"status\":\"SUBMITTED\"}}");
			_handler.Reply("PUT", "/v1/incident_analysis/FD24027000/analysis-1", HttpStatusCode.NoContent, "");
			var created = await _client.CreateIncidentAnalysisAsync(_profile, _credential, "incident-1", "{}");
			created.Kind.Should().Be(NerisOutcomeKind.Created);
			created.ExternalId.Should().Be("analysis-1");
			(await _client.UpdateIncidentAnalysisAsync(_profile, _credential, created.ExternalId, "{}")).Kind.Should().Be(NerisOutcomeKind.Updated);
			_handler.Requests.Should().OnlyContain(r => r.UserAgent.Contains("Resgrid-RMS/"));
		}

		[TestCase(null)]
		[TestCase("https://api.neris.fsri.org/v1")]
		[TestCase("https://API.NERIS.FSRI.ORG:443/test/")]
		[TestCase("http://neris.test/v1")]
		[TestCase("https://user:password@neris.test/v1")]
		public async Task Sandbox_misconfiguration_fails_before_credentials_or_payload_leave_the_process(string endpoint)
		{
			var previous = NerisConfig.SandboxBaseUrl;
			try
			{
				NerisConfig.SandboxBaseUrl = "";
				_profile.BaseUrlOverride = endpoint;
				(await _client.CreateIncidentAsync(_profile, _credential, "{}")).Kind.Should().Be(NerisOutcomeKind.Fatal);
				_handler.Requests.Should().BeEmpty();
			}
			finally { NerisConfig.SandboxBaseUrl = previous; }
		}

		public sealed class RecordedRequest
		{
			public string Method { get; set; }
			public string Path { get; set; }
			public string Body { get; set; }
			public string Authorization { get; set; }
			public string ContentType { get; set; }
			public string UserAgent { get; set; }
		}

		/// <summary>Replies keyed by "METHOD path"; the token endpoint has its own script.</summary>
		private sealed class ScriptedHandler : HttpMessageHandler
		{
			private readonly Dictionary<string, (HttpStatusCode status, string body)> _replies = new Dictionary<string, (HttpStatusCode, string)>(StringComparer.OrdinalIgnoreCase);
			public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

			public void Token(HttpStatusCode status, string body) => _replies["POST /v1/token"] = (status, body);
			public void Reply(string method, string path, HttpStatusCode status, string body) => _replies[$"{method} {path}"] = (status, body);

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
				Requests.Add(new RecordedRequest
				{
					Method = request.Method.Method,
					Path = request.RequestUri.PathAndQuery,
					Body = body,
					Authorization = request.Headers.Authorization?.ToString(),
					ContentType = request.Content?.Headers.ContentType?.ToString(),
					UserAgent = request.Headers.UserAgent.ToString()
				});

				if (!_replies.TryGetValue($"{request.Method.Method} {request.RequestUri.PathAndQuery}", out var reply))
					return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\":\"not scripted\"}", Encoding.UTF8, "application/json") };

				return new HttpResponseMessage(reply.status) { Content = new StringContent(reply.body ?? string.Empty, Encoding.UTF8, "application/json") };
			}
		}
	}
}
