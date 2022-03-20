using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;

namespace Resgrid.Model.Identity
{
	public class IdentityUserLoginInfo : IEntity
	{
		public string UserId { get; set; }

		/// <summary>
		/// Gets or sets the provider for this instance of <see cref="UserLoginInfo"/>.
		/// </summary>
		/// <value>The provider for the this instance of <see cref="UserLoginInfo"/></value>
		/// <remarks>
		/// Examples of the provider may be Local, Facebook, Google, etc.
		/// </remarks>
		public string LoginProvider { get; set; }

		/// <summary>
		/// Gets or sets the unique identifier for the user identity user provided by the login provider.
		/// </summary>
		/// <value>
		/// The unique identifier for the user identity user provided by the login provider.
		/// </value>
		/// <remarks>
		/// This would be unique per provider, examples may be @microsoft as a Twitter provider key.
		/// </remarks>
		public string ProviderKey { get; set; }

		/// <summary>
		/// Gets or sets the display name for the provider.
		/// </summary>
		/// <value>
		/// The display name for the provider.
		/// </value>
		public string ProviderDisplayName { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UserId; }
			set { UserId = (string)value; }
		}

		[NotMapped]
		public string TableName => "AspNetUserLogins";

		[NotMapped]
		public string IdName => "UserId";

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public int IdType => 0;

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
