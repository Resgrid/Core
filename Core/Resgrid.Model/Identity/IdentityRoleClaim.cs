using ProtoBuf;
using System;

namespace Resgrid.Model.Identity
{
	public class IdentityRoleClaim : IdentityRoleClaim<string>
	{
		public IdentityRoleClaim()
		{
			//Added to make the default constructor creates a new Id value
			Id = Guid.NewGuid().ToString();
		}
	}


	/// <summary>
	/// Represents a claim that is granted to all users within a role.
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	[ProtoContract]
	public class IdentityRoleClaim<TKey> where TKey : IEquatable<TKey>
	{
		/// <summary>
		/// Gets or sets the identifier for this role claim.
		/// </summary>
		[ProtoMember(1)]
		public virtual TKey Id { get; set; }

		/// <summary>
		/// Gets or sets the of the primary key of the role associated with this claim.
		/// </summary>
		[ProtoMember(2)]
		public virtual TKey RoleId { get; set; }

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
