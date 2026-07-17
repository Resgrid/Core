using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Processes an inbound chatbot message off the request thread: runs the chatbot pipeline
	/// (IChatbotIngressService) and, for SMS, sends the reply back to the sender via the SMS
	/// transport. Enqueued by the Twilio webhook so the webhook returns immediately.
	/// </summary>
	public class ChatbotMessageLogic
	{
		public static async Task<bool> ProcessChatbotMessageQueueItem(ChatbotMessageQueueItem item)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.From) || string.IsNullOrWhiteSpace(item.Body))
				return true;

			try
			{
				var chatbotIngressService = Bootstrapper.GetKernel().Resolve<IChatbotIngressService>();
				var textMessageProvider = Bootstrapper.GetKernel().Resolve<ITextMessageProvider>();

				var message = new ChatbotMessage
				{
					MessageId = item.MessageId,
					From = item.From,
					To = item.To,
					Text = item.Body,
					Platform = (ChatbotPlatform)item.Platform,
					Timestamp = DateTime.UtcNow
				};

				var response = await chatbotIngressService.ProcessMessageAsync(message);

				if (response != null && !string.IsNullOrWhiteSpace(response.Text))
				{
					// Reply from the department's text number (To) back to the sender (From). Twilio is the
					// primary transport; carrier only governs gateway fallback, so the default is fine here.
					// Chatbot replies are interactive (help/command lists the user acts on over SMS), so they
					// use the higher chatbot length cap instead of the notification default.
					await textMessageProvider.SendTextMessage(item.From, response.Text, item.To, default(MobileCarriers), item.DepartmentId,
						maxLengthOverride: Resgrid.Config.ChatbotConfig.SmsReplyMaxLength);
				}
			}
			catch (Exception ex)
			{
				// Same convention as AuditQueueLogic: a failed item must not take down the queue
				// processor — log it and move on (the sender simply gets no reply for this message).
				Logging.LogException(ex);
			}

			return true;
		}
	}
}
