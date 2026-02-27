using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class WorkflowCredential : IEntity
	{
		public string WorkflowCredentialId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		/// <summary>User-friendly label, e.g. "Production SMTP".</summary>
		[Required]
		[MaxLength(250)]
		public string Name { get; set; }

		/// <summary>Maps to <see cref="WorkflowCredentialType"/>.</summary>
		[Required]
		public int CredentialType { get; set; }

		/// <summary>AES-encrypted JSON blob of the credential fields. Encrypted via IEncryptionService.EncryptForDepartment.</summary>
		[Required]
		public string EncryptedData { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => WorkflowCredentialId;
			set => WorkflowCredentialId = (string)value;
		}

		[NotMapped] public string TableName => "WorkflowCredentials";
		[NotMapped] public string IdName => "WorkflowCredentialId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
