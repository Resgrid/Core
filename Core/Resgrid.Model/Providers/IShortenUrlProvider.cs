using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IShortenUrlProvider
	{
		bool CheckAccessToken();
		Task<bool> CheckAccessTokenAsync();
		string Shorten(string long_url);
		Task<string> ShortenAsync(string long_url);
	}
}
