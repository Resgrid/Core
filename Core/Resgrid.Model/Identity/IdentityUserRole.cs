using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;

namespace Resgrid.Model.Identity
{
	public class IdentityUserRole : IdentityUserRole<string>, IEntity
	{
		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return Id; }
			set { Id = (int)value; }
		}

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string TableName => "AspNetUserRoles";

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public string IdName => "Id";

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public int IdType => 0;

		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

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
