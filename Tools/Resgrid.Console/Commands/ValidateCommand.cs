using Resgrid.Console.Args;
using System;
using Consolas2.Core;

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
			_console.WriteLine("Validating the Environment for Resgrid Operations");
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
