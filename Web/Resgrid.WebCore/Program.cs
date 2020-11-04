using System.IO;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Resgrid.Web
{
		public class Program
		{
				//public static void Main(string[] args)
				//{
				//		var host = new WebHostBuilder()
				//				.UseKestrel()
				//				.UseContentRoot(Directory.GetCurrentDirectory())
				//				.UseIISIntegration()
				//				.UseStartup<Startup>()
				//				.Build();

				//		host.Run();
				//}

				public static void Main(string[] args)
				{
					CreateHostBuilder(args).Build().Run();
				}

				public static IHostBuilder CreateHostBuilder(string[] args) =>
					Host.CreateDefaultBuilder(args)
						.UseServiceProviderFactory(new AutofacServiceProviderFactory())
						.UseContentRoot(Directory.GetCurrentDirectory())
						.ConfigureLogging(logging =>
						{
							logging.ClearProviders();
							logging.AddConsole();
						})
						//.UseIISIntegration()
						.ConfigureWebHostDefaults(webBuilder =>
						{
							webBuilder.UseStartup<Startup>();
						});
		}
}
