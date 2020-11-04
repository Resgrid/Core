using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IShortenUrlProvider
	{
		Task<bool> CheckAccessToken();
		Task<bool> CheckAccessTokenAsync();
		Task<string> Shorten(string long_url);
		Task<string> ShortenAsync(string long_url);
	}
}
