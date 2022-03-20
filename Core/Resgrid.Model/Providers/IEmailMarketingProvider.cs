using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IEmailMarketingProvider
	{
		Task<bool> Unsubscribe(string emailAddress);
		Task<bool> SubscribeUserToAdminList(string firstName, string lastName, string emailAddress);
		Task<bool> SubscribeUserToUsersList(string firstName, string lastName, string emailAddress);
		Task<bool> IncreaseStatusPageMetric(string metric);
	}
}
