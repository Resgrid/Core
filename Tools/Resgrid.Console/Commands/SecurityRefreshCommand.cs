using Resgrid.Console.Args;
using System;
using Consolas2.Core;
using Resgrid.Workers.Framework.Logic;
using Amazon.Runtime.Internal.Util;
using System.Diagnostics;

namespace Resgrid.Console.Commands
{
	public class SecurityRefreshCommand : Command
	{
		private readonly IConsole _console;

		public SecurityRefreshCommand(IConsole console)
		{
			_console = console;
		}

		public string Execute(SecurityRefreshArgs args)
		{
			_console.WriteLine("Starting the Security Refresh Process");
			_console.WriteLine(DateTime.Now.ToString("MM/dd/yy H:mm:ss zzz"));
			_console.WriteLine("Please Wait...");

			try
			{
				Stopwatch timer = new Stopwatch();
				var security = new SecurityLogic();
				timer.Start();

				var result = AsyncHelpers.RunSync<Tuple<bool, string>>(() => security.UpdatedCachedSecurityForAllDepartments());

				timer.Stop();

				_console.WriteLine(DateTime.Now.ToString("MM/dd/yy H:mm:ss zzz"));
				_console.WriteLine($"Completed the Security Refresh Process in {timer.Elapsed.ToString("mm\\:ss\\.ff")}!");
			}
			catch (Exception ex)
			{
				_console.WriteLine("There was an error trying to run the Security Refresh Process, see the error output below:");
				_console.WriteLine(DateTime.Now.ToString("MM/dd/yy H:mm:ss zzz"));
				_console.WriteLine(ex.ToString());
			}

			return "";
		}
	}
}
