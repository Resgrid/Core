using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;

namespace Resgrid.Providers.ProtectedData
{
	/// <summary>Audits every broker-host KMS operation, including migration work outside the HTTP pipeline.</summary>
	public sealed class AuditedKeyWrappingProvider(IKeyWrappingProvider inner, IAdpAuditRepository audit) : IKeyWrappingProvider
	{
		public string ProviderType => inner.ProviderType;

		public async Task<WrappedDataKey> GenerateWrappedDataKeyAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var id = Guid.NewGuid().ToString("N");
			await Record(departmentId, id, "generate-wrapped-key", "requested", cancellationToken);
			try
			{
				var result = await inner.GenerateWrappedDataKeyAsync(departmentId, cancellationToken);
				await Record(departmentId, id, "generate-wrapped-key", "completed", cancellationToken);
				return result;
			}
			catch
			{
				await RecordFailure(departmentId, id, "generate-wrapped-key");
				throw;
			}
		}

		public async Task<byte[]> UnwrapDataKeyAsync(int departmentId, string wrappedKeyBase64, CancellationToken cancellationToken = default)
		{
			var id = Guid.NewGuid().ToString("N");
			await Record(departmentId, id, "unwrap-key", "requested", cancellationToken);
			byte[] key = null;
			try
			{
				key = await inner.UnwrapDataKeyAsync(departmentId, wrappedKeyBase64, cancellationToken);
				await Record(departmentId, id, "unwrap-key", "completed", cancellationToken);
				return key;
			}
			catch
			{
				if (key != null) CryptographicOperations.ZeroMemory(key);
				await RecordFailure(departmentId, id, "unwrap-key");
				throw;
			}
		}

		private Task Record(int departmentId, string id, string operation, string outcome, CancellationToken ct) =>
			audit.AppendAsync(new AdpAuditEvent { DepartmentId = departmentId, Layer = "key-management",
				Operation = operation, Outcome = outcome, CorrelationId = id }, ct);

		private async Task RecordFailure(int departmentId, string id, string operation)
		{
			try { await Record(departmentId, id, operation, "failed", CancellationToken.None); }
			catch { /* Preserve the original failure; no key or successful result is returned. */ }
		}
	}
}
