using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model.Identity
{
	/// <summary>
	/// Represents a role in the identity system
	/// </summary>
	public class IdentityRole : IdentityRole<string>, IEntity
	{
		/// <summary>
		///     Constructor
		/// </summary>
		public IdentityRole()
		{
			Id = Guid.NewGuid().ToString();
		}

		/// <summary>
		///     Constructor
		/// </summary>
		/// <param name="roleName"></param>
		public IdentityRole(string roleName) : this()
		{
			Name = roleName;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return Id; }
			set { Id = (string)value; }
		}

		[NotMapped]
		public string TableName => "AspNetRoles";

		[NotMapped]
		public string IdName => "Id";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}


	/// <summary>
	///     Represents a Role entity
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	[ProtoContract]
	public class IdentityRole<TKey> where TKey : IEquatable<TKey>
	{
		/// <summary>
		/// Initializes a new instance of <see cref="IdentityRole{TKey}"/>.
		/// </summary>
		public IdentityRole() { }

		/// <summary>
		/// Initializes a new instance of <see cref="IdentityRole{TKey}"/>.
		/// </summary>
		/// <param name="roleName">The role name.</param>
		public IdentityRole(string roleName) : this()
		{
			Name = roleName;
		}

		/// <summary>
		/// Navigation property for the users in this role.
		/// </summary>
		[ProtoMember(1)]
		public virtual ICollection<IdentityUserRole> Users { get; } = new List<IdentityUserRole>();

		/// <summary>
		/// Navigation property for claims in this role.
		/// </summary>
		[ProtoMember(2)]
		public virtual ICollection<IdentityRoleClaim> Claims { get; } = new List<IdentityRoleClaim>();

		/// <summary>
		/// Gets or sets the primary key for this role.
		/// </summary>
		[ProtoMember(3)]
		public virtual TKey Id { get; set; }

		/// <summary>
		/// Gets or sets the name for this role.
		/// </summary>
		[ProtoMember(4)]
		public virtual string Name { get; set; }

		/// <summary>
		/// Gets or sets the normalized name for this role.
		/// </summary>
		[ProtoMember(5)]
		public virtual string NormalizedName { get; set; }

		/// <summary>
		/// A random value that should change whenever a role is persisted to the store
		/// </summary>
		[ProtoMember(6)]
		public virtual string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

		/// <summary>
		/// Returns the name of the role.
		/// </summary>
		/// <returns>The name of the role.</returns>
		public override string ToString()
		{
			return Name;
		}
	}
}
