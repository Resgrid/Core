using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IPushLogsService
	{
		Task<PushLog> LogPushResult(string deviceConnectionStatus, string httpStatusCode, string messageId,
			string notificationStatus, string subscriptionStatus, string channelUri, Exception exception,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
