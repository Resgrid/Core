using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Broker.Models;
using Resgrid.Web.Broker.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Broker field-crypto pipeline (ADP plan section 3.1 steps 8-9) against the REAL grant, crypto
	/// and LocalDev key-wrapping implementations — only the repositories are mocked. Proves the
	/// grant gates (invalid/revoked/scope), replay refusal, encrypt/decrypt roundtrip with AAD
	/// binding, the double-encryption guard, and fail-closed item errors.
	/// </summary>
	[TestFixture]
	public class BrokerOperationServiceTests
	{
		private const int DeptId = 42;
		private const long Epoch = 3;

		private X509Certificate2 _certificate;
		private ProtectedDataGrantService _grantService;
		private LocalDevKeyWrappingProvider _keyWrappingProvider;
		private ProtectedFieldCryptoService _cryptoService;
		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private Mock<IDepartmentKeyService> _keyService;
		private IContainer _container;
		private BrokerOperationService _service;
		private DepartmentDataProtectionKey _activeKey;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			var request = new CertificateRequest("CN=adp-broker-tests", ecdsa, HashAlgorithmName.SHA256);
			_certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_certificate?.Dispose();
			_container?.Dispose();
		}

		[SetUp]
		public async Task SetUp()
		{
			_container?.Dispose();

			_grantService = new ProtectedDataGrantService(() => _certificate, () => _certificate);
			_keyWrappingProvider = new LocalDevKeyWrappingProvider();
			_cryptoService = new ProtectedFieldCryptoService();

			var wrapped = await _keyWrappingProvider.GenerateWrappedDataKeyAsync(DeptId);
			_activeKey = new DepartmentDataProtectionKey
			{
				DepartmentId = DeptId,
				Version = 1,
				Status = (int)DepartmentDataProtectionKeyStatus.Active,
				WrappedKey = wrapped.WrappedKeyBase64
			};

			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch });

			_keyService = new Mock<IDepartmentKeyService>();
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId)).ReturnsAsync(_activeKey);
			_keyService.Setup(x => x.GetKeyByVersionAsync(DeptId, 1)).ReturnsAsync(_activeKey);

			var builder = new ContainerBuilder();
			builder.RegisterInstance(_policyRepo.Object).As<IDepartmentDataProtectionPolicyRepository>();
			builder.RegisterInstance(_keyService.Object).As<IDepartmentKeyService>();
			_container = builder.Build();

			_service = new BrokerOperationService(_container, _grantService, _cryptoService,
				_keyWrappingProvider, new MemoryCache(new MemoryCacheOptions()));
		}

		private string IssueGrantToken(params string[] scopes)
		{
			var issued = _grantService.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = "user-1",
				DepartmentId = DeptId,
				PolicyEpoch = Epoch,
				WindowMinutes = 15,
				Scopes = scopes.Length > 0 ? scopes : new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
				MfaAtUtc = DateTime.UtcNow
			});
			return issued.Token;
		}

		private static BrokerFieldOperationRequest Request(string grantToken, string requestId,
			params ProtectedFieldOperationItem[] items) => new BrokerFieldOperationRequest
		{
			DepartmentId = DeptId,
			GrantToken = grantToken,
			RequestId = requestId,
			Items = items.ToList()
		};

		private static ProtectedFieldOperationItem Item(string value, string fieldId = "calls.natureofcall",
			string rowKey = "17", int catalogVersion = 1) => new ProtectedFieldOperationItem
		{
			FieldId = fieldId,
			RowKey = rowKey,
			Value = value,
			CatalogVersion = catalogVersion
		};

		[Test]
		public async Task Encrypt_then_decrypt_roundtrips_with_full_aad_binding()
		{
			var token = IssueGrantToken();

			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("Structure fire, 3 Main St")), CancellationToken.None);
			encrypted.Success.Should().BeTrue();
			encrypted.Items.Should().HaveCount(1);
			encrypted.Items[0].ErrorCode.Should().BeNull();
			ProtectedDataEnvelope.IsEnveloped(encrypted.Items[0].Value).Should().BeTrue();

			var decrypted = await _service.DecryptAsync(Request(token, "req-2", Item(encrypted.Items[0].Value)), CancellationToken.None);
			decrypted.Success.Should().BeTrue();
			decrypted.Items[0].ErrorCode.Should().BeNull();
			decrypted.Items[0].Value.Should().Be("Structure fire, 3 Main St");
		}

		[Test]
		public async Task Moved_ciphertext_fails_decrypt_per_item_without_failing_the_request()
		{
			var token = IssueGrantToken();
			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("secret", rowKey: "17")), CancellationToken.None);

			// Same envelope presented for a different row: AAD mismatch.
			var moved = await _service.DecryptAsync(Request(token, "req-2",
				Item(encrypted.Items[0].Value, rowKey: "99")), CancellationToken.None);

			moved.Success.Should().BeTrue();
			moved.Items[0].ErrorCode.Should().Be("decrypt_failed");
			moved.Items[0].Value.Should().BeNull();
		}

		[Test]
		public async Task Replayed_request_id_is_refused()
		{
			var token = IssueGrantToken();
			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);
			encrypted.Success.Should().BeTrue();

			var replay = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);
			replay.Success.Should().BeFalse();
			replay.ErrorCode.Should().Be("replayed_request");
			replay.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Revoked_grant_after_epoch_bump_is_refused()
		{
			var token = IssueGrantToken();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch + 1 });

			var result = await _service.DecryptAsync(Request(token, "req-1", Item("rgdp:1:1:AAAA")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("grant_revoked");
			result.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Grant_without_the_write_scope_cannot_encrypt()
		{
			var readOnlyToken = IssueGrantToken(ProtectedDataGrantScopes.Read);

			var result = await _service.EncryptAsync(Request(readOnlyToken, "req-1", Item("value")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("grant_invalid");
		}

		[Test]
		public async Task Garbage_grant_is_refused()
		{
			var result = await _service.DecryptAsync(Request("not-a-grant", "req-1", Item("rgdp:1:1:AAAA")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("grant_invalid");
		}

		[Test]
		public async Task Decrypting_plaintext_reports_not_enveloped_and_never_echoes_the_value()
		{
			var token = IssueGrantToken();

			var result = await _service.DecryptAsync(Request(token, "req-1", Item("just plain text")), CancellationToken.None);

			result.Success.Should().BeTrue();
			result.Items[0].ErrorCode.Should().Be("not_enveloped");
			result.Items[0].Value.Should().BeNull();
		}

		[Test]
		public async Task Encrypting_an_envelope_trips_the_double_encryption_guard()
		{
			var token = IssueGrantToken();
			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);

			var again = await _service.EncryptAsync(Request(token, "req-2", Item(encrypted.Items[0].Value)), CancellationToken.None);

			again.Success.Should().BeTrue();
			again.Items[0].ErrorCode.Should().Be("already_enveloped");
			again.Items[0].Value.Should().BeNull();
		}

		[Test]
		public async Task Unknown_key_version_reports_key_unknown()
		{
			var token = IssueGrantToken();

			var result = await _service.DecryptAsync(Request(token, "req-1", Item("rgdp:1:9:AAAA")), CancellationToken.None);

			result.Success.Should().BeTrue();
			result.Items[0].ErrorCode.Should().Be("key_unknown");
		}

		[Test]
		public async Task Oversized_requests_are_refused()
		{
			var token = IssueGrantToken();
			var items = Enumerable.Range(0, Resgrid.Config.DataProtectionConfig.BrokerMaxItemsPerRequest + 1)
				.Select(i => Item("value", rowKey: i.ToString()))
				.ToArray();

			var result = await _service.EncryptAsync(Request(token, "req-1", items), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("too_many_items");
		}

		[Test]
		public async Task Missing_active_key_fails_encrypt_closed()
		{
			var token = IssueGrantToken();
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionKey)null);

			var result = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("no_active_key");
			result.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Empty_and_null_requests_are_invalid()
		{
			var missingItems = await _service.DecryptAsync(new BrokerFieldOperationRequest
			{
				DepartmentId = DeptId,
				GrantToken = "x",
				RequestId = "req-1",
				Items = new List<ProtectedFieldOperationItem>()
			}, CancellationToken.None);
			missingItems.Success.Should().BeFalse();
			missingItems.ErrorCode.Should().Be("invalid_request");

			var nullRequest = await _service.DecryptAsync(null, CancellationToken.None);
			nullRequest.Success.Should().BeFalse();
			nullRequest.ErrorCode.Should().Be("invalid_request");
		}
	}
}
