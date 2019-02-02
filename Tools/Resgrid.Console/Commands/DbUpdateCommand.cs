using Consolas.Core;
using Resgrid.Console.Args;

namespace Resgrid.Console.Commands
{
    public class DbUpdateCommand : Command
    {
	    private readonly IConsole _console;

	    public DbUpdateCommand(IConsole console)
	    {
		    _console = console;
	    }

		public string Execute(HelpArgs args)
        {

	        return "";
		}
    }
}
