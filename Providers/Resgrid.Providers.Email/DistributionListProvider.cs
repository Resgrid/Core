using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus;
using MimeKit;

namespace Resgrid.Providers.EmailProvider
{
	public class DistributionListProvider : IDistributionListProvider
	{
		private readonly IEventAggregator _eventAggregator;

		public DistributionListProvider(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;
		}

		public List<MimeMessage> GetNewMessagesFromMailbox(DistributionList distributionList)
		{
			var allMessages = new List<MimeMessage>();

			// TODO: Remove me

			return allMessages;
		}
	}
}
