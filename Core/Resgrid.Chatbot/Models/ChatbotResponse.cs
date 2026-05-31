using System.Collections.Generic;

namespace Resgrid.Chatbot.Models
{
	public class ChatbotResponse
	{
		public string Text { get; set; }
		public List<string> Segments { get; set; } = new List<string>();
		public bool Processed { get; set; }
		public ChatbotIntent Intent { get; set; }
		public string ResponseFormat { get; set; } = "text";
		public List<ChatbotRichComponent> RichComponents { get; set; } = new List<ChatbotRichComponent>();
	}

	public class ChatbotRichComponent
	{
		public string Type { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string Url { get; set; }
		public string ColorHex { get; set; }
		public List<ChatbotRichField> Fields { get; set; } = new List<ChatbotRichField>();
		public List<ChatbotRichAction> Actions { get; set; } = new List<ChatbotRichAction>();
		public string ImageUrl { get; set; }
		public string FooterText { get; set; }
	}

	public class ChatbotRichField
	{
		public string Name { get; set; }
		public string Value { get; set; }
		public bool Inline { get; set; }
	}

	public class ChatbotRichAction
	{
		public string ActionId { get; set; }
		public string Label { get; set; }
		public string Style { get; set; }
		public string Value { get; set; }
		public string Url { get; set; }
	}
}
