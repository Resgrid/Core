using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Providers.ProtectedData;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class OpenBaoTransitKeyWrappingProviderTests
	{
		private string _originalAddress;
		private string _originalMount;
		private string _originalKeyName;

		[SetUp]
		public void SetUp()
		{
			_originalAddress = DataProtectionConfig.OpenBaoAddress;
			_originalMount = DataProtectionConfig.OpenBaoTransitMount;
			_originalKeyName = DataProtectionConfig.OpenBaoTransitKeyName;

			DataProtectionConfig.OpenBaoAddress = "https://bao.test:8200";
			DataProtectionConfig.OpenBaoTransitMount = "transit";
			DataProtectionConfig.OpenBaoTransitKeyName = "resgrid-dept-kek";
		}

		[TearDown]
		public void TearDown()
		{
			DataProtectionConfig.OpenBaoAddress = _originalAddress;
			DataProtectionConfig.OpenBaoTransitMount = _originalMount;
			DataProtectionConfig.OpenBaoTransitKeyName = _originalKeyName;
		}

		private sealed class ScriptedHandler : HttpMessageHandler
		{
			private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
			public List<(string Path, string Body, string Token)> Requests { get; } = new();

			public ScriptedHandler Enqueue(HttpStatusCode status, string json)
			{
				_responses.Enqueue(_ => new HttpResponseMessage(status)
				{
					Content = new StringContent(json, Encoding.UTF8, "application/json")
				});
				return this;
			}

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
				request.Headers.TryGetValues("X-Vault-Token", out var tokens);
				Requests.Add((request.RequestUri.AbsolutePath, body, tokens?.FirstOrDefault()));

				if (_responses.Count == 0)
					throw new InvalidOperationException("No scripted response left for " + request.RequestUri);

				return _responses.Dequeue()(request);
			}
		}

		private static string LoginJson(string token = "test-token", int lease = 3600) =>
			new JObject { ["auth"] = new JObject { ["client_token"] = token, ["lease_duration"] = lease } }.ToString();

		[Test]
		public async Task Datakey_request_carries_department_context_and_token()
		{
			var handler = new ScriptedHandler()
				.Enqueue(HttpStatusCode.OK, LoginJson())
				.Enqueue(HttpStatusCode.OK, new JObject
				{
					["data"] = new JObject { ["ciphertext"] = "vault:v3:wrapped-blob", ["key_version"] = 3 }
				}.ToString());

			using var provider = new OpenBaoTransitKeyWrappingProvider(handler);
			var wrapped = await provider.GenerateWrappedDataKeyAsync(42);

			wrapped.WrappedKeyBase64.Should().Be("vault:v3:wrapped-blob");
			wrapped.ProviderType.Should().Be("OpenBaoTransit");
			wrapped.ProviderKeyReference.Should().Be("transit/resgrid-dept-kek");
			wrapped.ProviderKeyVersion.Should().Be(3);

			handler.Requests.Should().HaveCount(2);
			handler.Requests[0].Path.Should().Be("/v1/auth/cert/login");
			handler.Requests[1].Path.Should().Be("/v1/transit/datakey/wrapped/resgrid-dept-kek");
			handler.Requests[1].Token.Should().Be("test-token");

			var body = JObject.Parse(handler.Requests[1].Body);
			body["context"].Value<string>().Should().Be(Convert.ToBase64String(Encoding.UTF8.GetBytes("42")),
				"the derived-key context is base64(DepartmentId) per the A.8 ceremony");
			body["bits"].Value<int>().Should().Be(256);
		}

		[Test]
		public async Task Unwrap_round_trips_plaintext_into_bytes_and_reuses_the_token()
		{
			var dek = new byte[32];
			RandomNumberGenerator.Fill(dek);

			var handler = new ScriptedHandler()
				.Enqueue(HttpStatusCode.OK, LoginJson())
				.Enqueue(HttpStatusCode.OK, new JObject
				{
					["data"] = new JObject { ["plaintext"] = Convert.ToBase64String(dek) }
				}.ToString())
				.Enqueue(HttpStatusCode.OK, new JObject
				{
					["data"] = new JObject { ["plaintext"] = Convert.ToBase64String(dek) }
				}.ToString());

			using var provider = new OpenBaoTransitKeyWrappingProvider(handler);
			var first = await provider.UnwrapDataKeyAsync(42, "vault:v3:wrapped-blob");
			var second = await provider.UnwrapDataKeyAsync(42, "vault:v3:wrapped-blob");

			first.Should().Equal(dek);
			second.Should().Equal(dek);

			// One login, two decrypts — the lease-fresh token is reused, never re-fetched per call.
			handler.Requests.Count(r => r.Path == "/v1/auth/cert/login").Should().Be(1);
			handler.Requests.Count(r => r.Path == "/v1/transit/decrypt/resgrid-dept-kek").Should().Be(2);

			var body = JObject.Parse(handler.Requests[1].Body);
			body["ciphertext"].Value<string>().Should().Be("vault:v3:wrapped-blob");
			body["context"].Value<string>().Should().Be(Convert.ToBase64String(Encoding.UTF8.GetBytes("42")));
		}

		[Test]
		public async Task Failed_operation_fails_closed()
		{
			var handler = new ScriptedHandler()
				.Enqueue(HttpStatusCode.OK, LoginJson())
				.Enqueue(HttpStatusCode.Forbidden, "{\"errors\":[\"permission denied\"]}");

			using var provider = new OpenBaoTransitKeyWrappingProvider(handler);

			var act = async () => await provider.UnwrapDataKeyAsync(42, "vault:v3:wrapped-blob");
			await act.Should().ThrowAsync<CryptographicException>();
		}

		[Test]
		public async Task Failed_login_fails_closed_before_any_transit_call()
		{
			var handler = new ScriptedHandler()
				.Enqueue(HttpStatusCode.Forbidden, "{\"errors\":[\"invalid certificate\"]}");

			using var provider = new OpenBaoTransitKeyWrappingProvider(handler);

			var act = async () => await provider.GenerateWrappedDataKeyAsync(42);
			await act.Should().ThrowAsync<CryptographicException>();
			handler.Requests.Should().HaveCount(1, "no transit call may be attempted without a token");
		}

		[Test]
		public async Task Malformed_success_response_fails_closed()
		{
			var handler = new ScriptedHandler()
				.Enqueue(HttpStatusCode.OK, LoginJson())
				.Enqueue(HttpStatusCode.OK, "{\"data\":{}}");

			using var provider = new OpenBaoTransitKeyWrappingProvider(handler);

			var act = async () => await provider.GenerateWrappedDataKeyAsync(42);
			await act.Should().ThrowAsync<CryptographicException>();
		}

		[Test]
		public void Missing_address_refuses_to_construct()
		{
			DataProtectionConfig.OpenBaoAddress = "";

			var act = () => new OpenBaoTransitKeyWrappingProvider(new ScriptedHandler());
			act.Should().Throw<InvalidOperationException>();
		}
	}
}
