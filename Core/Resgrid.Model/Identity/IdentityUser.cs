using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;

namespace Resgrid.Model.Identity
{
	/// <summary>
	/// The default implementation of <see cref="IdentityUser{TKey}"/> which uses a string as a primary key.
	/// </summary>
	[ProtoContract]
	public class IdentityUser : IdentityUser<string>, IEntity
	{
		/// <summary>
		/// Initializes a new instance of <see cref="IdentityUser"/>.
		/// </summary>
		/// <remarks>
		/// The Id property is initialized to from a new GUID string value.
		/// </remarks>
		public IdentityUser()
		{
			Id = Guid.NewGuid().ToString().ToUpper();
		}

		/// <summary>
		/// Initializes a new instance of <see cref="IdentityUser"/>.
		/// </summary>
		/// <param name="userName">The user name.</param>
		/// <remarks>
		/// The Id property is initialized to from a new GUID string value.
		/// </remarks>
		public IdentityUser(string userName) : this()
		{
			UserName = userName;
		}

		/// <summary>
		/// </summary>
		/// Gets or sets the primary key for this user.
		[ProtoMember(1)]
		public override string Id { get; set; }

		/// <summary>
		/// Gets or sets the user name for this user.
		/// </summary>
		[ProtoMember(2)]
		public override string UserName { get; set; }

		/// <summary>
		/// Gets or sets the normalized user name for this user.
		/// </summary>
		//[ProtoMember(3)]
		public override string NormalizedUserName { get; set; }

		/// <summary>
		/// Gets or sets the email address for this user.
		/// </summary>
		[ProtoMember(3)]
		public override string Email { get; set; }

		/// <summary>
		/// Gets or sets the normalized email address for this user.
		/// </summary>
		//[ProtoMember(5)]
		public override string NormalizedEmail { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if a user has confirmed their email address.
		/// </summary>
		/// <value>True if the email address has been confirmed, otherwise false.</value>
		//[ProtoMember(6)]
		public override bool EmailConfirmed { get; set; }

		/// <summary>
		/// Gets or sets a salted and hashed representation of the password for this user.
		/// </summary>
		//[ProtoMember(7)]
		public override string PasswordHash { get; set; }

		/// <summary>
		/// A random value that must change whenever a users credentials change (password changed, login removed)
		/// </summary>
		//[ProtoMember(8)]
		public override string SecurityStamp { get; set; }

		/// <summary>
		/// A random value that must change whenever a user is persisted to the store
		/// </summary>
		//[ProtoMember(9)]
		public override string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

		/// <summary>
		/// Gets or sets a telephone number for the user.
		/// </summary>
		[ProtoMember(4)]
		public override string PhoneNumber { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if a user has confirmed their telephone address.
		/// </summary>
		/// <value>True if the telephone number has been confirmed, otherwise false.</value>
		//[ProtoMember(11)]
		public override bool PhoneNumberConfirmed { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if two factor authentication is enabled for this user.
		/// </summary>
		/// <value>True if 2fa is enabled, otherwise false.</value>
		[ProtoMember(5)]
		public override bool TwoFactorEnabled { get; set; }

		/// <summary>
		/// Gets or sets the date and time, in UTC, when any user lockout ends.
		/// </summary>
		/// <remarks>
		/// A value in the past means the user is not locked out.
		/// </remarks>
		//[ProtoMember(6)]
		public override DateTimeOffset? LockoutEnd { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if this user is locked out.
		/// </summary>
		/// <value>True if the user is locked out, otherwise false.</value>
		[ProtoMember(7)]
		public override bool LockoutEnabled { get; set; }

		/// <summary>
		/// Gets or sets the number of failed login attempts for the current user.
		/// </summary>
		//[ProtoMember(15)]
		public override int AccessFailedCount { get; set; }

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string UserId
		{
			get
			{
				return Id;
			}
			set
			{
				Id = value;
			}
		}

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string SecurityQuestion { get; set; }

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string SecurityAnswer { get; set; }

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string SecurityAnswerSalt { get; set; }

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public DateTime CreateDate { get; set; }

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return Id; }
			set { Id = (string)value; }
		}

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string TableName => "AspNetUsers";

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string IdName => "Id";

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public int IdType => 0;

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}


	/// <summary>
	/// Represents a user in the identity system
	/// </summary>
	/// <typeparam name="TKey">The type used for the primary key for the user.</typeparam>
	[ProtoContract]
	public class IdentityUser<TKey> where TKey : IEquatable<TKey>
	{
		/// <summary>
		/// Initializes a new instance of <see cref="IdentityUser{TKey}"/>.
		/// </summary>
		public IdentityUser() { }

		/// <summary>
		/// Initializes a new instance of <see cref="IdentityUser{TKey}"/>.
		/// </summary>
		/// <param name="userName">The user name.</param>
		public IdentityUser(string userName) : this()
		{
			UserName = userName;
		}

		/// <summary>
		/// </summary>
		/// Gets or sets the primary key for this user.
		[ProtoMember(1)]
		public virtual TKey Id { get; set; }

		/// <summary>
		/// Gets or sets the user name for this user.
		/// </summary>
		[ProtoMember(2)]
		public virtual string UserName { get; set; }

		/// <summary>
		/// Gets or sets the normalized user name for this user.
		/// </summary>
		//[ProtoMember(3)]
		public virtual string NormalizedUserName { get; set; }

		/// <summary>
		/// Gets or sets the email address for this user.
		/// </summary>
		[ProtoMember(3)]
		public virtual string Email { get; set; }

		/// <summary>
		/// Gets or sets the normalized email address for this user.
		/// </summary>
		//[ProtoMember(5)]
		public virtual string NormalizedEmail { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if a user has confirmed their email address.
		/// </summary>
		/// <value>True if the email address has been confirmed, otherwise false.</value>
		//[ProtoMember(6)]
		public virtual bool EmailConfirmed { get; set; }

		/// <summary>
		/// Gets or sets a salted and hashed representation of the password for this user.
		/// </summary>
		//[ProtoMember(7)]
		public virtual string PasswordHash { get; set; }

		/// <summary>
		/// A random value that must change whenever a users credentials change (password changed, login removed)
		/// </summary>
		//[ProtoMember(8)]
		public virtual string SecurityStamp { get; set; }

		/// <summary>
		/// A random value that must change whenever a user is persisted to the store
		/// </summary>
		//[ProtoMember(9)]
		public virtual string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

		/// <summary>
		/// Gets or sets a telephone number for the user.
		/// </summary>
		[ProtoMember(4)]
		public virtual string PhoneNumber { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if a user has confirmed their telephone address.
		/// </summary>
		/// <value>True if the telephone number has been confirmed, otherwise false.</value>
		//[ProtoMember(11)]
		public virtual bool PhoneNumberConfirmed { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if two factor authentication is enabled for this user.
		/// </summary>
		/// <value>True if 2fa is enabled, otherwise false.</value>
		[ProtoMember(5)]
		public virtual bool TwoFactorEnabled { get; set; }

		/// <summary>
		/// Gets or sets the date and time, in UTC, when any user lockout ends.
		/// </summary>
		/// <remarks>
		/// A value in the past means the user is not locked out.
		/// </remarks>
		[ProtoMember(6)]
		public virtual DateTimeOffset? LockoutEnd { get; set; }

		/// <summary>
		/// Gets or sets a flag indicating if this user is locked out.
		/// </summary>
		/// <value>True if the user is locked out, otherwise false.</value>
		[ProtoMember(7)]
		public virtual bool LockoutEnabled { get; set; }

		/// <summary>
		/// Gets or sets the number of failed login attempts for the current user.
		/// </summary>
		//[ProtoMember(15)]
		public virtual int AccessFailedCount { get; set; }

		/// <summary>
		/// Navigation property for the roles this user belongs to.
		/// </summary>
		//[ProtoMember(16)]
		public virtual ICollection<IdentityUserRole> Roles { get; } = new List<IdentityUserRole>();

		/// <summary>
		/// Navigation property for the claims this user possesses.
		/// </summary>
		//[ProtoMember(17)]
		public virtual ICollection<IdentityUserClaim> Claims { get; } = new List<IdentityUserClaim>();

		/// <summary>
		/// Navigation property for this users login accounts.
		/// </summary>
		//[ProtoMember(18)]
		public virtual ICollection<IdentityUserLogin> Logins { get; } = new List<IdentityUserLogin>();

		/// <summary>
		/// Returns the username for this user.
		/// </summary>
		public override string ToString()
		{
			return UserName;
		}
	}
}
