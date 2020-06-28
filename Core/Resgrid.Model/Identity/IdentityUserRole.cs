using ProtoBuf;
using System;

namespace Resgrid.Model.Identity
{
	public class IdentityUserRole : IdentityUserRole<string> { }

	[ProtoContract]
	public class IdentityUserRole<TKey> where TKey : IEquatable<TKey>
	{
		public virtual int Id { get; set; }

		/// <summary> 
		/// Gets or sets the primary key of the user that is linked to a role. 
		/// </summary> 
		[ProtoMember(1)]
		public virtual TKey UserId { get; set; }


		/// <summary> 
		/// Gets or sets the primary key of the role that is linked to the user. 
		/// </summary> 
		[ProtoMember(2)]
		public virtual TKey RoleId { get; set; }
	}
}
