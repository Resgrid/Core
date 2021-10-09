using Resgrid.Console.Args;
using System;
using Consolas2.Core;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Resgrid.Console.Commands
{
	public class GenOidcCertsCommand : Command
	{
		private readonly IConsole _console;

		public GenOidcCertsCommand(IConsole console)
		{
			_console = console;
		}

		public string Execute(GenOidcCertsArgs args)
		{
			_console.WriteLine("Starting the Resgrid OIDC Certification Generation Process");
			_console.WriteLine("Please Wait...");

			try
			{
				using var algorithm = RSA.Create(keySizeInBits: 2048);

				var subject = new X500DistinguishedName("CN=Resgrid Encryption Certificate");
				var request = new CertificateRequest(subject, algorithm,
					HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				request.CertificateExtensions.Add(new X509KeyUsageExtension(
					X509KeyUsageFlags.KeyEncipherment, critical: true));

				var certificate = request.CreateSelfSigned(
					DateTimeOffset.UtcNow,
					DateTimeOffset.UtcNow.AddYears(5));

				var encryptionCertificate = certificate.Export(X509ContentType.Pfx, string.Empty);

				_console.WriteLine("==========================================================");
				_console.WriteLine("=                 BEGIN ENCRYPTION CERT                  =");
				_console.WriteLine("==========================================================");
				//_console.WriteLine("-----BEGIN CERTIFICATE-----");
				_console.WriteLine(Convert.ToBase64String(encryptionCertificate));
				//_console.WriteLine("-----END CERTIFICATE-----");
				_console.WriteLine("==========================================================");
				_console.WriteLine("=                  END ENCRYPTION CERT                   =");
				_console.WriteLine("==========================================================");

				using var algorithm2 = RSA.Create(keySizeInBits: 2048);

				var subject2 = new X500DistinguishedName("CN=Resgrid Signing Certificate");
				var request2 = new CertificateRequest(subject2, algorithm2,
					HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				request2.CertificateExtensions.Add(new X509KeyUsageExtension(
					X509KeyUsageFlags.DigitalSignature, critical: true));

				var certificate2 = request2.CreateSelfSigned(
					DateTimeOffset.UtcNow,
					DateTimeOffset.UtcNow.AddYears(5));

				var signingCertificate = certificate2.Export(X509ContentType.Pfx, string.Empty);

				_console.WriteLine("==========================================================");
				_console.WriteLine("=                  BEGIN SIGNING CERT                    =");
				_console.WriteLine("==========================================================");
				//_console.WriteLine("-----BEGIN CERTIFICATE-----");
				_console.WriteLine(Convert.ToBase64String(signingCertificate));
				//_console.WriteLine("-----END CERTIFICATE-----");
				_console.WriteLine("==========================================================");
				_console.WriteLine("=                   END SIGNING CERT                     =");
				_console.WriteLine("==========================================================");
			}
			catch (Exception ex)
			{
				_console.WriteLine("There was an error trying to Generation the OIDC Certificates, see the error output below:");
				_console.WriteLine(ex.ToString());
			}

			return "";
		}
	}
}
