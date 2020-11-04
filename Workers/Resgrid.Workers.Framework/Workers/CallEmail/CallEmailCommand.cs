using System.Threading.Tasks;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework
{
	public class CallEmailCommand : ICommand<CallEmailQueueItem>
	{
		public CallEmailCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(CallEmailQueueItem item)
		{
			var logic = new CallEmailImporterLogic();
			await logic.Process(item);
			return true;
		}
	}
}
