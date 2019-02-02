using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public interface IBatchCommand<in T> : ICommand<T> where T : QueueItem
	{
		void PreRun();
		void PostRun();
	}
}