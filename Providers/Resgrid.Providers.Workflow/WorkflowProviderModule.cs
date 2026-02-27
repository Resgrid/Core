using Autofac;
using Resgrid.Model.Providers;
using Resgrid.Providers.Workflow.Executors;

namespace Resgrid.Providers.Workflow
{
	public class WorkflowProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			// Register all action executors as both their concrete type and IWorkflowActionExecutor
			builder.RegisterType<SmtpEmailExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<TwilioSmsExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<HttpApiExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<FtpFileExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<SftpFileExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<S3FileExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<TeamsMessageExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<SlackMessageExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<DiscordMessageExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<AzureBlobExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<BoxFileExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();
			builder.RegisterType<DropboxFileExecutor>().As<IWorkflowActionExecutor>().InstancePerLifetimeScope();

			builder.RegisterType<WorkflowActionExecutorFactory>().As<IWorkflowActionExecutorFactory>().InstancePerLifetimeScope();
		}
	}
}

