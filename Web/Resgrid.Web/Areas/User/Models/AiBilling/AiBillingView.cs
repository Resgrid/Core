using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.AiBilling
{
	/// <summary>Enhanced AI add-on purchase page (enhanced-ai-addon-plan.md). Status is null when billing is unavailable.</summary>
	public sealed class AiBillingView
	{
		public AiBillingStatus Status { get; set; }

		/// <summary>Ai.Enhanced is on and the department's AI module switch is on; a purchase before rollout shows "being enabled".</summary>
		public bool RolledOut { get; set; }

		public bool FreeAllowanceEnabled { get; set; }
		public int FreeStarterQuestions { get; set; }
		public int FreeStarterDays { get; set; }
		public int FreeMonthlyQuestions { get; set; }
	}
}
