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
    }
}
