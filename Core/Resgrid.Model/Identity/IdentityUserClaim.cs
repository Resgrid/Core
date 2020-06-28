using ProtoBuf;
using System;

namespace Resgrid.Model.Identity
{
	public class IdentityUserClaim : IdentityUserClaim<string> { }


	/// <summary>
	/// Represents a claim that a user possesses. 
	/// </summary>
	/// <typeparam name="TKey">The type used for the primary key for this user that possesses this claim.</typeparam>
	[ProtoContract]
	public class IdentityUserClaim<TKey> where TKey : IEquatable<TKey>
	{
		/// <summary>
		/// Gets or sets the identifier for this user claim.
		/// </summary>
		[ProtoMember(1)]
		public virtual int Id { get; set; }

		/// <summary>
		/// Gets or sets the of the primary key of the user associated with this claim.
		/// </summary>
		[ProtoMember(2)]
		public virtual TKey UserId { get; set; }

		/// <summary>
		/// Gets or sets the claim type for this claim.
		/// </summary>
		[ProtoMember(3)]
		public virtual string ClaimType { get; set; }

		/// <summary>
		/// Gets or sets the claim value for this claim.
		/// </summary>
		[ProtoMember(4)]
		public virtual string ClaimValue { get; set; }
	}
}
