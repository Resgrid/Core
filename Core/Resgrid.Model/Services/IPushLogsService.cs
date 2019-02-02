using System;

namespace Resgrid.Model.Services
{
	public interface IPushLogsService
	{
		PushLog LogPushResult(string deviceConnectionStatus, string httpStatusCode, string messageId, string notificationStatus,
													string subscriptionStatus, string channelUri);

		PushLog LogPushResult(string deviceConnectionStatus, string httpStatusCode, string messageId, string notificationStatus,
													string subscriptionStatus, string channelUri, Exception exception);
	}
}