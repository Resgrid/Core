using ProtoBuf;
using System;

namespace Resgrid.Model.Identity
{
	public class IdentityUserLogin : IdentityUserLogin<string> { }


	[ProtoContract]
	public class IdentityUserLogin<TKey> where TKey : IEquatable<TKey>
	{
		public virtual TKey Id { get; set; }

		/// <summary>
		/// Gets or sets the login provider for the login (e.g. facebook, google)
		/// </summary>
		[ProtoMember(1)]
		public virtual string LoginProvider { get; set; }

		/// <summary>
		/// Gets or sets the unique provider identifier for this login.
		/// </summary>
		[ProtoMember(2)]
		public virtual string ProviderKey { get; set; }

		/// <summary>
		/// Gets or sets the friendly name used in a UI for this login.
		/// </summary>
		[ProtoMember(3)]
		public virtual string ProviderDisplayName { get; set; }

		/// <summary>
		/// Gets or sets the of the primary key of the user associated with this login.
		/// </summary>
		[ProtoMember(4)]
		public virtual TKey UserId { get; set; }
	}
}
