using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.NLU.Providers
{
	public class MLNetNluProvider : INLUProvider
	{
		public string ProviderName => "MLNet";
		public int Priority => 10;

		public MLNetNluProvider()
		{
		}

		public Task<NLUResult> ClassifyAsync(string text, string context = null)
		{
			if (string.IsNullOrWhiteSpace(text))
				return Task.FromResult(new NLUResult { IntentName = "unknown", Confidence = 0, ProviderName = ProviderName });

			// Phase 2: ML.NET placeholder.
			// When a trained model is available at ChatbotConfig.MlNetModelPath,
			// this will load the model and use PredictionEngine to classify.
			// For now, falls back to returning unknown so the router uses keyword classifier.

			return Task.FromResult(new NLUResult
			{
				IntentName = "unknown",
				Confidence = 0,
				ProviderName = ProviderName,
				RawResponse = "ML.NET model not yet trained. Use Keyword provider."
			});
		}

		public Task<bool> IsAvailableAsync()
		{
			// Check if model file exists
			var modelPath = Resgrid.Chatbot.Config.ChatbotConfig.MlNetModelPath;
			if (string.IsNullOrWhiteSpace(modelPath))
				return Task.FromResult(false);

			return Task.FromResult(System.IO.File.Exists(modelPath));
		}
	}
}
