using Newtonsoft.Json;
using ProtoBuf;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// One node of an alarm level's decision graph (conditional run cards): a yes/no condition, or an outcome naming a
	/// response package and/or advice. Children are pointers by stable <see cref="NodeKey"/> on the parent, so one node can
	/// be the child of two parents (a shared node; "A or B" is two conditions whose Yes branches share an outcome). There
	/// are deliberately no navigation properties to other nodes: the repository's child cascade must never see a
	/// self-referencing collection.
	/// </summary>
	[Table("RunCardDecisionNodes")]
	[ProtoContract]
	public class RunCardDecisionNode : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int RunCardDecisionNodeId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int RunCardAlarmLevelId { get; set; }

		/// <summary>Stable key (unique within the level) the Yes/No pointers refer to.</summary>
		[Required]
		[MaxLength(36)]
		[ProtoMember(3)]
		public string NodeKey { get; set; }

		[ProtoMember(4)]
		public bool IsRoot { get; set; }

		/// <summary>A <see cref="RunCardDecisionNodeKinds"/> value.</summary>
		[ProtoMember(5)]
		public int NodeKind { get; set; }

		[MaxLength(200)]
		[ProtoMember(6)]
		public string Label { get; set; }

		/// <summary>A <see cref="RunCardConditionTypes"/> value (conditions only).</summary>
		[ProtoMember(7)]
		public int ConditionType { get; set; }

		/// <summary>A <see cref="RunCardStatusFilters"/> value for the subject operand.</summary>
		[ProtoMember(8)]
		public int SubjectStatusFilter { get; set; }

		/// <summary>Comma-separated ActionBaseTypes for the subject when its filter is BaseTypes.</summary>
		[MaxLength(200)]
		[ProtoMember(9)]
		public string SubjectStatusBaseTypes { get; set; }

		[ProtoMember(10)]
		public int ReferenceStatusFilter { get; set; }

		[MaxLength(200)]
		[ProtoMember(11)]
		public string ReferenceStatusBaseTypes { get; set; }

		/// <summary>Margin (ArrivesBefore) or limit (AvailableWithin), in seconds.</summary>
		[ProtoMember(12)]
		public int? ThresholdSeconds { get; set; }

		/// <summary>AvailableWithin: how many resources must make it (default 1).</summary>
		[ProtoMember(13)]
		public int RequiredCount { get; set; } = 1;

		/// <summary>Kind-specific parameters that fit no typed column (extension condition kinds).</summary>
		[ProtoMember(14)]
		public string ParametersJson { get; set; }

		[MaxLength(36)]
		[ProtoMember(15)]
		public string YesNodeKey { get; set; }

		[MaxLength(36)]
		[ProtoMember(16)]
		public string NoNodeKey { get; set; }

		/// <summary>Outcome: the response package to fill (null with advice = advice only).</summary>
		[ProtoMember(17)]
		public int? ResponsePackageId { get; set; }

		[MaxLength(1000)]
		[ProtoMember(18)]
		public string AdviceText { get; set; }

		[ProtoMember(19)]
		public int SortOrder { get; set; }

		[NotMapped]
		[ProtoMember(20)]
		public List<RunCardTarget> SubjectTargets { get; set; } = new List<RunCardTarget>();

		[NotMapped]
		[ProtoMember(21)]
		public List<RunCardTarget> ReferenceTargets { get; set; } = new List<RunCardTarget>();

		[NotMapped]
		public bool IsCondition => NodeKind == (int)RunCardDecisionNodeKinds.Condition;

		[NotMapped]
		public bool IsOutcome => NodeKind == (int)RunCardDecisionNodeKinds.Outcome;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardDecisionNodeId; }
			set { RunCardDecisionNodeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardDecisionNodes";

		[NotMapped]
		public string IdName => "RunCardDecisionNodeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "SubjectTargets", "ReferenceTargets", "IsCondition", "IsOutcome" };
	}
}
