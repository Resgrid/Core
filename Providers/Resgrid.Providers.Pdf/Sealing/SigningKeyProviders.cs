using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.PdfProvider.Sealing
{
	/// <summary>
	/// A PKCS#12 (.p12/.pfx) signing key, for development and self-hosting (self-hosters supply their own certificate, decision 16b;
	/// without an AATL-listed one Acrobat shows "validity unknown"). Signs digests only.
	/// </summary>
	public sealed class Pkcs12SigningKeyProvider : ISigningKeyProvider, IDisposable
	{
		private readonly X509Certificate2 _leaf;
		private readonly List<X509Certificate2> _chain;

		public Pkcs12SigningKeyProvider(byte[] pkcs12, string password)
		{
			if (pkcs12 == null || pkcs12.Length == 0)
				return;

			X509Certificate2Collection collection;
			try
			{
				collection = X509CertificateLoader.LoadPkcs12Collection(pkcs12, password, X509KeyStorageFlags.EphemeralKeySet);
			}
			catch (CryptographicException)
			{
				// EphemeralKeySet is not available on every platform; the default key set works everywhere else.
				collection = X509CertificateLoader.LoadPkcs12Collection(pkcs12, password, X509KeyStorageFlags.DefaultKeySet);
			}

			_leaf = collection.FirstOrDefault(c => c.HasPrivateKey);
			_chain = collection.Where(c => _leaf == null || !string.Equals(c.Thumbprint, _leaf.Thumbprint, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		/// <summary>From SealingConfig (Pkcs12Base64, else Pkcs12Path). Missing or unreadable ⇒ not configured.</summary>
		public static Pkcs12SigningKeyProvider FromConfig()
		{
			try
			{
				byte[] bytes = null;
				if (!string.IsNullOrWhiteSpace(SealingConfig.Pkcs12Base64))
					bytes = Convert.FromBase64String(SealingConfig.Pkcs12Base64.Trim());
				else if (!string.IsNullOrWhiteSpace(SealingConfig.Pkcs12Path) && System.IO.File.Exists(SealingConfig.Pkcs12Path))
					bytes = System.IO.File.ReadAllBytes(SealingConfig.Pkcs12Path);

				return new Pkcs12SigningKeyProvider(bytes, SealingConfig.Pkcs12Password);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "The sealing PKCS#12 certificate could not be loaded; sealing stays pending.");
				return new Pkcs12SigningKeyProvider(null, null);
			}
		}

		public bool IsConfigured => _leaf != null;

		public Task<SigningCertificateInfo> GetCertificateAsync(CancellationToken cancellationToken = default)
		{
			if (_leaf == null)
				throw new InvalidOperationException("sealing_key_not_configured");

			return Task.FromResult(new SigningCertificateInfo
			{
				Certificate = _leaf.RawData,
				Chain = _chain.Select(c => c.RawData).ToList(),
				Thumbprint = Convert.ToHexString(SHA256.HashData(_leaf.RawData)).ToLowerInvariant(),
				KeyAlgorithm = _leaf.GetECDsaPublicKey() != null ? "ECDSA" : "RSA"
			});
		}

		public Task<byte[]> SignDigestAsync(byte[] sha256Digest, CancellationToken cancellationToken = default)
		{
			if (_leaf == null)
				throw new InvalidOperationException("sealing_key_not_configured");
			if (sha256Digest == null || sha256Digest.Length != 32)
				throw new ArgumentException("A SHA-256 digest is required.", nameof(sha256Digest));

			using var rsa = _leaf.GetRSAPrivateKey();
			if (rsa != null)
				return Task.FromResult(rsa.SignHash(sha256Digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

			using var ecdsa = _leaf.GetECDsaPrivateKey();
			if (ecdsa != null)
				return Task.FromResult(ecdsa.SignHash(sha256Digest, DSASignatureFormat.Rfc3279DerSequence));

			throw new InvalidOperationException("sealing_key_algorithm_unsupported");
		}

		public void Dispose()
		{
			_leaf?.Dispose();
			foreach (var c in _chain ?? new List<X509Certificate2>())
				c.Dispose();
		}
	}

	/// <summary>
	/// The default remote hash signer: not configured. The HSM / CA remote-signing option is still undecided (§25.5); register an
	/// <see cref="IRemoteHashSigner"/> for the chosen one and set SealingConfig.CertificateSource to "remote".
	/// </summary>
	public sealed class NotConfiguredRemoteHashSigner : IRemoteHashSigner
	{
		public bool IsConfigured => false;

		public Task<SigningCertificateInfo> GetCertificateAsync(CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("sealing_remote_signer_not_configured");

		public Task<byte[]> SignHashAsync(byte[] sha256Digest, string keyAlgorithm, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("sealing_remote_signer_not_configured");
	}

	/// <summary>A signing key held by a remote hash signer (HSM or CA): only the digest is sent.</summary>
	public sealed class RemoteSigningKeyProvider : ISigningKeyProvider
	{
		private readonly IRemoteHashSigner _signer;
		private SigningCertificateInfo _certificate;

		public RemoteSigningKeyProvider(IRemoteHashSigner signer)
		{
			_signer = signer;
		}

		public bool IsConfigured => _signer != null && _signer.IsConfigured;

		public async Task<SigningCertificateInfo> GetCertificateAsync(CancellationToken cancellationToken = default)
		{
			if (!IsConfigured)
				throw new InvalidOperationException("sealing_key_not_configured");
			return _certificate ??= await _signer.GetCertificateAsync(cancellationToken);
		}

		public async Task<byte[]> SignDigestAsync(byte[] sha256Digest, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured)
				throw new InvalidOperationException("sealing_key_not_configured");
			var certificate = await GetCertificateAsync(cancellationToken);
			return await _signer.SignHashAsync(sha256Digest, certificate.KeyAlgorithm, cancellationToken);
		}
	}

	/// <summary>
	/// The key provider SealingConfig.CertificateSource selects: "pkcs12", "remote", or nothing (blank or unknown ⇒ not configured,
	/// so sealing fails closed). Read once per process; a changed certificate needs a restart.
	/// </summary>
	public sealed class ConfiguredSigningKeyProvider : ISigningKeyProvider
	{
		private readonly Lazy<ISigningKeyProvider> _inner;

		public ConfiguredSigningKeyProvider(IRemoteHashSigner remoteSigner)
		{
			_inner = new Lazy<ISigningKeyProvider>(() => Select(SealingConfig.CertificateSource, remoteSigner), LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public static ISigningKeyProvider Select(string source, IRemoteHashSigner remoteSigner)
		{
			switch ((source ?? string.Empty).Trim().ToLowerInvariant())
			{
				case "pkcs12":
					return Pkcs12SigningKeyProvider.FromConfig();
				case "remote":
					return new RemoteSigningKeyProvider(remoteSigner);
				default:
					return new Pkcs12SigningKeyProvider(null, null);
			}
		}

		public bool IsConfigured => _inner.Value.IsConfigured;

		public Task<SigningCertificateInfo> GetCertificateAsync(CancellationToken cancellationToken = default) => _inner.Value.GetCertificateAsync(cancellationToken);

		public Task<byte[]> SignDigestAsync(byte[] sha256Digest, CancellationToken cancellationToken = default) => _inner.Value.SignDigestAsync(sha256Digest, cancellationToken);
	}
}
