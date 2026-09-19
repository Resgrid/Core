using System.Linq;

namespace Resgrid.Model.Certifications
{
	/// <summary>The Phase D workflow triggers (23 activated, 87–93 added; plan D7).</summary>
	public static class CertificationWorkflowTriggers
	{
		public static readonly int[] Triggers =
		{
			(int)WorkflowTriggerEventType.CertificationExpiring,
			(int)WorkflowTriggerEventType.CertificationAdded,
			(int)WorkflowTriggerEventType.CertificationRenewed,
			(int)WorkflowTriggerEventType.CertificationExpired,
			(int)WorkflowTriggerEventType.CertificationRoleRemoved,
			(int)WorkflowTriggerEventType.CertificationStatusChanged,
			(int)WorkflowTriggerEventType.UnitCertificationExpiring,
			(int)WorkflowTriggerEventType.UnitCertificationExpired
		};

		public static bool IsCertification(int trigger) => Triggers.Contains(trigger);
	}
}
