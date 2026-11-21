using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
    public class SlackBotAdapter : HttpChatbotAdapter
    {
        public SlackBotAdapter() : this(new ChatbotHttpClient()) { }
        public SlackBotAdapter(ChatbotHttpClient http) : base(http) { }
        public override ChatbotPlatform Platform => ChatbotPlatform.Slack;
        public override bool IsConfigured => !string.IsNullOrWhiteSpace(ChatbotConfig.SlackBotToken)
            && !string.IsNullOrWhiteSpace(ChatbotConfig.SlackTeamId);
        protected override int MessageLength => 3000;
        protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
        {
            var user = Id(recipient, "slack");
            if (!System.Text.RegularExpressions.Regex.IsMatch(user, "^[UW][A-Z0-9]+$"))
                throw new InvalidOperationException("Invalid Slack user identifier.");
            var opened = await Http.PostAsync("https://slack.com/api/conversations.open", new { users = user }, "Bearer " + ChatbotConfig.SlackBotToken);
            if (opened.Value<bool?>("ok") != true || string.IsNullOrEmpty((string)opened["channel"]?["id"]))
                throw new InvalidOperationException("Slack could not open a direct conversation.");
            var sent = await Http.PostAsync("https://slack.com/api/chat.postMessage",
                new { channel = (string)opened["channel"]["id"], text, mrkdwn = false, parse = "none", unfurl_links = false, unfurl_media = false },
                "Bearer " + ChatbotConfig.SlackBotToken);
            if (sent.Value<bool?>("ok") != true) throw new InvalidOperationException("Slack rejected the message.");
        }

        /// <summary>
        /// Intake acknowledgements (call-intake plan D3): a threaded reply under the intake message in the linked channel. Only the
        /// configured workspace (ChatbotConfig.SlackTeamId) can be answered: the bot token belongs to it. Route keys: team_id,
        /// channel, thread_ts.
        /// </summary>
        public async Task PostToConversationAsync(System.Collections.Generic.IReadOnlyDictionary<string, string> route, string text)
        {
            if (!IsConfigured) throw new InvalidOperationException("Slack messaging is not configured.");
            string Value(string key) => route != null && route.TryGetValue(key, out var v) ? v : null;
            var channel = Value("channel");
            if (!string.Equals(Value("team_id"), ChatbotConfig.SlackTeamId, StringComparison.Ordinal)
                || !System.Text.RegularExpressions.Regex.IsMatch(channel ?? "", "^[CG][A-Z0-9]+$"))
                throw new InvalidOperationException("The Slack channel cannot be answered.");
            var body = Resgrid.Model.ProtectedOutboundGuard.Scrub(text, out _);
            if (string.IsNullOrWhiteSpace(body)) return;
            var sent = await Http.PostAsync("https://slack.com/api/chat.postMessage",
                new { channel, thread_ts = Value("thread_ts"), text = body.Length > MessageLength ? body.Substring(0, MessageLength) : body,
                    mrkdwn = false, parse = "none", unfurl_links = false, unfurl_media = false },
                "Bearer " + ChatbotConfig.SlackBotToken);
            if (sent.Value<bool?>("ok") != true) throw new InvalidOperationException("Slack rejected the message.");
        }
    }
}
