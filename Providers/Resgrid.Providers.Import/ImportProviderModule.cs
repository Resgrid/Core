using Autofac;
using Resgrid.Model.Onboarding;
using Resgrid.Providers.Import.Catalog;
using Resgrid.Providers.Import.Helpdesk;
using Resgrid.Providers.Import.Readers;
using Resgrid.Providers.Import.Storage;
using Resgrid.Providers.Import.Templates;

namespace Resgrid.Providers.Import
{
	/// <summary>Department Import infrastructure (wizard plan section 18.1): readers, the RustFS artifact store, templates, catalog, helpdesk.</summary>
	public class ImportProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterInstance(ImportCatalog.Current).As<IImportCatalog>().SingleInstance();
			builder.RegisterType<RustFsImportArtifactStore>().As<IImportArtifactStore>().SingleInstance();
			builder.RegisterType<ImportArtifactCipher>().As<IImportArtifactCipher>().SingleInstance();
			builder.RegisterType<ImportFileReader>().As<IImportFileReader>().SingleInstance();
			builder.RegisterType<ImportTemplateWriter>().As<IImportTemplateWriter>().SingleInstance();
			builder.RegisterType<FreshdeskHelpdeskProvider>().As<IHelpdeskProvider>().SingleInstance();
		}
	}
}
