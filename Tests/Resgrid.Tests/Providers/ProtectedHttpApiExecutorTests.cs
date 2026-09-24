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
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Workflow.Executors;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// HttpApiExecutor in Protected Workflow mode: pinned host re-checked on the rendered URL, no redirects, status line
	/// only (the body is never read), the payload hash of the exact bytes, the OAuth2 token host pinned too, and the
	/// auth scheme inferred from the credential type.
	/// </summary>
	[TestFixture]
	public class ProtectedHttpApiExecutorTests
	{
		private const string Host = "org.crm.dynamics.com";
		private const string Url = "https://org.crm.dynamics.com/api/data/v9.2/incidents(1)";
		private const string Payload = "{\"closure\":\"SENTINEL-EXEC-4471\"}";

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

		private static (HttpApiExecutor Executor, RecordingHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			var handler = new RecordingHandler(respond);
			var executor = new HttpApiExecutor(_ => handler, url => Task.FromResult((true, (string)null)));
			return (executor, handler);
		}

		private static WorkflowActionContext Context(string url = Url, string pinned = Host, int credentialType = (int)WorkflowCredentialType.HttpBearer,
			string credentialJson = "{\"token\":\"tok-1\"}", string pinnedTokenHost = null, int actionType = (int)WorkflowActionType.CallApiPut,
			string pinnedAuthMethod = WorkflowJwtKeys.ClientSecret, object extraConfig = null) =>
			new WorkflowActionContext
			{
				RenderedContent = Payload,
				ActionConfigJson = ConfigJson(url, extraConfig),
				DecryptedCredentialJson = credentialJson,
				CredentialType = credentialType,
				ActionType = actionType,
				ProtectedMode = true,
				PinnedHost = pinned,
				PinnedTokenHost = pinnedTokenHost,
				PinnedAuthMethod = credentialType == (int)WorkflowCredentialType.OAuth2ClientCredentials ? pinnedAuthMethod : null
			};

		private static string ConfigJson(string url, object extra)
		{
			var config = Newtonsoft.Json.Linq.JObject.FromObject(new { Url = url, Headers = new Dictionary<string, string> { ["Host"] = "attacker.example", ["Prefer"] = "return=minimal" } });
			if (extra != null)
				config.Merge(Newtonsoft.Json.Linq.JObject.FromObject(extra));
			return config.ToString(Formatting.None);
		}

		[SetUp]
		public void SetUp() => HttpApiExecutor.ClearTokenCache();

		[Test]
		public async Task a_successful_send_reports_the_status_line_and_the_hash_of_the_exact_bytes()
		{
			var (executor, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent("echo " + Payload) });

			var result = await executor.ExecuteAsync(Context(), CancellationToken.None);

			result.Success.Should().BeTrue();
			result.ResultMessage.Should().Be("HTTP 204 No Content");
			result.HttpStatus.Should().Be(204);
			result.PayloadBytes.Should().Be(Encoding.UTF8.GetByteCount(Payload));
			result.PayloadSha256.Should().Be(ProtectedWorkflowDisclosureChain.Sha256Hex(Encoding.UTF8.GetBytes(Payload)));
			handler.Bodies.Single().Should().Be(Payload);
			handler.Requests.Single().Method.Should().Be(HttpMethod.Put);
			handler.Requests.Single().Headers.Authorization.ToString().Should().Be("Bearer tok-1", "the auth scheme comes from the credential type");
			handler.Requests.Single().Headers.Contains("Host").Should().BeFalse("a configured Host header could re-route the request");
			handler.Requests.Single().Headers.Contains("Prefer").Should().BeTrue();
		}

		[Test]
		public async Task a_failure_never_carries_the_response_body()
		{
			var (executor, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("invalid: " + Payload) });

			var result = await executor.ExecuteAsync(Context(), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedHttp);
			(result.ResultMessage + result.ErrorDetail).Should().NotContain("SENTINEL-EXEC-4471");
			result.ResultMessage.Should().Be("HTTP 400 Bad Request");
		}

		[TestCase(HttpStatusCode.Found)]
		[TestCase(HttpStatusCode.TemporaryRedirect)]
		[TestCase(HttpStatusCode.PermanentRedirect)]
		public async Task a_redirect_is_never_followed_and_is_blocked_host(HttpStatusCode status)
		{
			var (executor, handler) = Build(_ =>
			{
				var response = new HttpResponseMessage(status);
				response.Headers.Location = new Uri("https://attacker.example/collect");
				return response;
			});

			var result = await executor.ExecuteAsync(Context(), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedHost);
			result.HttpStatus.Should().Be((int)status);
			handler.Requests.Should().ContainSingle().Which.RequestUri.Host.Should().Be(Host);
		}

		[TestCase("https://attacker.example/api", ProtectedWorkflowErrorCodes.HostMismatch)]
		[TestCase("http://org.crm.dynamics.com/api", ProtectedWorkflowErrorCodes.SchemeNotHttps)]
		[TestCase("https://org.crm.dynamics.com.attacker.example/api", ProtectedWorkflowErrorCodes.HostMismatch)]
		[TestCase("https://user@org.crm.dynamics.com/api", ProtectedWorkflowErrorCodes.HostMismatch)]
		public async Task a_rendered_url_off_the_pinned_host_never_leaves(string url, string code)
		{
			var (executor, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));

			var result = await executor.ExecuteAsync(Context(url), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedHost);
			result.ErrorDetail.Should().Be(code);
			handler.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task only_post_and_put_are_allowed_and_basic_is_refused_by_default()
		{
			var (executor, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));

			(await executor.ExecuteAsync(Context(actionType: (int)WorkflowActionType.CallApiGet), CancellationToken.None)).ProtectedOutcome
				.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedRelease);
			(await executor.ExecuteAsync(Context(credentialType: (int)WorkflowCredentialType.HttpBasic, credentialJson: "{\"username\":\"u\",\"password\":\"p\"}"), CancellationToken.None))
				.ErrorDetail.Should().Be(ProtectedWorkflowErrorCodes.CredentialNotAllowed);
			handler.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task oauth2_tokens_are_fetched_from_the_pinned_token_host_and_cached()
		{
			var (executor, handler) = Build(request => request.RequestUri.Host == "login.microsoftonline.com"
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"at-1\",\"expires_in\":3600,\"token_type\":\"Bearer\"}") }
				: new HttpResponseMessage(HttpStatusCode.NoContent));
			var oauth = "{\"tokenUrl\":\"https://login.microsoftonline.com/tenant/oauth2/v2.0/token\",\"clientId\":\"c\",\"clientSecret\":\"s\",\"scope\":\"https://org.crm.dynamics.com/.default\"}";

			var first = await executor.ExecuteAsync(Context(credentialType: (int)WorkflowCredentialType.OAuth2ClientCredentials, credentialJson: oauth, pinnedTokenHost: "login.microsoftonline.com"), CancellationToken.None);
			var second = await executor.ExecuteAsync(Context(credentialType: (int)WorkflowCredentialType.OAuth2ClientCredentials, credentialJson: oauth, pinnedTokenHost: "login.microsoftonline.com"), CancellationToken.None);

			first.Success.Should().BeTrue();
			second.Success.Should().BeTrue();
			handler.Requests.Count(r => r.RequestUri.Host == "login.microsoftonline.com").Should().Be(1, "the token is reused until 60 seconds before it expires");
			handler.Bodies.First().Should().Contain("grant_type=client_credentials").And.Contain("scope=https");
			handler.Requests.Where(r => r.RequestUri.Host == Host).Should().OnlyContain(r => r.Headers.Authorization.ToString() == "Bearer at-1");
		}

		[Test]
		public async Task an_oauth2_token_url_off_the_pinned_token_host_is_blocked_before_the_secret_leaves()
		{
			var (executor, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));
			var oauth = "{\"tokenUrl\":\"https://attacker.example/token\",\"clientId\":\"c\",\"clientSecret\":\"s\"}";

			var result = await executor.ExecuteAsync(Context(credentialType: (int)WorkflowCredentialType.OAuth2ClientCredentials, credentialJson: oauth, pinnedTokenHost: "login.microsoftonline.com"), CancellationToken.None);

			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedHost);
			result.ErrorDetail.Should().Be(ProtectedWorkflowErrorCodes.TokenHostMismatch);
			handler.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task a_transport_failure_reports_a_code_and_type_without_the_payload()
		{
			var (executor, _) = Build(_ => throw new HttpRequestException("connection reset while sending " + Payload));

			var result = await executor.ExecuteAsync(Context(), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ProtectedOutcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedHttp);
			result.ErrorDetail.Should().StartWith(ProtectedWorkflowErrorCodes.HttpFailed + ": System.Net.Http.HttpRequestException");
			result.ErrorDetail.Should().NotContain("SENTINEL-EXEC-4471", "an exception message can quote the request, so it is never kept");
		}

		[Test]
		public void the_protected_handler_never_follows_redirects_and_requires_tls12_or_later()
		{
			using var handler = (SocketsHttpHandler)HttpApiExecutor.CreateHandler(true);

			handler.AllowAutoRedirect.Should().BeFalse();
			handler.UseCookies.Should().BeFalse();
			handler.SslOptions.EnabledSslProtocols.Should().Be(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13);
		}

		[Test]
		public void the_auth_scheme_is_inferred_from_the_credential_type_when_the_json_has_none()
		{
			HttpApiExecutor.ResolveAuthType(new HttpCredential { Token = "t" }, (int)WorkflowCredentialType.HttpBearer).Should().Be("bearer");
			HttpApiExecutor.ResolveAuthType(new HttpCredential { ApiKey = "k", HeaderName = "X" }, (int)WorkflowCredentialType.HttpApiKey).Should().Be("apikey");
			HttpApiExecutor.ResolveAuthType(new HttpCredential { ClientId = "c" }, (int)WorkflowCredentialType.OAuth2ClientCredentials).Should().Be("oauth2");
			HttpApiExecutor.ResolveAuthType(new HttpCredential { AuthType = "Basic" }, (int)WorkflowCredentialType.HttpBearer).Should().Be("basic");
		}
	}
}
