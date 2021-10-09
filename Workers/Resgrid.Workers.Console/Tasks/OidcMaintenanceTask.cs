using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class OidcMaintenanceTask : IQuidjiboHandler<OidcMaintenanceCommand>
	{
		public string Name => "OIDC Maintenance";
		public int Priority => 1;
		public ILogger _logger;

		public OidcMaintenanceTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(OidcMaintenanceCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}

		///// <summary>
		///// Configure the dependency injection services
		///// </summary>
		//private static IServiceProvider CreateServices()
		//{
		//	return new ServiceCollection()
		//		.AddOpenIddict()
		//		// Register the OpenIddict core components.
		//		.AddCore(options =>
		//		{
		//			// Configure OpenIddict to use the Entity Framework Core stores and models.
		//			// Note: call ReplaceDefaultEntities() to replace the default OpenIddict entities.
		//			options.UseEntityFrameworkCore()
		//			   .UseDbContext<AuthorizationDbContext>()
		//			   .ReplaceDefaultEntities<Guid>();

		//			// Enable Quartz.NET integration.
		//			//options.UseQuartz();
		//		});
		//}
	}
}
