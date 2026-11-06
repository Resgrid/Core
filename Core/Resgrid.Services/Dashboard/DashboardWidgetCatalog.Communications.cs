using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>Communications widgets (home-dashboard-redesign-plan.md §7.5).</summary>
		private static partial IEnumerable<DashboardWidgetDefinition> CommunicationsWidgets()
		{
			yield return new DashboardWidgetDefinition
			{
				Key = "messages.inbox",
				Category = DashboardWidgetCategories.Communications,
				Icon = "fa-envelope",
				ModuleUrl = "/User/Messages/Inbox",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 120,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Messages_View" },
					Access = new[] { DashboardAccessChecks.MessagingEnabled }
				},
				Settings = new[] { Number("maxItems", 5, 1, 20) },
				ProtectedFields = new[]
				{
					new DashboardProtectedField { FieldId = "messages.subject", ViewPermission = PermissionTypes.ViewProtectedOperationalData }
				}
			};

			// v4 ChatController is Chat_View and every chat surface sits behind Chat.System.
			yield return new DashboardWidgetDefinition
			{
				Key = "chat.unread",
				Category = DashboardWidgetCategories.Communications,
				Icon = "fa-comments",
				ModuleUrl = "/User/Chat",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.Chat),
				DefaultRefreshSeconds = 300,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Department_View" }, // the Chat page is [Authorize] + Chat.System; no Chat_View policy in the web app
					Flags = new[] { FeatureFlagKeys.ChatSystem },
					Access = new[] { DashboardAccessChecks.ChatEnabled }
				},
				Settings = new[] { Number("maxItems", 5, 1, 20) }
			};

			// The Moderation page admits group admins, but the flag queue (IChatModerationService.GetFlagsAsync) answers
			// department admins only, so this widget is theirs.
			yield return new DashboardWidgetDefinition
			{
				Key = "chat.moderation",
				Category = DashboardWidgetCategories.Communications,
				Icon = "fa-flag",
				ModuleUrl = "/User/Moderation",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 300,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Department_View" }, // the Chat page is [Authorize] + Chat.System; no Chat_View policy in the web app
					Flags = new[] { FeatureFlagKeys.ChatSystem },
					Access = new[] { DashboardAccessChecks.ChatEnabled },
					Role = DashboardRoles.DepartmentAdmin
				},
				Settings = new[] { Number("maxItems", 5, 1, 20) }
			};

			// The Communication Tests page admits department admins and group admins.
			yield return new DashboardWidgetDefinition
			{
				Key = "comms.tests",
				Category = DashboardWidgetCategories.Communications,
				Icon = "fa-signal",
				ModuleUrl = "/User/CommunicationTest",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Gates = new DashboardWidgetGates { Role = DashboardRoles.GroupAdmin },
				Settings = new[] { Number("maxRuns", 5, 1, 20) }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "voice.ptt",
				Category = DashboardWidgetCategories.Communications,
				Icon = "fa-microphone",
				ModuleUrl = "/User/Voice",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 300,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Voice_View" },
					Access = new[] { DashboardAccessChecks.VoiceAddon },
					Addon = PlanAddonTypes.PTT
				}
			};
		}
	}
}
