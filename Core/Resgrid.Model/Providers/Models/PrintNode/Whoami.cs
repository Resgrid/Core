using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Model.Providers.Models.PrintNode
{
	public partial class Whoami
	{
		[JsonProperty("ApiKeys")]
		public List<object> ApiKeys { get; set; }

		[JsonProperty("Tags")]
		public List<object> Tags { get; set; }

		[JsonProperty("canCreateSubAccounts")]
		public bool CanCreateSubAccounts { get; set; }

		[JsonProperty("childAccounts")]
		public List<object> ChildAccounts { get; set; }

		[JsonProperty("connected")]
		public List<long> Connected { get; set; }

		[JsonProperty("creatorEmail")]
		public object CreatorEmail { get; set; }

		[JsonProperty("creatorRef")]
		public object CreatorRef { get; set; }

		[JsonProperty("credits")]
		public object Credits { get; set; }

		[JsonProperty("email")]
		public string Email { get; set; }

		[JsonProperty("firstname")]
		public string Firstname { get; set; }

		[JsonProperty("id")]
		public long Id { get; set; }

		[JsonProperty("lastname")]
		public string Lastname { get; set; }

		[JsonProperty("numComputers")]
		public long NumComputers { get; set; }

		[JsonProperty("permissions")]
		public List<string> Permissions { get; set; }

		[JsonProperty("state")]
		public string State { get; set; }

		[JsonProperty("totalPrints")]
		public long TotalPrints { get; set; }

		[JsonProperty("versions")]
		public List<object> Versions { get; set; }
	}
}
