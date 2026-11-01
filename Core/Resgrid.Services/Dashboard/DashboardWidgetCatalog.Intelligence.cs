using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>Intelligence widgets (home-dashboard-redesign-plan.md §7.11, and aiDispatch.triage from §7.1).</summary>
		private static partial IEnumerable<DashboardWidgetDefinition> IntelligenceWidgets()
		{
			// The assistant conversation rides the chat hub and store (rg-assistant hides itself without Chat.System), so the widget
			// needs Chat.System as well as the Enhanced AI capability.
			yield return new DashboardWidgetDefinition
			{
				Key = "ai.assistant",
				Category = DashboardWidgetCategories.Intelligence,
				Icon = "fa-magic",
				// The assistant has no page of its own (it is a drawer); "Open module" goes to the chat page that hosts conversations.
				ModuleUrl = "/User/Chat",
				AllowedSizes = Sizes(DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.L,
				DefaultRefreshSeconds = 3600,
				Gates = new DashboardWidgetGates
				{
					Flags = new[] { FeatureFlagKeys.AiAssistant, FeatureFlagKeys.ChatSystem },
					Access = new[] { DashboardAccessChecks.EnhancedAiAssistant },
					Addon = PlanAddonTypes.EnhancedAi
				}
			};

			// Listed with the other AI widget (category Intelligence) although the plan files it under Operations.
			yield return new DashboardWidgetDefinition
			{
				Key = "aiDispatch.triage",
				Category = DashboardWidgetCategories.Intelligence,
				Icon = "fa-inbox",
				ModuleUrl = "/User/Dispatch/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 60,
				Settings = new[] { Number("hours", 24, 1, 72), Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Call_Create" },
					Flags = new[] { FeatureFlagKeys.AiDispatchTemplate, FeatureFlagKeys.AiEnhanced },
					Access = new[] { DashboardAccessChecks.AiDispatchAvailable },
					Addon = PlanAddonTypes.EnhancedAi
				}
			};
		}
	}
}
