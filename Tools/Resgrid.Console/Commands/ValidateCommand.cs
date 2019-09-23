using Consolas.Core;
using Resgrid.Console.Args;
using System;

namespace Resgrid.Console.Commands
{
	public class ValidateCommand : Command
    {
	    private readonly IConsole _console;

	    public ValidateCommand(IConsole console)
	    {
		    _console = console;
	    }

		public string Execute(ValidateArgs args)
        {
			_console.WriteLine("Starting the Resgrid Database Update Process");
			_console.WriteLine("Please Wait...");

			try
			{
				
			}
			catch (Exception ex)
			{
				
			}


			return "";
		}
    }
}
