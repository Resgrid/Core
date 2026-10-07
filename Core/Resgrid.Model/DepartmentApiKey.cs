using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A department API key (registry M0265): lets another system call a fixed set of department-level v4 endpoints
	/// without a user's credentials. Only a keyed HMAC of the key is stored; the key itself is shown once, when it is
	/// created. Every key expires, can be revoked, and carries the scopes (<see cref="DepartmentApiKeyScopes"/>) that
	/// decide which endpoints it may call.
	/// </summary>
	public class DepartmentApiKey : IEntity
	{
		[Key]
		[Required]
		[MaxLength(128)]
		public string DepartmentApiKeyId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>What the key is for, e.g. "Follow-up scheduling system".</summary>
		[Required]
		[MaxLength(100)]
		public string Name { get; set; }

		/// <summary>The public part of the key ("rgk_{prefix}_..."), shown in lists so an admin can tell keys apart.</summary>
		[Required]
		[MaxLength(20)]
		public string KeyPrefix { get; set; }

		/// <summary>Lower-case hex HMAC-SHA256 of the whole key under the server pepper.</summary>
		[Required]
		[MaxLength(64)]
		[JsonIgnore]
		public string SecretHash { get; set; }

		/// <summary>Space-separated <see cref="DepartmentApiKeyScopes"/> values.</summary>
		[Required]
		[MaxLength(500)]
		public string Scopes { get; set; }

		/// <summary>Optional: the addresses or CIDR ranges the key may be used from, one per line. Empty means any address.</summary>
		[MaxLength(1000)]
		public string AllowedIpRanges { get; set; }

		[Required]
		[MaxLength(128)]
		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>When the key stops working (UTC). Every key has one.</summary>
		public DateTime ExpiresOn { get; set; }

		public DateTime? LastUsedOn { get; set; }

		[MaxLength(64)]
		public string LastUsedIp { get; set; }

		public DateTime? RevokedOn { get; set; }

		[MaxLength(128)]
		public string RevokedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => DepartmentApiKeyId;
			set => DepartmentApiKeyId = (string)value;
		}

		[NotMapped]
		public string TableName => "DepartmentApiKeys";

		[NotMapped]
		public string IdName => "DepartmentApiKeyId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };

		/// <summary>Not revoked and not yet expired.</summary>
		public bool IsActiveAt(DateTime utcNow) => !RevokedOn.HasValue && ExpiresOn > utcNow;

		public List<string> GetScopes() => DepartmentApiKeyScopes.Parse(Scopes);
	}
}
