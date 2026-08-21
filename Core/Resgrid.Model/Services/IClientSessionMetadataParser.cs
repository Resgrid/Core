using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	public interface IClientSessionMetadataParser
	{
		ClientSessionMetadata Parse(string userAgent, string deviceName = null, string deviceType = null,
			string operatingSystem = null, string browser = null, string applicationVersion = null);
	}
}
