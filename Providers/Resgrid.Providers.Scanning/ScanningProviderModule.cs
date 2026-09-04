using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Scanning
{
	/// <summary>
	/// Registers the ClamAV scanner as the Records attachment scanner. Loaded after ServicesModule, so it
	/// replaces the null scanner registered there; with AttachmentScanningConfig.Enabled off it still reports
	/// Skipped, which keeps the no-scanner behaviour byte for byte.
	/// </summary>
	public class ScanningProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<ClamAvAttachmentScanner>().As<IRecordAttachmentScanner>().InstancePerLifetimeScope();
		}
	}
}
