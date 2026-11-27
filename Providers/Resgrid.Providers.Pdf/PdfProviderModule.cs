using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.PdfProvider
{
	public class PdfProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<NRecoProvider>().As<IPdfProvider>().InstancePerLifetimeScope();
			builder.RegisterType<PrintNodeProvider>().As<IPrinterProvider>().InstancePerLifetimeScope();

			// e-Signature S1 (L3 sealing): the resgrid-sealing EU DSS sidecar, the signing key (PKCS#12 or a remote hash signer, chosen
			// by SealingConfig.CertificateSource; blank fails closed) and the sealed-document bucket.
			builder.RegisterType<Sealing.DssRestClient>().As<IDssClient>().SingleInstance();
			builder.RegisterType<Sealing.NotConfiguredRemoteHashSigner>().As<IRemoteHashSigner>().SingleInstance().PreserveExistingDefaults();
			builder.RegisterType<Sealing.ConfiguredSigningKeyProvider>().As<ISigningKeyProvider>().SingleInstance();
			builder.RegisterType<Sealing.S3SealedDocumentStore>().As<ISealedDocumentStore>().SingleInstance();

			// e-Signature S2 (L4 external signers): Documenso CE, optional (blank ESignConfig.DocumensoBaseUrl = unavailable).
			builder.RegisterType<ESign.DocumensoESignProvider>().As<IESignProvider>().SingleInstance();
		}
	}
}
