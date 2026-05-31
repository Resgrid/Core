using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Interfaces;

namespace Resgrid.Chatbot.NLU.Services
{
	public class EntityExtractor : IEntityExtractor
	{
		private static readonly Regex _callIdPattern = new(@"\bc(\d+)\b", RegexOptions.IgnoreCase);
		private static readonly Regex _callIdPattern2 = new(@"call\s*#?\s*(\d+)", RegexOptions.IgnoreCase);
		private static readonly Regex _messageIdPattern = new(@"message\s*#?\s*(\d+)", RegexOptions.IgnoreCase);
		private static readonly Regex _unitPattern = new(@"(engine|ladder|medic|ambulance|rescue|truck|squad|tanker|brush|boat|chief|battalion)\s*(\d+)?", RegexOptions.IgnoreCase);

		public Task<List<ChatbotEntity>> ExtractAsync(string text, string intentName, int departmentId)
		{
			var entities = new List<ChatbotEntity>();

			if (string.IsNullOrWhiteSpace(text))
				return Task.FromResult(entities);

			// Extract call IDs
			ExtractCallIds(text, entities);

			// Extract message IDs (for message-related intents)
			if (intentName?.Contains("message") == true)
			{
				var msgMatch = _messageIdPattern.Match(text);
				if (msgMatch.Success)
				{
					entities.Add(new ChatbotEntity
					{
						EntityType = "MessageId",
						Value = msgMatch.Groups[1].Value,
						NormalizedValue = msgMatch.Groups[1].Value,
						Confidence = 0.95
					});
				}
			}

			// Extract unit names (for unit-related intents)
			if (intentName?.Contains("unit") == true || intentName?.Contains("units") == true)
			{
				var unitMatches = _unitPattern.Matches(text);
				foreach (Match m in unitMatches)
				{
					entities.Add(new ChatbotEntity
					{
						EntityType = "UnitName",
						Value = m.Value,
						NormalizedValue = m.Value.ToLowerInvariant(),
						Confidence = 0.8
					});
				}
			}

			// Extract dates (today, tomorrow, next X, specific dates)
			ExtractDates(text, entities);

			// Extract times
			ExtractTimes(text, entities);

			return Task.FromResult(entities);
		}

		private static void ExtractCallIds(string text, List<ChatbotEntity> entities)
		{
			foreach (Match m in _callIdPattern.Matches(text))
			{
				entities.Add(new ChatbotEntity
				{
					EntityType = "CallId",
					Value = m.Groups[1].Value,
					NormalizedValue = m.Groups[1].Value,
					Confidence = 0.99
				});
			}

			foreach (Match m in _callIdPattern2.Matches(text))
			{
				entities.Add(new ChatbotEntity
				{
					EntityType = "CallId",
					Value = m.Groups[1].Value,
					NormalizedValue = m.Groups[1].Value,
					Confidence = 0.95
				});
			}
		}

		private static void ExtractDates(string text, List<ChatbotEntity> entities)
		{
			var lower = text.ToLowerInvariant();

			if (lower.Contains("today"))
				entities.Add(new ChatbotEntity { EntityType = "DateExpression", Value = DateTime.UtcNow.ToString("yyyy-MM-dd"), NormalizedValue = "today", Confidence = 0.9 });

			if (lower.Contains("tomorrow"))
				entities.Add(new ChatbotEntity { EntityType = "DateExpression", Value = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"), NormalizedValue = "tomorrow", Confidence = 0.9 });

			if (lower.Contains("this week"))
				entities.Add(new ChatbotEntity { EntityType = "DateRange", Value = "week", NormalizedValue = "thisweek", Confidence = 0.8 });

			if (lower.Contains("next week"))
				entities.Add(new ChatbotEntity { EntityType = "DateRange", Value = "nextweek", NormalizedValue = "nextweek", Confidence = 0.8 });
		}

		private static void ExtractTimes(string text, List<ChatbotEntity> entities)
		{
			var timePattern = new Regex(@"\b(\d{1,2})(:\d{2})?\s*(am|pm|AM|PM)?\b");
			var match = timePattern.Match(text);
			if (match.Success)
			{
				entities.Add(new ChatbotEntity
				{
					EntityType = "TimeExpression",
					Value = match.Value,
					NormalizedValue = match.Value.ToLowerInvariant(),
					Confidence = 0.85
				});
			}
		}
	}
}
