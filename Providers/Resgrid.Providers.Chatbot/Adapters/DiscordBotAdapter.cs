using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
    public class DiscordBotAdapter : HttpChatbotAdapter
    {
        public DiscordBotAdapter() : this(new ChatbotHttpClient()) { }
        public DiscordBotAdapter(ChatbotHttpClient http) : base(http) { }
        public override ChatbotPlatform Platform => ChatbotPlatform.Discord;
        public override bool IsConfigured => !string.IsNullOrWhiteSpace(ChatbotConfig.DiscordBotToken);
        protected override int MessageLength => 2000;
        protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
        {
            var user = Id(recipient, "discord");
            if (!ulong.TryParse(user, out _)) throw new InvalidOperationException("Invalid Discord user identifier.");
            var dm = await Http.PostAsync("https://discord.com/api/v10/users/@me/channels", new { recipient_id = user }, "Bot " + ChatbotConfig.DiscordBotToken);
            var channel = (string)dm["id"];
            if (!ulong.TryParse(channel, out _)) throw new InvalidOperationException("Discord could not open a direct conversation.");
            var sent = await Http.PostAsync("https://discord.com/api/v10/channels/" + channel + "/messages",
                new { content = text, allowed_mentions = new { parse = Array.Empty<string>() } }, "Bot " + ChatbotConfig.DiscordBotToken);
            if (string.IsNullOrEmpty((string)sent["id"])) throw new InvalidOperationException("Discord rejected the message.");
        }
    }
}
