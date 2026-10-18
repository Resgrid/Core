using System;
using ProtoBuf;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// Department setting 83 (DepartmentSettingTypes.DepartmentImportConfig): the department's Import &amp; Migration
	/// policy (wizard plan sections 5.1, 14.6 and 17.2). Every member defaults to zero/false, which means "use the plan
	/// default", so a missing setting and an empty one behave the same and no [DefaultValue] is needed.
	/// </summary>
	[ProtoContract]
	public class DepartmentImportConfig
	{
		public const int DefaultArtifactRetentionDaysAfterSuccess = 7;
		public const int DefaultArtifactRetentionDaysAfterFailure = 30;
		public const int DefaultStagingRetentionDaysAfterActivation = 30;
		public const int DefaultSupportGrantDays = 7;
		public const int MaxSupportGrantDays = 30;
		public const int MaxOperateGrantDays = 90;
		/// <summary>Under a contract authorization, every administrator is told this long before a managed activation or invitation send (section 17.8).</summary>
		public const int ManagedActivationNoticeHours = 24;
		public const int MaxRetentionDays = 180;

		/// <summary>Days uploaded files are kept after their datasets' last successful commit. 0 = 7 (72 hours for ADP departments).</summary>
		[ProtoMember(1)]
		public int ArtifactRetentionDaysAfterSuccess { get; set; }

		/// <summary>Days uploaded files are kept after a failure or cancellation. 0 = 30.</summary>
		[ProtoMember(2)]
		public int ArtifactRetentionDaysAfterFailure { get; set; }

		/// <summary>Days staging rows, issues and value maps are kept after activation. 0 = 30.</summary>
		[ProtoMember(3)]
		public int StagingRetentionDaysAfterActivation { get; set; }

		/// <summary>A run's approval needs a second, different administrator holding permission 72.</summary>
		[ProtoMember(4)]
		public bool RequireSecondApprover { get; set; }

		/// <summary>Default <see cref="ImportWriteMode"/> for new workspaces. 0 = create-only.</summary>
		[ProtoMember(5)]
		public int DefaultWriteMode { get; set; }

		/// <summary>Default length, in days, of a support access grant. 0 = 7; at most 30.</summary>
		[ProtoMember(6)]
		public int SupportGrantDefaultDays { get; set; }

		public TimeSpan ArtifactRetentionAfterSuccess(bool adpEnabled) => adpEnabled
			? TimeSpan.FromHours(72)
			: TimeSpan.FromDays(Clamp(ArtifactRetentionDaysAfterSuccess, DefaultArtifactRetentionDaysAfterSuccess));

		public TimeSpan ArtifactRetentionAfterFailure() =>
			TimeSpan.FromDays(Clamp(ArtifactRetentionDaysAfterFailure, DefaultArtifactRetentionDaysAfterFailure));

		public TimeSpan StagingRetentionAfterActivation() =>
			TimeSpan.FromDays(Clamp(StagingRetentionDaysAfterActivation, DefaultStagingRetentionDaysAfterActivation));

		public ImportWriteMode EffectiveWriteMode() =>
			Enum.IsDefined(typeof(ImportWriteMode), DefaultWriteMode) && DefaultWriteMode != (int)ImportWriteMode.Replace
				? (ImportWriteMode)DefaultWriteMode
				: ImportWriteMode.CreateOnly;

		public int EffectiveSupportGrantDays() =>
			SupportGrantDefaultDays <= 0 ? DefaultSupportGrantDays : Math.Min(SupportGrantDefaultDays, MaxSupportGrantDays);

		private static int Clamp(int value, int fallback) => value <= 0 ? fallback : Math.Min(value, MaxRetentionDays);
	}
}
