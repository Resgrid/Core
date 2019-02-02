using System.Threading;
using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public interface IJob
	{
		bool OnStart(CancellationToken token);
		void Run();
		void Stop();
		JobTypes Type { get; }
	}
}
