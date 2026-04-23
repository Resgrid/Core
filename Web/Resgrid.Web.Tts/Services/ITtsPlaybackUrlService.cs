using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Tts.Services
{
	public interface ITtsPlaybackUrlService
	{
		Uri CreatePlaybackUrl(HttpRequest? request, string hash);
	}
}
