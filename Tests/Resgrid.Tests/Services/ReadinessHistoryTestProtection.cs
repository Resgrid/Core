using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>Real protected-write pipeline and authenticated encryption with an in-process broker.</summary>
	internal sealed class ReadinessHistoryTestProtection : IDisposable
	{
		private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
		private readonly ProtectedFieldCryptoService _crypto = new();
		public Mock<IProtectedDataBrokerClient> Broker { get; } = new();
		public ReadinessHistoryProtectionService Service { get; }
		public Lazy<IReadinessHistoryProtectionService> Lazy => new(() => Service);
		public ReadinessHistoryTestProtection(Mock<IDepartmentDataProtectionService> policy)
		{
			policy.Setup(s => s.ShouldEncryptNewWritesAsync(It.IsAny<int>())).Returns((int d) => policy.Object.IsProtectionEnforcedAsync(d));
			policy.Setup(s => s.GetPolicyByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, bool b) => new DepartmentDataProtectionPolicy { DepartmentId = d, CatalogVersion = ReadinessHistoryFields.CatalogVersion });
			Broker.Setup(s => s.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string grant, string request, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) => new ProtectedDataBrokerResult
				{
					Success = true, Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = _crypto.EncryptText(_key, 1, i.Value, d, i.FieldId, i.RowKey) }).ToList()
				});
			Service = new ReadinessHistoryProtectionService(new ProtectedReadService(policy.Object, Mock.Of<IProtectedDataGrantService>(), Broker.Object, new ProtectedFieldCatalog()), policy.Object);
		}
		public string Decrypt(int departmentId, string field, string row, string envelope) => _crypto.DecryptText(_key, envelope, departmentId, field, row);
		public void Dispose() => CryptographicOperations.ZeroMemory(_key);
	}
}
