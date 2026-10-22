using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// A department's named, reusable set of requirement rows ("MUG + AMB", "Full first alarm"). Decision graph outcomes and
	/// fixed alarm levels reference a package, so a change applies everywhere it is used. A package's rows are the
	/// complete set of resources it asks for.
	/// </summary>
	[Table("ResponsePackages")]
	[ProtoContract]
	public class ResponsePackage : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int ResponsePackageId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(100)]
		[ProtoMember(3)]
		public string Name { get; set; }

		[MaxLength(500)]
		[ProtoMember(4)]
		public string Description { get; set; }

		/// <summary>Retired packages stay readable for history but cannot be picked for new outcomes.</summary>
		[ProtoMember(5)]
		public bool IsRetired { get; set; }

		[ProtoMember(6)]
		public DateTime AddedOn { get; set; }

		[MaxLength(450)]
		[ProtoMember(7)]
		public string AddedByUserId { get; set; }

		[ProtoMember(8)]
		public DateTime? UpdatedOn { get; set; }

		[MaxLength(450)]
		[ProtoMember(9)]
		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[ProtoMember(10)]
		public List<ResponsePackageRow> Rows { get; set; } = new List<ResponsePackageRow>();

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ResponsePackageId; }
			set { ResponsePackageId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ResponsePackages";

		[NotMapped]
		public string IdName => "ResponsePackageId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Rows" };
	}

	/// <summary>One row of a response package: what resources it accepts, how many, and what happens when it falls short.</summary>
	[Table("ResponsePackageRows")]
	[ProtoContract]
	public class ResponsePackageRow : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int ResponsePackageRowId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int ResponsePackageId { get; set; }

		/// <summary>Optional display label ("First due engine"); the targets' names are shown when empty.</summary>
		[MaxLength(100)]
		[ProtoMember(3)]
		public string Label { get; set; }

		/// <summary>A <see cref="RequirementFillModes"/> value.</summary>
		[ProtoMember(4)]
		public int FillMode { get; set; } = (int)RequirementFillModes.Count;

		[ProtoMember(5)]
		public int RequiredCount { get; set; } = 1;

		/// <summary>A <see cref="RunCardStatusFilters"/> value, applied to unit targets.</summary>
		[ProtoMember(6)]
		public int StatusFilter { get; set; }

		[MaxLength(200)]
		[ProtoMember(7)]
		public string StatusBaseTypes { get; set; }

		/// <summary>Optional response code (DispatchResponseCode.Id in the department's code catalog).</summary>
		[ProtoMember(8)]
		public int? ResponseCodeId { get; set; }

		/// <summary>Package filled in this row's place when the row is still short after the relaxed fill.</summary>
		[ProtoMember(9)]
		public int? SubstitutePackageId { get; set; }

		/// <summary>
		/// When the row is not met as configured (filled by a substitute, or short), the call keeps an outstanding
		/// requirement for it until a matching resource is dispatched or a person dismisses it.
		/// </summary>
		[ProtoMember(10)]
		public bool KeepOutstanding { get; set; }

		[ProtoMember(11)]
		public int SortOrder { get; set; }

		[NotMapped]
		[ProtoMember(12)]
		public List<RunCardTarget> Targets { get; set; } = new List<RunCardTarget>();

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ResponsePackageRowId; }
			set { ResponsePackageRowId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ResponsePackageRows";

		[NotMapped]
		public string IdName => "ResponsePackageRowId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Targets" };
	}
}
