namespace Resgrid.Model.Providers
{
	public interface IEmailMarketingProvider
	{
		void Unsubscribe(string emailAddress);
		void SubscribeUserToAdminList(string firstName, string lastName, string emailAddress);
		void SubscribeUserToUsersList(string firstName, string lastName, string emailAddress);
		void IncreaseStatusPageMetric(string metric);
	}
}
