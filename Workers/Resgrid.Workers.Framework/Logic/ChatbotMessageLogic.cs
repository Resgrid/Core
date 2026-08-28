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
		/// <summary>
		/// True when the message is the SMS opt-out command. Uses the same TextCommandTypes.Stop
		/// classification as the webhook so every STOP variant it honors is honored here; a resolver
		/// fault falls back to a literal "STOP" compare rather than blocking an opt-out.
		/// </summary>
		private static bool IsStopCommand(string body)
		{
			try
			{
				var textCommandService = Bootstrapper.GetKernel().Resolve<Model.Services.ITextCommandService>();
				return textCommandService.DetermineType(body).Type == Model.TextCommandTypes.Stop;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Chatbot STOP classification failed; falling back to literal compare.");
				return string.Equals(body?.Trim(), "STOP", StringComparison.OrdinalIgnoreCase);
			}
		}

		public static async Task<bool> ProcessChatbotMessageQueueItem(ChatbotMessageQueueItem item)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.From) || string.IsNullOrWhiteSpace(item.Body))
				return true;

			try
			{
				var chatbotIngressService = Bootstrapper.GetKernel().Resolve<IChatbotIngressService>();
				var textMessageProvider = Bootstrapper.GetKernel().Resolve<ITextMessageProvider>();
				var cacheProvider = Bootstrapper.GetKernel().Resolve<ICacheProvider>();

				// Idempotency: the bus is at-least-once, so a redelivered item must not produce a second
				// bot reply. Keyed on the platform/persisted message id with a 24h marker. A cache
				// outage must never drop the message, so failures here fall through to processing.
				var idempotencyKey = string.IsNullOrWhiteSpace(item.MessageId) ? null : $"chatbotmsg:{item.MessageId}";
				if (idempotencyKey != null && cacheProvider != null)
				{
					try
					{
						if (!string.IsNullOrEmpty(await cacheProvider.GetStringAsync(idempotencyKey)))
							return true;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, "Chatbot idempotency check failed; processing anyway.");
					}
				}

				async Task SendReplyAsync(string text)
				{
					if ((ChatbotPlatform)item.Platform == ChatbotPlatform.WebChat)
					{
						// WebChat replies go back through the user's chatbot chat channel (persisted +
						// SignalR fan-out), never over SMS — From is a Resgrid user id here, not a phone
						// number. The ingress-resolved DepartmentId is passed through so the reply lands
						// in the department the message actually came from.
						var notifier = Bootstrapper.GetKernel().Resolve<IChatbotWebChatNotifier>();
						if (notifier != null)
							await notifier.PushToUserAsync(item.From, text, item.DepartmentId);
					}
					else
					{
						// Reply from the department's text number (To) back to the sender (From). Twilio is the
						// primary transport; carrier only governs gateway fallback, so the default is fine here.
						// Chatbot replies are interactive (help/command lists the user acts on over SMS), so they
						// use the higher chatbot length cap instead of the notification default.
						await textMessageProvider.SendTextMessage(item.From, text, item.To, default(MobileCarriers), item.DepartmentId,
							maxLengthOverride: Resgrid.Config.ChatbotConfig.SmsReplyMaxLength);
					}

					if (idempotencyKey != null && cacheProvider != null)
					{
						try
						{
							await cacheProvider.SetStringAsync(idempotencyKey, "1", TimeSpan.FromHours(24));
						}
						catch (Exception ex)
						{
							Logging.LogException(ex, "Chatbot idempotency marker write failed.");
						}
					}
				}

				// ADP department operation lock (plan section 20.2): while a migration window is open,
				// the chatbot pipeline must not run — its intents mutate cataloged department data
				// (create call, respond, close) below the API lock filter. Unlike other consumers this
				// one answers NOW with the paused banner instead of deferring: an SMS command replayed
				// half an hour later would act on stale intent. Value-free text; fail-open guard.
				// EXCEPTION: STOP always works — opting out of messages is not a department-data
				// mutation and must never be blocked by a migration lock.
				if (await DepartmentLockGuard.IsDepartmentLockedAsync(item.DepartmentId) && !IsStopCommand(item.Body))
				{
					await SendReplyAsync("Resgrid is briefly paused for scheduled maintenance in your department. Please try again shortly.");
					return true;
				}

				var message = new ChatbotMessage
				{
					MessageId = item.MessageId,
					From = item.From,
					To = item.To,
					Text = item.Body,
					Platform = (ChatbotPlatform)item.Platform,
					Timestamp = DateTime.UtcNow
				};

				// Command-board questions carry the incident the sender has open so "PAR" means "PAR on
				// this board". The ingress copies it onto the session; authorization is re-checked there.
				if (item.IncidentCallId.HasValue && item.IncidentCallId.Value > 0)
					message.PlatformMetadata["incidentCallId"] = item.IncidentCallId.Value;

				var response = await chatbotIngressService.ProcessMessageAsync(message);

				if (response != null && !string.IsNullOrWhiteSpace(response.Text))
					await SendReplyAsync(response.Text);
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
