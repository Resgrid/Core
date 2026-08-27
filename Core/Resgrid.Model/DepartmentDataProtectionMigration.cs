using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// Durable cursor and progress record for one table of one department's bulk ADP migration
	/// (enrollment encryption, offboarding decryption, or rotation re-encryption). The ADP migration
	/// worker checkpoints the cursor in the same transaction as each batch's writes, so a crash
	/// re-processes at most one batch — which the double-encryption guard makes a no-op. Error codes
	/// are value-free; no plaintext, ciphertext or key material is ever recorded here.
	/// </summary>
	[Table("DepartmentDataProtectionMigrations")]
	public class DepartmentDataProtectionMigration : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentDataProtectionMigrationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>DepartmentDataProtectionMigrationKind value.</summary>
		[Required]
		public int Kind { get; set; }

		/// <summary>Protected-field catalog version this run migrates to.</summary>
		[Required]
		public int CatalogVersion { get; set; }

		/// <summary>Target department key version for enrollment/rotation runs; null for offboarding.</summary>
		public int? TargetKeyVersion { get; set; }

		/// <summary>Cataloged table this row tracks (one row per table per run).</summary>
		[Required]
		[MaxLength(128)]
		public string TargetTable { get; set; }

		/// <summary>Serialized resume cursor (last processed key of TargetTable), engine-agnostic string form.</summary>
		[MaxLength(256)]
		public string Cursor { get; set; }

		public long RowsTotal { get; set; }

		public long RowsProcessed { get; set; }

		/// <summary>Rows skipped because they already carried a matching rgdp envelope (idempotent re-run).</summary>
		public long RowsAlreadyProtected { get; set; }

		/// <summary>Plaintext values seen on the decrypt path (passed through untouched) — anomaly counter.</summary>
		public long RowsAnomalous { get; set; }

		/// <summary>DepartmentDataProtectionVerificationState value.</summary>
		public int VerificationState { get; set; }

		public int Attempts { get; set; }

		/// <summary>Value-free machine error code for the last failure; never exception text or content.</summary>
		[MaxLength(64)]
		public string LastErrorCode { get; set; }

		[MaxLength(128)]
		public string CorrelationId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime? CheckpointedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentDataProtectionMigrationId; }
			set { DepartmentDataProtectionMigrationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentDataProtectionMigrations";

		[NotMapped]
		public string IdName => "DepartmentDataProtectionMigrationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
