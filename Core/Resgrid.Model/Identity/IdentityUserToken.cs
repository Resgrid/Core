using ProtoBuf;
using System;

namespace Resgrid.Model.Identity
{
	public class IdentityUserToken : IdentityUserToken<string> { }

	/// <summary>
	/// Represents an authentication token for a user.
	/// </summary>
	/// <typeparam name="TKey">The type of the primary key used for users.</typeparam>
	[ProtoContract]
	public class IdentityUserToken<TKey> where TKey : IEquatable<TKey>
	{
		/// <summary>
		/// Gets or sets the primary key of the user that the token belongs to.
		/// </summary>
		[ProtoMember(1)]
		public virtual TKey UserId { get; set; }

		/// <summary>
		/// Gets or sets the LoginProvider this token is from.
		/// </summary>
		[ProtoMember(2)]
		public virtual string LoginProvider { get; set; }

		/// <summary>
		/// Gets or sets the name of the token.
		/// </summary>
		[ProtoMember(3)]
		public virtual string Name { get; set; }

		/// <summary>
		/// Gets or sets the token value.
		/// </summary>
		[ProtoMember(4)]
		public virtual string Value { get; set; }
	}
}
