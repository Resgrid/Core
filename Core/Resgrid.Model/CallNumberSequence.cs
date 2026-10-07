using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One department call-number sequence (registry M0264): the last sequence issued in a <see cref="CallNumberScope"/> and
	/// the department's raised starting point for it. Only the repository's atomic statements write it.
	/// </summary>
	public class CallNumberSequence : IEntity
	{
		[Required]
		public int DepartmentId { get; set; }

		/// <summary><see cref="CallNumberScope.Key"/>, e.g. "26-#" for the legacy pattern in 2026.</summary>
		[Required]
		public string ScopeKey { get; set; }

		/// <summary>The last sequence issued; the next call receives one more.</summary>
		public int LastSequence { get; set; }

		/// <summary>The department's raised next number for this scope (0 = none); renumbering a year starts here.</summary>
		public int FloorSequence { get; set; }

		public DateTime? FloorSetOn { get; set; }

		public string FloorSetByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "CallNumberSequences";

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
