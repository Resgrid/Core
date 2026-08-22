using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("UserExternalIdentityLinks")]
	public class UserExternalIdentityLink : IEntity
	{
		[Key]
		[MaxLength(128)]
		public string UserExternalIdentityLinkId { get; set; }
		[Required, MaxLength(128)] public string UserId { get; set; }
		public int DepartmentId { get; set; }
		public int DepartmentMemberId { get; set; }
		[Required, MaxLength(128)] public string DepartmentSsoConfigId { get; set; }
		public int ProviderType { get; set; }
		[Required, MaxLength(1024)] public string Issuer { get; set; }
		[Required, MaxLength(512)] public string ExternalSubject { get; set; }
		public int LinkMethod { get; set; }
		[MaxLength(512)] public string EmailAtLink { get; set; }
		public bool IsEmailExternallyManaged { get; set; }
		public bool IsActive { get; set; }
		public DateTime LinkedOn { get; set; }
		public DateTime? LastLoginOn { get; set; }
		public DateTime? UnlinkedOn { get; set; }
		[MaxLength(128)] public string UnlinkedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => UserExternalIdentityLinkId;
			set => UserExternalIdentityLinkId = value?.ToString();
		}

		[NotMapped] public string TableName => "UserExternalIdentityLinks";
		[NotMapped] public string IdName => "UserExternalIdentityLinkId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
