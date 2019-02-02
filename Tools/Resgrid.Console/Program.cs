using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Consolas.Core;
using Consolas.Mustache;
using SimpleInjector;

namespace Resgrid.Console
{
	public class Program : ConsoleApp<Program>
	{
		static void Main(string[] args)
		{
			Match(args);
		}

		public override void Configure(Container container)
		{
			container.Register<IConsole, SystemConsole>();


			ViewEngines.Add<MustacheViewEngine>();
		}
	}
}
