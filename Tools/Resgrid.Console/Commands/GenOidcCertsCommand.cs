using Resgrid.Console.Args;
using System;
using Consolas2.Core;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;

namespace Resgrid.Console.Commands
{
	public sealed class GenOidcCertsCommand(
		IConfiguration configuration,
		ILogger<GenOidcCertsCommand> logger,
		IMigrationRunner migrationRunner) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Starting the Resgrid OIDC Certification Generation Process");
			logger.LogInformation("Please Wait...");

			try
			{
				var algorithm = RSA.Create(keySizeInBits: 2048);

				var subject = new X500DistinguishedName("CN=Resgrid Encryption Certificate");
				var request = new CertificateRequest(subject, algorithm,
					HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				request.CertificateExtensions.Add(new X509KeyUsageExtension(
					X509KeyUsageFlags.KeyEncipherment, critical: true));

				var certificate = request.CreateSelfSigned(
					DateTimeOffset.UtcNow,
					DateTimeOffset.UtcNow.AddYears(5));

				var encryptionCertificate = certificate.Export(X509ContentType.Pfx, string.Empty);

				logger.LogInformation("==========================================================");
				logger.LogInformation("=                 BEGIN ENCRYPTION CERT                  =");
				logger.LogInformation("==========================================================");
				//_console.WriteLine("-----BEGIN CERTIFICATE-----");
				logger.LogInformation(Convert.ToBase64String(encryptionCertificate));
				//_console.WriteLine("-----END CERTIFICATE-----");
				logger.LogInformation("==========================================================");
				logger.LogInformation("=                  END ENCRYPTION CERT                   =");
				logger.LogInformation("==========================================================");

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

				logger.LogInformation("==========================================================");
				logger.LogInformation("=                  BEGIN SIGNING CERT                    =");
				logger.LogInformation("==========================================================");
				//_console.WriteLine("-----BEGIN CERTIFICATE-----");
				logger.LogInformation(Convert.ToBase64String(signingCertificate));
				//_console.WriteLine("-----END CERTIFICATE-----");
				logger.LogInformation("==========================================================");
				logger.LogInformation("=                   END SIGNING CERT                     =");
				logger.LogInformation("==========================================================");

				logger.LogInformation("Completed updating the Resgrid Database!");
			}
			catch (Exception ex)
			{
				logger.LogError("There was an error trying to Generation the OIDC Certificates, see the error output below:");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}
	}
}
