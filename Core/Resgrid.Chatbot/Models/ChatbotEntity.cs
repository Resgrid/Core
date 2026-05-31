using System;
using System.Collections.Generic;

namespace Resgrid.Chatbot.Models
{
	public class ChatbotEntity
	{
		public string EntityType { get; set; }
		public string Value { get; set; }
		public string NormalizedValue { get; set; }
		public double Confidence { get; set; }
		public int? ResolvedId { get; set; }
	}
}
