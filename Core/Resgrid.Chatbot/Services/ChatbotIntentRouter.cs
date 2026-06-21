using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;

namespace Resgrid.Chatbot.Services
{
	public class ChatbotIntentRouter : IChatbotIntentRouter
	{
		private readonly IEnumerable<INLUProvider> _nluProviders;
		private readonly IIntentMapper _intentMapper;
		private List<INLUProvider> _sortedProviders;

		public ChatbotIntentRouter(IEnumerable<INLUProvider> nluProviders, IIntentMapper intentMapper)
		{
			_nluProviders = nluProviders;
			_intentMapper = intentMapper;
		}

		/// <summary>
		/// Classifies user input using the configured NLU pipeline.
		///
		/// Pipeline modes:
		///   Keyword:    Only keyword classifier (fast, offline, always available)
		///   MLNet:      Only ML.NET local model
		///   HybridLocal: Keyword first, then ML.NET if confidence below threshold
		///   Cloud:      Only cloud LLM (OpenAI/DeepSeek/Azure/Anthropic)
		///   HybridCloud: Keyword first, then cloud LLM fallback for low-confidence results
		///
		/// All modes fall back to keyword if the primary provider fails or is unavailable.
		/// </summary>
		public async Task<ChatbotIntent> ClassifyIntentAsync(ChatbotMessage message, ChatbotSession session)
		{
			if (message == null || string.IsNullOrWhiteSpace(message.Text))
				return new ChatbotIntent { Type = ChatbotIntentType.Unknown, Confidence = 0 };

			try
			{
				var text = message.Text.Trim();
				var context = BuildContextString(session);
				var departmentId = session?.DepartmentId ?? 0;
				var mode = ChatbotConfig.NluProvider;

				switch (mode)
				{
					case NluProviderType.Cloud:
						return await ClassifyCloudOnlyAsync(text, context, departmentId);

					case NluProviderType.HybridCloud:
						return await ClassifyHybridCloudAsync(text, context, departmentId);

					case NluProviderType.HybridLocal:
						return await ClassifyHybridLocalAsync(text, context, departmentId);

					case NluProviderType.MLNet:
						return await ClassifySingleProviderAsync("MLNet", text, context, departmentId);

					case NluProviderType.Keyword:
					default:
						return await ClassifyKeywordOnlyAsync(text, context, departmentId);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ChatbotIntent { Type = ChatbotIntentType.Unknown, Confidence = 0 };
			}
		}

		private async Task<ChatbotIntent> ClassifyKeywordOnlyAsync(string text, string context, int departmentId)
		{
			var provider = GetProviders().FirstOrDefault(p => p.ProviderName == "Keyword");
			return await ClassifyWithProviderAsync(provider, text, context, departmentId);
		}

		private async Task<ChatbotIntent> ClassifySingleProviderAsync(string providerName, string text, string context, int departmentId)
		{
			var provider = GetProviders().FirstOrDefault(p => p.ProviderName == providerName);
			if (provider == null)
				return await ClassifyKeywordOnlyAsync(text, context, departmentId);

			return await ClassifyWithProviderAsync(provider, text, context, departmentId);
		}

		private async Task<ChatbotIntent> ClassifyCloudOnlyAsync(string text, string context, int departmentId)
		{
			var cloudProvider = GetProviders().FirstOrDefault(p => p.ProviderName == "CloudLLM");
			if (cloudProvider == null || !await cloudProvider.IsAvailableAsync())
			{
				// Cloud not available, fall back to keyword
				Logging.LogInfo("ChatbotIntentRouter: Cloud provider unavailable, falling back to keyword");
				return await ClassifyKeywordOnlyAsync(text, context, departmentId);
			}

			var intent = await ClassifyWithProviderAsync(cloudProvider, text, context, departmentId);

			// If cloud returned low confidence, optionally fall back to keyword
			if (intent.Confidence < ChatbotConfig.MinimumIntentConfidence && ChatbotConfig.CloudNluFallbackToKeyword)
			{
				Logging.LogInfo($"ChatbotIntentRouter: Cloud confidence {intent.Confidence} below threshold, falling back to keyword");
				var keywordIntent = await ClassifyKeywordOnlyAsync(text, context, departmentId);
				if (keywordIntent.Confidence > intent.Confidence)
					return keywordIntent;
			}

			return intent;
		}

		private async Task<ChatbotIntent> ClassifyHybridCloudAsync(string text, string context, int departmentId)
		{
			// Step 1: Try keyword classifier first (fast)
			var keywordIntent = await ClassifyKeywordOnlyAsync(text, context, departmentId);

			// If keyword is confident enough, return immediately
			if (keywordIntent.Confidence >= ChatbotConfig.MinimumIntentConfidence)
				return keywordIntent;

			// Step 2: Keyword wasn't confident — try cloud
			var cloudProvider = GetProviders().FirstOrDefault(p => p.ProviderName == "CloudLLM");
			if (cloudProvider == null || !await cloudProvider.IsAvailableAsync())
				return keywordIntent; // Return best we have (even if low confidence)

			var cloudIntent = await ClassifyWithProviderAsync(cloudProvider, text, context, departmentId);
			cloudIntent.IsFallbackResult = true;

			// If cloud is confident, use it
			if (cloudIntent.Confidence >= ChatbotConfig.MinimumCloudConfidence)
				return cloudIntent;

			// Neither is confident — return the higher-confidence one
			return cloudIntent.Confidence > keywordIntent.Confidence ? cloudIntent : keywordIntent;
		}

		private async Task<ChatbotIntent> ClassifyHybridLocalAsync(string text, string context, int departmentId)
		{
			var keywordIntent = await ClassifyKeywordOnlyAsync(text, context, departmentId);
			if (keywordIntent.Confidence >= ChatbotConfig.MinimumIntentConfidence)
				return keywordIntent;

			var mlProvider = GetProviders().FirstOrDefault(p => p.ProviderName == "MLNet");
			if (mlProvider == null || !await mlProvider.IsAvailableAsync())
				return keywordIntent;

			var mlIntent = await ClassifyWithProviderAsync(mlProvider, text, context, departmentId);
			return mlIntent.Confidence > keywordIntent.Confidence ? mlIntent : keywordIntent;
		}

		private async Task<ChatbotIntent> ClassifyWithProviderAsync(INLUProvider provider, string text, string context, int departmentId)
		{
			if (provider == null)
				return new ChatbotIntent { Type = ChatbotIntentType.Unknown, Confidence = 0 };

			var nluResult = await provider.ClassifyAsync(text, context, departmentId);
			var intent = _intentMapper.MapToIntent(nluResult);

			// Apply confidence threshold (applied at mapping level)
			if (intent.Confidence < ChatbotConfig.MinimumIntentConfidence && provider.ProviderName != "CloudLLM")
			{
				intent.Type = ChatbotIntentType.Unknown;
				intent.Confidence = 0;
			}

			return intent;
		}

		private List<INLUProvider> GetProviders()
		{
			if (_sortedProviders == null)
			{
				_sortedProviders = _nluProviders
					.OrderBy(p => p.Priority)
					.ToList();
			}
			return _sortedProviders;
		}

		private string BuildContextString(ChatbotSession session)
		{
			if (session?.PendingIntent == null || session.Context.Count == 0)
				return null;

			var parts = new List<string>();
			parts.Add($"pending_intent:{session.PendingIntent}");

			foreach (var kvp in session.Context)
				parts.Add($"{kvp.Key}:{kvp.Value}");

			return string.Join("|", parts);
		}
	}
}
