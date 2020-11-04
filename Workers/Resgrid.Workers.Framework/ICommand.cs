using System.Threading.Tasks;

namespace Resgrid.Workers.Framework
{
	public interface ICommand<in T>// where T : QueueItem
	{
		Task<bool> Run(T message);
		bool Continue { get; set; }
	}
}
