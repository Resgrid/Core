using System;
using System.Threading;
using System.Threading.Tasks;

namespace GoogleMapsApi
{
    public static class TaskFactoryExtensions
    {
        public static Task Delay(this TaskFactory task, TimeSpan dueTime, CancellationToken cancellationToken)
        {
            if (dueTime.TotalMilliseconds <= 0) throw new ArgumentOutOfRangeException("dueTime");

            var tcs = new TaskCompletionSource<bool>();
            var timer = new Timer(self =>
            {
                ((Timer)self).Dispose();
                tcs.TrySetResult(true);
            });
            timer.Change(dueTime, TimeSpan.FromMilliseconds(-1));
            return tcs.Task;
        }
    }
}