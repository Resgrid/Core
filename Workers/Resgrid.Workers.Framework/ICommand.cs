using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public interface ICommand<in T>// where T : QueueItem
	{
		void Run(T message);
		bool Continue { get; set; }
	}
}