using Newtonsoft.Json;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooCreateUserRequest
	{
		[JsonProperty("email")]
		public string EmailAddress { get; set; }

		[JsonProperty("first_name")]
		public string FirstName { get; set; }

		[JsonProperty("last_name")]
		public string LastName { get; set; }

		[JsonProperty("username")]
		public string Username { get; set; }

		[JsonProperty("password")]
		public string Password { get; set; }
	}
}
