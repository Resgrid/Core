using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Resgrid.Tests.Helpers
{
	/// <summary>
	/// A PKCS#12 file in the temp directory, written the way the step-ca sidecar renews one: a fresh self-signed leaf
	/// generated in process, written beside the target and renamed over it. Nothing is committed to the repository.
	/// Every write gets a distinct last-write time, because filesystem timestamp granularity can otherwise make two
	/// writes in one test look identical and hide a reload that did happen.
	/// </summary>
	public sealed class Pkcs12TestFile : IDisposable
	{
		public const string Password = "test-pkcs12-password";

		private DateTime _nextWriteTimeUtc = DateTime.UtcNow.AddDays(-1);

		public string Path { get; } =
			System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rg-reloading-cert-{Guid.NewGuid():N}.pfx");

		/// <summary>Writes a fresh certificate and returns its thumbprint.</summary>
		public string Write(string subject, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null,
			string password = Password)
		{
			using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
			using var certificate = request.CreateSelfSigned(
				notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5),
				notAfter ?? DateTimeOffset.UtcNow.AddDays(1));

			Replace(certificate.Export(X509ContentType.Pkcs12, password));
			return certificate.Thumbprint;
		}

		/// <summary>Replaces the file with bytes that are not a PKCS#12.</summary>
		public void Corrupt() => Replace(System.Text.Encoding.UTF8.GetBytes("not a certificate"));

		public void Delete()
		{
			if (File.Exists(Path))
				File.Delete(Path);
		}

		public void Dispose()
		{
			Delete();
			var staging = Path + ".tmp";
			if (File.Exists(staging))
				File.Delete(staging);
		}

		private void Replace(byte[] contents)
		{
			var staging = Path + ".tmp";
			File.WriteAllBytes(staging, contents);
			File.SetLastWriteTimeUtc(staging, _nextWriteTimeUtc);
			_nextWriteTimeUtc = _nextWriteTimeUtc.AddMinutes(1);
			File.Move(staging, Path, overwrite: true);
		}
	}
}
