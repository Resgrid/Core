using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Security
{
	/// <summary>Department API keys: the list, the create form and, right after creating one, the key itself (shown once).</summary>
	public class ApiKeysView
	{
		public Department Department { get; set; }

		public List<DepartmentApiKey> Keys { get; set; } = new List<DepartmentApiKey>();

		public string ApiBaseUrl { get; set; }

		public string Name { get; set; }

		public List<string> SelectedScopes { get; set; } = new List<string>();

		public int ExpiresInDays { get; set; } = 90;

		public string AllowedIpRanges { get; set; }

		/// <summary>The expiry choices offered, in days, up to the configured maximum lifetime.</summary>
		public List<int> ExpiryChoices { get; set; } = new List<int>();

		/// <summary>Set only on the response that created a key: the key in clear, never stored.</summary>
		public string NewKey { get; set; }

		public string NewKeyName { get; set; }

		public string ErrorMessage { get; set; }

		public string SuccessMessage { get; set; }
	}
}
