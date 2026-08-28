using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	/// <summary>
	/// Fail-closed placeholder registered when DataProtectionConfig.KeyWrappingProviderType names a
	/// provider this host does not implement (in the target topology only the Protected Data Broker
	/// runs a real KMS adapter — Web/API/worker hosts land here by design). Every operation throws:
	/// protected operations fail closed rather than degrading to a local or null crypto path.
	/// </summary>
	public class NotConfiguredKeyWrappingProvider : IKeyWrappingProvider
	{
		public string ProviderType => DataProtectionConfig.KeyWrappingProviderType;

		public Task<WrappedDataKey> GenerateWrappedDataKeyAsync(int departmentId, CancellationToken cancellationToken = default) =>
			throw Fail();

		public Task<byte[]> UnwrapDataKeyAsync(int departmentId, string wrappedKeyBase64, CancellationToken cancellationToken = default) =>
			throw Fail();

		private static InvalidOperationException Fail() => new InvalidOperationException(
			$"Key wrapping provider '{DataProtectionConfig.KeyWrappingProviderType}' is not available on this host. " +
			"Department key operations run only where the configured KMS adapter is deployed (the Protected Data Broker); protected operations fail closed here.");
	}
}
