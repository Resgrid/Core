using System.Collections.Generic;
using MimeKit;

namespace Resgrid.Model.Providers
{
	public interface IDistributionListProvider
	{
		List<MimeMessage> GetNewMessagesFromMailbox(DistributionList distributionList);
	}
}