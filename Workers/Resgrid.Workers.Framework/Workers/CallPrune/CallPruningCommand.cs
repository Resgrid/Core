using System.Threading.Tasks;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework
{
	public class CallPruningCommand : ICommand<CallPruneQueueItem>
	{
		public CallPruningCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(CallPruneQueueItem item)
		{
			var logic = new CallPruneLogic();
			await logic.Process(item);

			return true;
		}
	}
}
