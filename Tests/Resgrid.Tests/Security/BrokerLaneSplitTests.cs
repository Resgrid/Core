using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Providers.ProtectedData;
using Resgrid.Web.Broker.Middleware;
using Resgrid.Web.Broker.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The broker lane split (passkey plan section 8.5): one credential per calling host, stored as key hashes and compared
	/// in constant time, each authorized for specific lanes and purposes; the legacy shared key only for the migration
	/// window; and a client that presents its own credential.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class BrokerLaneSplitTests
	{
		private const string ApiKey = "api-key-0123456789abcdef0123456789abcdef";
		private const string ApiNextKey = "api-next-key-0123456789abcdef012345678";
		private const string WorkersKey = "workers-key-0123456789abcdef0123456789";
		private const string BackofficeKey = "backoffice-key-0123456789abcdef01234567";

		private string _purposes, _legacyKey, _clientId, _clientKey, _credentials, _baseUrl;
		private bool _legacyEnabled;

		private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

		private static string Map =>
			$"api=attended,workload,receipt|records-export,invoicing|{Hash(ApiKey)},{Hash(ApiNextKey)};" +
			$"workers=workload|neris-submission,records-export|{Hash(WorkersKey)};" +
			$"backoffice=receipt||{Hash(BackofficeKey)}";

		[SetUp]
		public void SetUp()
		{
			_purposes = DataProtectionConfig.BrokerWorkloadPurposes;
			_legacyKey = DataProtectionConfig.BrokerApiKey;
			_legacyEnabled = DataProtectionConfig.BrokerLegacySharedKeyEnabled;
			_clientId = DataProtectionConfig.BrokerClientId;
			_clientKey = DataProtectionConfig.BrokerClientKey;
			_credentials = DataProtectionConfig.BrokerClientCredentials;
			_baseUrl = DataProtectionConfig.BrokerBaseUrl;
			DataProtectionConfig.BrokerWorkloadPurposes = "neris-submission,records-export,invoicing,protected-workflow";
			DataProtectionConfig.BrokerApiKey = "";
			DataProtectionConfig.BrokerLegacySharedKeyEnabled = true;
		}

		[TearDown]
		public void TearDown()
		{
			DataProtectionConfig.BrokerWorkloadPurposes = _purposes;
			DataProtectionConfig.BrokerApiKey = _legacyKey;
			DataProtectionConfig.BrokerLegacySharedKeyEnabled = _legacyEnabled;
			DataProtectionConfig.BrokerClientId = _clientId;
			DataProtectionConfig.BrokerClientKey = _clientKey;
			DataProtectionConfig.BrokerClientCredentials = _credentials;
			DataProtectionConfig.BrokerBaseUrl = _baseUrl;
		}

		// ---- Registry ----------------------------------------------------------------------------------------------

		[Test]
		public void Each_host_gets_exactly_its_lanes_and_purposes()
		{
			var registry = new BrokerCredentialRegistry(Map);

			registry.IsValid.Should().BeTrue();
			registry.CredentialIds.Should().BeEquivalentTo("api", "workers", "backoffice");

			var workers = registry.Authenticate("workers", WorkersKey);
			workers.Allows(BrokerLane.Workload).Should().BeTrue();
			workers.Allows(BrokerLane.Attended).Should().BeFalse("workers never act for an attended user");
			workers.Allows(BrokerLane.Receipt).Should().BeFalse();
			workers.AllowsPurpose("neris-submission").Should().BeTrue();
			workers.AllowsPurpose("invoicing").Should().BeFalse("a purpose the broker allows is still not this host's");

			var backoffice = registry.Authenticate("backoffice", BackofficeKey);
			backoffice.Lanes.Should().BeEquivalentTo(new[] { BrokerLane.Receipt }, "BackOffice support gets neither attended nor workload access");
		}

		[Test]
		public void Both_the_current_and_the_next_key_work_during_rotation()
		{
			var registry = new BrokerCredentialRegistry(Map);

			registry.Authenticate("api", ApiKey).Should().NotBeNull();
			registry.Authenticate("api", ApiNextKey).Should().NotBeNull();
		}

		[Test]
		public void A_key_only_opens_its_own_credential()
		{
			var registry = new BrokerCredentialRegistry(Map);

			registry.Authenticate("api", WorkersKey).Should().BeNull("the workers key is not the API's key");
			registry.Authenticate("workers", "wrong").Should().BeNull();
			registry.Authenticate("workers", "").Should().BeNull();
			registry.Authenticate("unknown", WorkersKey).Should().BeNull();
			registry.Authenticate(null, WorkersKey).Should().BeNull();
		}

		[Test]
		public void Upper_case_hashes_are_accepted()
		{
			new BrokerCredentialRegistry($"api=attended||{Hash(ApiKey).ToUpperInvariant()}").Authenticate("api", ApiKey).Should().NotBeNull();
		}

		[Test]
		public void An_empty_map_is_valid_but_has_no_credentials()
		{
			var registry = new BrokerCredentialRegistry("");

			registry.IsValid.Should().BeTrue();
			registry.HasCredentials.Should().BeFalse();
		}

		private static readonly (string Name, string Map)[] InvalidMaps =
		{
			("not id=fields", "api"),
			("two fields", $"api=attended|{Hash(ApiKey)}"),
			("an upper-case id", $"API=attended||{Hash(ApiKey)}"),
			("a 17-character id", $"abcdefghijklmnopq=attended||{Hash(ApiKey)}"),
			("the reserved legacy id", $"legacy=attended||{Hash(ApiKey)}"),
			("a duplicate id", $"api=attended||{Hash(ApiKey)};api=receipt||{Hash(WorkersKey)}"),
			("no lanes", $"api=||{Hash(ApiKey)}"),
			("an unknown lane", $"api=admin||{Hash(ApiKey)}"),
			("purposes without the workload lane", $"api=attended|records-export|{Hash(ApiKey)}"),
			("a purpose off the global list", $"api=workload|bulk-dump|{Hash(ApiKey)}"),
			("no key hash", "api=attended||"),
			("three key hashes", $"api=attended||{Hash("a")},{Hash("b")},{Hash("c")}"),
			("a plaintext key instead of a hash", "api=attended||not-a-hash"),
			("two hosts sharing one key", $"api=attended||{Hash(ApiKey)};web=attended||{Hash(ApiKey)}")
		};

		[TestCaseSource(nameof(InvalidMaps))]
		public void An_invalid_map_is_rejected_whole((string Name, string Map) invalid)
		{
			var registry = new BrokerCredentialRegistry(invalid.Map);

			registry.IsValid.Should().BeFalse(invalid.Name);
			registry.Problems.Should().NotBeEmpty();
			registry.HasCredentials.Should().BeFalse();
			registry.Authenticate("api", ApiKey).Should().BeNull("nothing authenticates against a map that failed validation");
		}

		[Test]
		public void The_legacy_key_has_full_authority_only_while_accepted()
		{
			DataProtectionConfig.BrokerApiKey = "legacy-shared-key";

			BrokerCredentialRegistry.IsLegacyKey("legacy-shared-key").Should().BeTrue();
			BrokerCredentialRegistry.IsLegacyKey("other").Should().BeFalse();
			var legacy = BrokerCredential.Legacy();
			legacy.Lanes.Should().BeEquivalentTo(new[] { BrokerLane.Attended, BrokerLane.Workload, BrokerLane.Receipt });
			legacy.AllowsPurpose("records-export").Should().BeTrue();

			DataProtectionConfig.BrokerLegacySharedKeyEnabled = false;
			BrokerCredentialRegistry.IsLegacyKey("legacy-shared-key").Should().BeFalse("a retired shared key opens nothing");
		}

		[Test]
		public void The_audit_layer_names_credential_and_lane_within_the_column()
		{
			var longest = new BrokerCredential { Id = new string('a', 16), Lanes = new[] { BrokerLane.Attended }.ToHashSet(), WorkloadPurposes = new System.Collections.Generic.HashSet<string>() };

			longest.AuditLayer(BrokerLane.Attended).Length.Should().BeLessThanOrEqualTo(32, "AdpAuditEvents.Layer is 32 characters");
			BrokerCredential.Legacy().AuditLayer(BrokerLane.Receipt).Should().Be("broker/legacy/receipt");
		}

		// ---- Middleware --------------------------------------------------------------------------------------------

		private static async Task<(DefaultHttpContext Context, bool Passed)> Invoke(BrokerCredentialRegistry registry, string clientId, string key,
			string path = "/api/v1/broker/decrypt")
		{
			var context = new DefaultHttpContext();
			context.Request.Path = path;
			if (clientId != null)
				context.Request.Headers[BrokerCredentialMiddleware.ClientHeader] = clientId;
			if (key != null)
				context.Request.Headers[BrokerCredentialMiddleware.KeyHeader] = key;
			var passed = false;
			await new BrokerCredentialMiddleware(_ => { passed = true; return Task.CompletedTask; }).InvokeAsync(context, registry);
			return (context, passed);
		}

		[Test]
		public async Task A_host_credential_passes_with_its_identity_attached()
		{
			var (context, passed) = await Invoke(new BrokerCredentialRegistry(Map), "workers", WorkersKey);

			passed.Should().BeTrue();
			(context.Items[BrokerCredentialMiddleware.CredentialItemKey] as BrokerCredential).Id.Should().Be("workers");
		}

		[TestCase("workers", "wrong-key")]
		[TestCase("nobody", WorkersKey)]
		[TestCase("api", WorkersKey)]
		public async Task A_wrong_credential_is_unauthorized(string clientId, string key)
		{
			var (context, passed) = await Invoke(new BrokerCredentialRegistry(Map), clientId, key);

			passed.Should().BeFalse();
			context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
		}

		[Test]
		public async Task The_legacy_key_works_without_a_client_id_only_during_the_window()
		{
			DataProtectionConfig.BrokerApiKey = "legacy-shared-key";
			var registry = new BrokerCredentialRegistry(Map);

			var (context, passed) = await Invoke(registry, null, "legacy-shared-key");
			passed.Should().BeTrue();
			(context.Items[BrokerCredentialMiddleware.CredentialItemKey] as BrokerCredential).IsLegacy.Should().BeTrue();

			(await Invoke(registry, "api", "legacy-shared-key")).Passed.Should().BeFalse("naming a credential never falls back to the shared key");

			DataProtectionConfig.BrokerLegacySharedKeyEnabled = false;
			var (retired, retiredPassed) = await Invoke(registry, null, "legacy-shared-key");
			retiredPassed.Should().BeFalse();
			retired.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
		}

		[Test]
		public async Task Nothing_configured_refuses_everything_but_health()
		{
			var registry = new BrokerCredentialRegistry("");

			var (context, passed) = await Invoke(registry, null, "anything");
			passed.Should().BeFalse();
			context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

			(await Invoke(registry, null, null, "/health")).Passed.Should().BeTrue();
		}

		// ---- Client ------------------------------------------------------------------------------------------------

		private sealed class CapturingHandler : HttpMessageHandler
		{
			public HttpRequestMessage Last;

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Last = request;
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"Success\":true,\"Items\":[]}") });
			}
		}

		private static string Header(HttpRequestMessage request, string name) =>
			request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

		private static async Task<HttpRequestMessage> Send()
		{
			DataProtectionConfig.BrokerBaseUrl = "https://broker.test";
			var handler = new CapturingHandler();
			using var client = new ProtectedDataBrokerClient(handler, Mock.Of<IAdpAuditRepository>());
			await client.EncryptAsync(42, null, "req-1", new[] { new ProtectedFieldOperationItem { FieldId = "f", RowKey = "1", Value = "v" } });
			return handler.Last;
		}

		[Test]
		public async Task A_host_with_its_own_credential_never_sends_the_shared_key()
		{
			DataProtectionConfig.BrokerApiKey = "legacy-shared-key";
			DataProtectionConfig.BrokerClientId = "workers";
			DataProtectionConfig.BrokerClientKey = WorkersKey;

			var request = await Send();

			Header(request, BrokerCredentialMiddleware.ClientHeader).Should().Be("workers");
			Header(request, BrokerCredentialMiddleware.KeyHeader).Should().Be(WorkersKey);
			Header(request, BrokerCredentialMiddleware.HostHeader).Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task A_host_without_a_credential_keeps_using_the_shared_key()
		{
			DataProtectionConfig.BrokerApiKey = "legacy-shared-key";
			DataProtectionConfig.BrokerClientId = "";
			DataProtectionConfig.BrokerClientKey = "";

			var request = await Send();

			Header(request, BrokerCredentialMiddleware.ClientHeader).Should().BeNull();
			Header(request, BrokerCredentialMiddleware.KeyHeader).Should().Be("legacy-shared-key");
		}
	}
}
