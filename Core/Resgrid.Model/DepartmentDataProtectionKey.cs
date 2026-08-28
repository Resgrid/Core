using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// One version of a department's data encryption key (DEK) in its KMS-wrapped form. NEVER holds
	/// plaintext key material — the wrapped blob is only unwrapped inside the Protected Data Broker via
	/// the KMS wrap/unwrap API with the department encryption context. Rows are never deleted by
	/// ordinary rotation or offboarding; cryptographic erasure is a separate dual-controlled operation.
	/// Deliberately not cached in Redis and not protobuf cache-serializable.
	/// </summary>
	[Table("DepartmentDataProtectionKeys")]
	public class DepartmentDataProtectionKey : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentDataProtectionKeyId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Department key version referenced by the rgdp envelope header. Starts at 1.</summary>
		[Required]
		public int Version { get; set; }

		/// <summary>Base64 KMS-wrapped DEK as returned by the wrapping provider (e.g. Transit datakey/wrapped).</summary>
		[Required]
		public string WrappedKey { get; set; }

		/// <summary>Wrapping provider discriminator (e.g. "OpenBaoTransit", "AzureKeyVault", "AwsKms", "LocalDev").</summary>
		[Required]
		[MaxLength(64)]
		public string ProviderType { get; set; }

		/// <summary>Provider key reference — for OpenBao Transit: mount and key name (e.g. "transit/resgrid-dept-kek").</summary>
		[Required]
		[MaxLength(256)]
		public string ProviderKeyReference { get; set; }

		/// <summary>KEK version at the provider that wrapped this DEK (Transit key version for rewrap tracking).</summary>
		public int ProviderKeyVersion { get; set; }

		/// <summary>DepartmentDataProtectionKeyStatus value.</summary>
		public int Status { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ActivatedOn { get; set; }

		public DateTime? RetiredOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentDataProtectionKeyId; }
			set { DepartmentDataProtectionKeyId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentDataProtectionKeys";

		[NotMapped]
		public string IdName => "DepartmentDataProtectionKeyId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
