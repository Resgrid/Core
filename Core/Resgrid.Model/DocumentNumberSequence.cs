using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One department document-number sequence (registry M0268): the last sequence issued for a document kind under a custom
	/// pattern in one <see cref="CallNumberScope"/>, and the department's raised starting point for it. Built-in numbers keep
	/// their own counters (InvoiceNumberSequences, RmsPreventionSequences ...); only the repository's atomic statements write it.
	/// </summary>
	public class DocumentNumberSequence : IEntity
	{
		[Required]
		public int DepartmentId { get; set; }

		/// <summary>A <see cref="DocumentNumberKinds"/> key.</summary>
		[Required]
		public string Kind { get; set; }

		/// <summary><see cref="CallNumberScope.Key"/>, e.g. "INV-2027-#".</summary>
		[Required]
		public string ScopeKey { get; set; }

		/// <summary>The last sequence issued; the next document receives one more.</summary>
		public int LastSequence { get; set; }

		/// <summary>The department's raised next number for this scope (0 = none).</summary>
		public int FloorSequence { get; set; }

		public DateTime? FloorSetOn { get; set; }

		public string FloorSetByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "DocumentNumberSequences";

		[NotMapped]
		public string IdName => "DepartmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentId; }
			set { DepartmentId = (int)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
