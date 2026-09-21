using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
    public class TelegramBotAdapter : HttpChatbotAdapter
    {
        public TelegramBotAdapter() : this(new ChatbotHttpClient()) { }
        public TelegramBotAdapter(ChatbotHttpClient http) : base(http) { }
        public override ChatbotPlatform Platform => ChatbotPlatform.Telegram;
        public override bool IsConfigured => !string.IsNullOrWhiteSpace(ChatbotConfig.TelegramBotToken);
        protected override int MessageLength => 4096;
        protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
        {
            var user = Id(recipient, "telegram");
            if (!long.TryParse(user, out var id) || id <= 0) throw new InvalidOperationException("Invalid private Telegram recipient.");
            var sent = await Http.PostAsync("https://api.telegram.org/bot" + ChatbotConfig.TelegramBotToken + "/sendMessage",
                new { chat_id = user, text, link_preview_options = new { is_disabled = true } });
            if (sent.Value<bool?>("ok") != true) throw new InvalidOperationException("Telegram rejected the message.");
        }
    }
}
