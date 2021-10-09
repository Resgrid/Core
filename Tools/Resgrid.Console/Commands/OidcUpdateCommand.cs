using Resgrid.Console.Args;
using System;
using Consolas2.Core;
using Resgrid.Workers.Framework;
using Resgrid.Model.Repositories;
using Autofac;
using System.Data.SqlClient;
using Resgrid.Config;

namespace Resgrid.Console.Commands
{
	public class OidcUpdateCommand : Command
	{
		private readonly IConsole _console;

		public OidcUpdateCommand(IConsole console)
		{
			_console = console;
		}

		public string Execute(OidcUpdateArgs args)
		{
			_console.WriteLine("Starting the Resgrid OIDC DB Update Process");
			_console.WriteLine("Please Wait...");

			try
			{
				SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(OidcConfig.ConnectionString);
				_console.WriteLine($"Using Database: {builder.InitialCatalog} on Server: {builder.DataSource}");

				var oidcRepository = Bootstrapper.GetKernel().Resolve<IOidcRepository>();
				bool result = oidcRepository.UpdateOidcDatabase();

				if (result)
					_console.WriteLine("Completed updating the Resgrid OIDC DB!");
				else
					_console.WriteLine("Process did not complete with a success code. OIDC DB may have been properly updated, please check it and ensure everything is there.");
			}
			catch (Exception ex)
			{
				_console.WriteLine("There was an error trying to update the Resgrid OIDC DB, see the error output below:");
				_console.WriteLine(ex.ToString());
			}

			return "";
		}
	}
}
