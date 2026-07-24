using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class UnitTrackingCredential : IEntity
	{
		[Key]
		[Required]
		[MaxLength(128)]
		public string UnitTrackingCredentialId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UnitTrackingDeviceId { get; set; }

		[ForeignKey(nameof(UnitTrackingDeviceId))]
		public virtual UnitTrackingDevice UnitTrackingDevice { get; set; }

		[Required]
		public int AuthMode { get; set; }

		[MaxLength(128)]
		public string HeaderName { get; set; }

		[MaxLength(128)]
		public string BasicUsername { get; set; }

		[Required]
		[MaxLength(20)]
		public string KeyPrefix { get; set; }

		[Required]
		[MaxLength(64)]
		[JsonIgnore]
		public string SecretHash { get; set; }

		public DateTime ValidFrom { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime? RevokedOn { get; set; }

		public DateTime? LastUsedOn { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => UnitTrackingCredentialId;
			set => UnitTrackingCredentialId = (string)value;
		}

		[NotMapped]
		public string TableName => "UnitTrackingCredentials";

		[NotMapped]
		public string IdName => "UnitTrackingCredentialId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "UnitTrackingDevice" };
	}
}
