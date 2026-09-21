using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
    public class ViberBotAdapter : HttpChatbotAdapter
    {
        public ViberBotAdapter(ChatbotHttpClient http) : base(http) { }
        public override ChatbotPlatform Platform => ChatbotPlatform.Viber;
        public override bool IsConfigured => !string.IsNullOrWhiteSpace(ChatbotConfig.ViberBotToken);
        protected override int MessageLength => 7000;
        protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
        {
            var sent = await Http.PostAsync("https://chatapi.viber.com/pa/send_message",
                new { receiver = recipient, type = "text", text, sender = new { name = "Resgrid" } },
                tokenHeader: "X-Viber-Auth-Token", token: ChatbotConfig.ViberBotToken);
            if (sent.Value<int?>("status") != 0) throw new InvalidOperationException("Viber rejected the message.");
        }
    }
}
