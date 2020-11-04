using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PushLogsService : IPushLogsService
	{
		private readonly IPushLogsRepository _pushLogsRepository;

		public PushLogsService(IPushLogsRepository pushLogsRepository)
		{
			_pushLogsRepository = pushLogsRepository;
		}


		public async Task<PushLog> LogPushResult(string deviceConnectionStatus, string httpStatusCode, string messageId, string notificationStatus,
			string subscriptionStatus, string channelUri, Exception exception, CancellationToken cancellationToken = default(CancellationToken))
		{
			PushLog log = new PushLog();
			log.MessageId = messageId;
			log.Subscription = subscriptionStatus;
			log.Connection = deviceConnectionStatus;
			log.Status = httpStatusCode;
			log.Notification = notificationStatus;
			log.Exception = exception.ToString();
			log.ChannelUri = channelUri;
			log.Timestamp = DateTime.Now.ToUniversalTime();

			return await _pushLogsRepository.SaveOrUpdateAsync(log, cancellationToken);
		}
	}
}
