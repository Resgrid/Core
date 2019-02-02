using System;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PushLogsService : IPushLogsService
	{
		private readonly IGenericDataRepository<PushLog> _pushLogsRepository;

		public PushLogsService(IGenericDataRepository<PushLog> pushLogsRepository)
		{
			_pushLogsRepository = pushLogsRepository;
		}

		public PushLog LogPushResult(string deviceConnectionStatus, string httpStatusCode, string messageId, string notificationStatus, string subscriptionStatus, string channelUri)
		{
			PushLog log = new PushLog();
			
			_pushLogsRepository.SaveOrUpdate(log);

			return log;
		}

		public PushLog LogPushResult(string deviceConnectionStatus, string httpStatusCode, string messageId, string notificationStatus, string subscriptionStatus, string channelUri, Exception exception)
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

			_pushLogsRepository.SaveOrUpdate(log);

			return log;
		}
	}
}