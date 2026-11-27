using System;
using System.ComponentModel;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// ProtoBuf <see cref="DepartmentSettingTypes.LearningRequirementSettings"/> (143), Trainings → Programs → Settings (T3, LMS): the
	/// department switch for training-backed enforcement (off by default, decision 12; each requirement still opts in), the
	/// blast-radius share (R-L11: one run never removes more than this share of a role's holders) and the default at-risk window for new
	/// requirements. Missing = enforcement off, 20%, 14 days.
	/// </summary>
	[ProtoContract]
	public class LearningRequirementSettings
	{
		public const int DefaultMaxRoleRemovalSharePercent = 20;
		public const int StandardAtRiskDays = 14;

		/// <summary>Training-backed enforcement may expire certifications (and so, under Enforce, roles) for this department.</summary>
		[ProtoMember(1)] public bool EnforcementEnabled { get; set; }

		/// <summary>A sweep refuses to act, and alerts the admins, when it would remove more than this share of a role's holders.</summary>
		[ProtoMember(2)]
		[DefaultValue(DefaultMaxRoleRemovalSharePercent)]
		public int MaxRoleRemovalSharePercent { get; set; } = DefaultMaxRoleRemovalSharePercent;

		/// <summary>Days before the due date a new requirement shows members at risk.</summary>
		[ProtoMember(3)]
		[DefaultValue(StandardAtRiskDays)]
		public int DefaultAtRiskDays { get; set; } = StandardAtRiskDays;

		public LearningRequirementSettings Normalized()
		{
			return new LearningRequirementSettings
			{
				EnforcementEnabled = EnforcementEnabled,
				MaxRoleRemovalSharePercent = Math.Clamp(MaxRoleRemovalSharePercent <= 0 ? DefaultMaxRoleRemovalSharePercent : MaxRoleRemovalSharePercent, 1, 100),
				DefaultAtRiskDays = Math.Clamp(DefaultAtRiskDays, 0, 365)
			};
		}
	}
}
