using Consolas2.Core;
using Resgrid.Console.Args;

namespace Resgrid.Console.Commands
{
    public class HelpCommand : Command
    {
        public string Execute(HelpArgs args)
        {
            return "Using: Resgrid.Console.exe ...";
        }
    }
}
