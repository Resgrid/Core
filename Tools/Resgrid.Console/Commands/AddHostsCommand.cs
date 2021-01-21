using Resgrid.Console.Args;
using System;
using System.IO;
using System.Text;
using Consolas2.Core;

namespace Resgrid.Console.Commands
{
	public class AddHostsCommand : Command
	{
		private readonly IConsole _console;

		public AddHostsCommand(IConsole console)
		{
			_console = console;
		}

		public string Execute(AddHostsArgs args)
		{
			_console.WriteLine("Adding Resgrid local Urls to the hostfile");
			_console.WriteLine("Please Wait...");

			string webUrl = "127.0.0.1 resgrid.local";
			string apiUrl = "127.0.0.1 resgridapi.local";

			try
			{
				WriteRedirectToHostFile(webUrl);
				WriteRedirectToHostFile(apiUrl);

				_console.WriteLine("Successfully updated the Hostfile with the Resgrid Loopback urls!");
			}
			catch (Exception ex)
			{
				_console.WriteLine("There was an error trying to update the hostfile, see the error output below:");
				_console.WriteLine(ex.ToString());
			}

			return "";
		}

		private void WriteRedirectToHostFile(string entry)
		{
			string text = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers/etc/hosts"), Encoding.UTF8);
			if (!text.Contains(entry))
			{
				using (StreamWriter w = File.AppendText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers/etc/hosts")))
				{
					w.WriteLine(entry);
				}
			}
		}
	}
}
