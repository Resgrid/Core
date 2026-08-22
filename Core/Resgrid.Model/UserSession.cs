using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("UserSessions")]
	public class UserSession : IEntity
	{
		[Key]
		[MaxLength(128)]
		public string UserSessionId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		public int? DepartmentId { get; set; }
		public long AuthenticationGeneration { get; set; }
		public int State { get; set; }
		public long StateVersion { get; set; }
		public int ClientApplication { get; set; }

		[MaxLength(128)] public string ClientInstanceIdHash { get; set; }
		[MaxLength(256)] public string DeviceName { get; set; }
		[MaxLength(128)] public string DeviceType { get; set; }
		[MaxLength(128)] public string OperatingSystem { get; set; }
		[MaxLength(128)] public string Browser { get; set; }
		[MaxLength(64)] public string ApplicationVersion { get; set; }
		public int AuthenticationMethod { get; set; }
		[MaxLength(128)] public string DepartmentSsoConfigId { get; set; }
		[MaxLength(128)] public string OpenIddictAuthorizationId { get; set; }
		[MaxLength(512)] public string WebCookieTicketKey { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime LastActiveOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		[MaxLength(64)] public string FirstIpAddress { get; set; }
		[MaxLength(64)] public string LastIpAddress { get; set; }
		[MaxLength(128)] public string LastCountry { get; set; }
		[MaxLength(128)] public string LastRegion { get; set; }
		[MaxLength(128)] public string LastCity { get; set; }
		[MaxLength(1024)] public string UserAgent { get; set; }
		public bool IsLegacyAdopted { get; set; }
		public DateTime? RevokedOn { get; set; }
		[MaxLength(128)] public string RevokedByUserId { get; set; }
		public int? RevocationReason { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => UserSessionId;
			set => UserSessionId = value?.ToString();
		}

		[NotMapped] public string TableName => "UserSessions";
		[NotMapped] public string IdName => "UserSessionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
