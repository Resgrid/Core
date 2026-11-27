using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Adp
{
	/// <summary>
	/// A department with Advanced Data Protection enforced, for the round-8 compatibility tests: the real read/write seam
	/// (<see cref="ProtectedReadService"/>) and catalog, a broker that really encrypts (AES-GCM under a test key, the production envelope
	/// format), and a grant issuer. Without a grant every sealed value reads REDACTED, as on a page load; unattended paths never decrypt.
	/// </summary>
	public sealed class AdpTestKit : IDisposable
	{
		public const int DefaultDepartmentId = 4242;

		public int DepartmentId { get; }
		public const long Epoch = 7;
		public const string UserId = "adp-user-1";

		private readonly X509Certificate2 _certificate;
		private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
		private readonly ProtectedFieldCryptoService _crypto = new ProtectedFieldCryptoService();

		public Mock<IDepartmentDataProtectionService> Protection { get; } = new Mock<IDepartmentDataProtectionService>();
		public Mock<IProtectedDataBrokerClient> Broker { get; } = new Mock<IProtectedDataBrokerClient>();
		public ProtectedDataGrantService Grants { get; }
		public ProtectedReadService Seam { get; }
		public Mock<IProtectedGrantContext> GrantContext { get; } = new Mock<IProtectedGrantContext>();

		/// <summary>Every value the broker was asked to decrypt (an unattended path must never add to it).</summary>
		public List<ProtectedFieldOperationItem> Decrypted { get; } = new List<ProtectedFieldOperationItem>();

		public AdpTestKit(bool enforced = true, int catalogVersion = ContentProtectedFields.CatalogVersion, int departmentId = DefaultDepartmentId)
		{
			DepartmentId = departmentId;
			using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
			{
				var request = new CertificateRequest("CN=adp-round8-tests", ecdsa, HashAlgorithmName.SHA256);
				_certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
			}

			Grants = new ProtectedDataGrantService(() => _certificate, () => _certificate);
			Protection.Setup(x => x.IsProtectionEnforcedAsync(DepartmentId)).ReturnsAsync(enforced);
			Protection.Setup(x => x.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(enforced);
			Protection.Setup(x => x.GetPolicyByDepartmentIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DepartmentId, PolicyEpoch = Epoch, CatalogVersion = catalogVersion });

			Broker.Setup(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) => new ProtectedDataBrokerResult
				{
					Success = true,
					Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = _crypto.EncryptText(_key, 1, i.Value, d, i.FieldId, i.RowKey) }).ToList()
				});
			Broker.Setup(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
				{
					Decrypted.AddRange(items);
					return new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = _crypto.DecryptText(_key, i.Value, d, i.FieldId, i.RowKey) }).ToList()
					};
				});

			Seam = new ProtectedReadService(Protection.Object, Grants, Broker.Object, new ProtectedFieldCatalog());
		}

		/// <summary>A valid read+write grant for <see cref="UserId"/> in the department.</summary>
		public string IssueGrant(string userId = UserId) => Grants.IssueGrant(new ProtectedDataGrantIssueRequest
		{
			UserId = userId,
			DepartmentId = DepartmentId,
			PolicyEpoch = Epoch,
			WindowMinutes = 15,
			Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
			MfaAtUtc = DateTime.UtcNow
		}).Token;

		/// <summary>The grant context of an attended caller holding a grant (<paramref name="withGrant"/>) or a page load without one.</summary>
		public IProtectedGrantContext Attended(bool withGrant, string userId = UserId)
		{
			GrantContext.SetupGet(x => x.GrantToken).Returns(withGrant ? IssueGrant(userId) : null);
			GrantContext.SetupGet(x => x.UserId).Returns(userId);
			return GrantContext.Object;
		}

		/// <summary>The value enveloped as this department's ADP would store it.</summary>
		public string Seal(string fieldId, string rowKey, string plaintext) => _crypto.EncryptText(_key, 1, plaintext, DepartmentId, fieldId, rowKey);

		public string Open(string fieldId, string rowKey, string envelope) => _crypto.DecryptText(_key, envelope, DepartmentId, fieldId, rowKey);

		public void Dispose() => _certificate?.Dispose();
	}
}
