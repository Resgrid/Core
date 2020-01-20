using System.Xml.Linq;

namespace Resgrid.Providers.NumberProvider
{
	public static class LaMLResponse
	{
		public static class Message
		{
			public static string Respond(string message)
			{
				XElement laml =	new XElement("Response",
									new XElement("Message", message)
								);


				return laml.ToString();
			}
		}
	}
}
