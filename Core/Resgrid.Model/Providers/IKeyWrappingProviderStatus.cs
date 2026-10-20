using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Optional status surface of an <see cref="IKeyWrappingProvider"/>: whether it is the fail-closed placeholder and
	/// whether its KMS is reachable right now. A decorator around a provider (the broker's audit decorator) must
	/// forward both members to the provider it wraps. Type checks such as <c>is NotConfiguredKeyWrappingProvider</c>
	/// see the decorator, not the provider inside it. Use <see cref="KeyWrappingProviderStatusExtensions"/>, never a
	/// type check.
	/// </summary>
	public interface IKeyWrappingProviderStatus
	{
		/// <summary>False only for the fail-closed placeholder registered on hosts with no KMS adapter.</summary>
		bool IsConfigured { get; }

		/// <summary>
		/// Readiness probe that moves no key material: the KMS answers and this host can still authenticate to it.
		/// Never throws for KMS failures; returns an unhealthy result with a value-free reason code instead.
		/// </summary>
		Task<KeyWrappingProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>Result of <see cref="IKeyWrappingProviderStatus.CheckHealthAsync"/>. Reason is a value-free code.</summary>
	public sealed class KeyWrappingProviderHealth
	{
		public bool IsHealthy { get; init; }

		/// <summary>Value-free reason code (e.g. kms_sealed, kms_unreachable); null when healthy.</summary>
		public string Reason { get; init; }

		public static KeyWrappingProviderHealth Healthy() => new KeyWrappingProviderHealth { IsHealthy = true };

		public static KeyWrappingProviderHealth Unhealthy(string reason) =>
			new KeyWrappingProviderHealth { IsHealthy = false, Reason = reason };
	}

	public static class KeyWrappingProviderStatusExtensions
	{
		/// <summary>
		/// True unless the provider is missing or reports itself as the fail-closed placeholder. A provider without
		/// the status surface (a test double) counts as configured, which matches the old type check.
		/// </summary>
		public static bool IsKmsConfigured(this IKeyWrappingProvider provider) =>
			provider != null && (provider is not IKeyWrappingProviderStatus status || status.IsConfigured);

		/// <summary>
		/// The provider's readiness probe. Missing or unconfigured providers are unhealthy; a provider without the
		/// status surface has nothing to probe and is reported healthy.
		/// </summary>
		public static Task<KeyWrappingProviderHealth> CheckKmsHealthAsync(this IKeyWrappingProvider provider,
			CancellationToken cancellationToken = default)
		{
			if (!provider.IsKmsConfigured())
				return Task.FromResult(KeyWrappingProviderHealth.Unhealthy("kms_not_configured"));

			return provider is IKeyWrappingProviderStatus status
				? status.CheckHealthAsync(cancellationToken)
				: Task.FromResult(KeyWrappingProviderHealth.Healthy());
		}
	}
}
