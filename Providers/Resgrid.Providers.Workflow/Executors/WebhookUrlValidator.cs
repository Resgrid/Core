using System;
using System.Text.RegularExpressions;

namespace Resgrid.Providers.Workflow.Executors
{
	/// <summary>
	/// Validates that webhook URLs belong to the expected vendor domains,
	/// preventing these executors from being used as generic HTTP proxy tools.
	/// </summary>
	internal static class WebhookUrlValidator
	{
		// Teams: https://<tenant>.webhook.office.com/... or Azure Logic Apps
		private static readonly Regex TeamsPattern = new Regex(
			@"^https://[a-zA-Z0-9\-]+\.webhook\.office\.com/",
			RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private static readonly Regex TeamsLogicAppPattern = new Regex(
			@"^https://[a-zA-Z0-9\-]+\.logic\.azure\.com/",
			RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		// Slack: https://hooks.slack.com/services/...
		private static readonly Regex SlackPattern = new Regex(
			@"^https://hooks\.slack\.com/",
			RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		// Discord: https://discord.com/api/webhooks/... or discordapp.com
		private static readonly Regex DiscordPattern = new Regex(
			@"^https://discord(app)?\.com/api/webhooks/",
			RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		/// <summary>
		/// Validates a webhook URL for the specified vendor.
		/// </summary>
		/// <param name="url">The webhook URL to validate.</param>
		/// <param name="vendor">"teams", "slack", or "discord"</param>
		/// <returns>(IsValid: true, Reason: null) on success; (false, reason) when rejected.</returns>
		public static (bool IsValid, string Reason) Validate(string url, string vendor)
		{
			if (string.IsNullOrWhiteSpace(url))
				return (false, "Webhook URL is empty.");

			return vendor?.ToLowerInvariant() switch
			{
				"teams" => ValidateTeams(url),
				"slack" => ValidateSlack(url),
				"discord" => ValidateDiscord(url),
				_ => (false, $"Unknown vendor '{vendor}'.")
			};
		}

		private static (bool, string) ValidateTeams(string url)
		{
			if (TeamsPattern.IsMatch(url) || TeamsLogicAppPattern.IsMatch(url))
				return (true, null);
			return (false, $"Teams webhook URL must match '*.webhook.office.com' or '*.logic.azure.com'. Received: {SanitizeForLog(url)}");
		}

		private static (bool, string) ValidateSlack(string url)
		{
			if (SlackPattern.IsMatch(url))
				return (true, null);
			return (false, $"Slack webhook URL must match 'https://hooks.slack.com/...'. Received: {SanitizeForLog(url)}");
		}

		private static (bool, string) ValidateDiscord(string url)
		{
			if (DiscordPattern.IsMatch(url))
				return (true, null);
			return (false, $"Discord webhook URL must start with 'https://discord.com/api/webhooks/...'. Received: {SanitizeForLog(url)}");
		}

		/// <summary>Truncates URL to avoid leaking long tokens in error messages.</summary>
		private static string SanitizeForLog(string url) =>
			url.Length > 60 ? url.Substring(0, 60) + "..." : url;
	}
}

