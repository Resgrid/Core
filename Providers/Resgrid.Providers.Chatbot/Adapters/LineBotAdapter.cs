using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
    public class LineBotAdapter : HttpChatbotAdapter
    {
        public LineBotAdapter(ChatbotHttpClient http) : base(http) { }
        public override ChatbotPlatform Platform => ChatbotPlatform.Line;
        public override bool IsConfigured => !string.IsNullOrWhiteSpace(ChatbotConfig.LineChannelAccessToken);
        protected override int MessageLength => 5000;
        protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
            => await Http.PostAsync("https://api.line.me/v2/bot/message/push",
                new { to = recipient, messages = new[] { new { type = "text", text } } }, "Bearer " + ChatbotConfig.LineChannelAccessToken);
    }
}
