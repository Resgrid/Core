using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	public class ConversationEngine : IConversationEngine
	{
		public Task<ConversationResult> ProcessAsync(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent)
		{
			if (session == null)
				return Task.FromResult(new ConversationResult
				{
					IsComplete = true,
					NeedsFollowUp = false,
					NextState = ChatbotDialogState.Idle
				});

			// If we're in a multi-turn state, handle continuation
			switch (session.State)
			{
				case ChatbotDialogState.AwaitingParameter:
					return HandleParameterResponse(message, session, intent);

				case ChatbotDialogState.AwaitingConfirmation:
					return HandleConfirmationResponse(message, session, intent);

				case ChatbotDialogState.AwaitingAuth:
					return HandleAuthResponse(message, session, intent);

				case ChatbotDialogState.AwaitingResponse:
					return HandleGeneralResponse(message, session, intent);

				default:
					// Idle or new intent - check if this needs multi-turn
					return HandleNewIntent(message, session, intent);
			}
		}

		/// <summary>
		/// Phase 2: Called by IngressService when the session is already in a multi-turn state.
		/// Processes the continuation of an ongoing dialog.
		/// </summary>
		public async Task<ChatbotResponse> HandleContinuationAsync(ChatbotMessage message, ChatbotSession session)
		{
			if (session == null || session.State == ChatbotDialogState.Idle)
				return null;

			// The user sent a message while in a multi-turn state — it's a parameter/response
			switch (session.State)
			{
				case ChatbotDialogState.AwaitingParameter:
					session.Context["lastResponse"] = message.Text;
					if (session.PendingIntent.HasValue)
					{
						var pendingIntent = session.PendingIntent.Value;
						switch (pendingIntent)
						{
							case ChatbotIntentType.SendMessage when !session.Context.ContainsKey("body"):
								session.Context["body"] = message.Text;
								session.State = ChatbotDialogState.Idle;
								session.PendingIntent = null;
								return new ChatbotResponse
								{
									Text = $"Message sent to {TryGetContext(session, "recipient", "recipient")}: \"{message.Text}\"",
									Processed = true
								};
							default:
								session.Context["parameterValue"] = message.Text;
								session.State = ChatbotDialogState.Idle;
								session.PendingIntent = null;
								return new ChatbotResponse
								{
									Text = "Received. Processing your request...",
									Processed = true
								};
						}
					}
					break;

				case ChatbotDialogState.AwaitingConfirmation:
					var confirm = message.Text?.Trim().ToUpperInvariant();
					if (confirm == "YES" || confirm == "Y" || confirm == "CONFIRM" || confirm == "OK")
					{
						session.State = ChatbotDialogState.Idle;
						session.PendingIntent = null;
						return new ChatbotResponse { Text = "Confirmed.", Processed = true };
					}
					if (confirm == "NO" || confirm == "N" || confirm == "CANCEL")
					{
						session.Reset();
						return new ChatbotResponse { Text = "Cancelled.", Processed = true };
					}
					return new ChatbotResponse { Text = "Please reply YES to confirm or NO to cancel.", Processed = true };

				case ChatbotDialogState.AwaitingAuth:
					session.Context["linkingCode"] = message.Text?.Trim();
					session.State = ChatbotDialogState.Idle;
					session.PendingIntent = null;
					return new ChatbotResponse { Text = "Processing your linking code...", Processed = true };
			}

			return null;
		}

		/// <summary>
		/// Phase 2: Determines if an intent type requires multi-turn dialog.
		/// </summary>
		public bool IsMultiTurnIntent(ChatbotIntentType intentType)
		{
			return intentType switch
			{
				ChatbotIntentType.SendMessage => true,
				ChatbotIntentType.SetStatus => true,
				ChatbotIntentType.SetStaffing => true,
				ChatbotIntentType.DispatchCall => true,
				ChatbotIntentType.CloseCall => true,
				_ => false
			};
		}

		/// <summary>
		/// Phase 2: Begins a multi-turn dialog for an intent, returning the first prompt.
		/// </summary>
		public Task<ChatbotResponse> BeginDialogAsync(ChatbotIntent intent, ChatbotSession session)
		{
			session.State = ChatbotDialogState.AwaitingParameter;
			session.PendingIntent = intent.Type;

			var prompt = BuildParameterPrompt(intent);
			return Task.FromResult(new ChatbotResponse
			{
				Text = prompt,
				Processed = true,
				Intent = intent
			});
		}

		// Helper to safely get context values
		private static string TryGetContext(ChatbotSession session, string key, string defaultValue)
		{
			if (session?.Context != null && session.Context.TryGetValue(key, out var value))
				return value;
			return defaultValue;
		}

		private Task<ConversationResult> HandleNewIntent(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent)
		{
			// Check if the intent requires multi-turn (needs parameters not provided)
			if (NeedsParameterCollection(intent))
			{
				session.State = ChatbotDialogState.AwaitingParameter;
				session.PendingIntent = intent.Type;

				var prompt = BuildParameterPrompt(intent);
				return Task.FromResult(new ConversationResult
				{
					Response = new ChatbotResponse
					{
						Text = prompt,
						Processed = true,
						Intent = intent
					},
					IsComplete = false,
					NeedsFollowUp = true,
					NextState = ChatbotDialogState.AwaitingParameter
				});
			}

			// Simple intent - complete in one turn
			session.State = ChatbotDialogState.Completed;
			return Task.FromResult(new ConversationResult
			{
				IsComplete = true,
				NeedsFollowUp = false,
				NextState = ChatbotDialogState.Idle
			});
		}

		private Task<ConversationResult> HandleParameterResponse(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent)
		{
			// User provided a parameter while we were waiting
			// Store the parameter and either complete or ask for more
			session.Context["lastResponse"] = message.Text;

			if (session.PendingIntent.HasValue)
			{
				var pendingIntent = session.PendingIntent.Value;

				switch (pendingIntent)
				{
					case ChatbotIntentType.SendMessage when !session.Context.ContainsKey("body"):
						session.Context["body"] = message.Text;
						return Task.FromResult(new ConversationResult
						{
							Response = new ChatbotResponse
							{
								Text = $"Message sent to {TryGetContext(session, "recipient", "recipient")}: \"{message.Text}\"",
								Processed = true
							},
							IsComplete = true,
							NeedsFollowUp = false,
							NextState = ChatbotDialogState.Idle
						});

					default:
						// Complete the pending intent with the provided parameter
						session.Context["parameterValue"] = message.Text;
						session.State = ChatbotDialogState.Completed;
						return Task.FromResult(new ConversationResult
						{
							IsComplete = true,
							NeedsFollowUp = false,
							NextState = ChatbotDialogState.Idle
						});
				}
			}

			session.State = ChatbotDialogState.Idle;
			session.PendingIntent = null;
			return Task.FromResult(new ConversationResult
			{
				IsComplete = true,
				NeedsFollowUp = false,
				NextState = ChatbotDialogState.Idle
			});
		}

		private Task<ConversationResult> HandleConfirmationResponse(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent)
		{
			var response = message.Text?.Trim().ToUpperInvariant();

			if (response == "YES" || response == "Y" || response == "CONFIRM" || response == "OK")
			{
				session.State = ChatbotDialogState.Completed;
				return Task.FromResult(new ConversationResult
				{
					Response = new ChatbotResponse { Text = "Confirmed.", Processed = true },
					IsComplete = true,
					NeedsFollowUp = false,
					NextState = ChatbotDialogState.Idle
				});
			}

			if (response == "NO" || response == "N" || response == "CANCEL")
			{
				session.Reset();
				return Task.FromResult(new ConversationResult
				{
					Response = new ChatbotResponse { Text = "Cancelled.", Processed = true },
					IsComplete = true,
					NeedsFollowUp = false,
					NextState = ChatbotDialogState.Idle
				});
			}

			// Unrecognized - ask again
			return Task.FromResult(new ConversationResult
			{
				Response = new ChatbotResponse { Text = "Please reply YES to confirm or NO to cancel.", Processed = true },
				IsComplete = false,
				NeedsFollowUp = true,
				NextState = ChatbotDialogState.AwaitingConfirmation
			});
		}

		private Task<ConversationResult> HandleAuthResponse(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent)
		{
			// User sent a linking code
			var code = message.Text?.Trim();
			session.Context["linkingCode"] = code;
			session.State = ChatbotDialogState.Completed;
			return Task.FromResult(new ConversationResult
			{
				IsComplete = true,
				NeedsFollowUp = false,
				NextState = ChatbotDialogState.Idle
			});
		}

		private Task<ConversationResult> HandleGeneralResponse(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent)
		{
			// General awaited response - store and complete
			session.Context["response"] = message.Text;
			session.State = ChatbotDialogState.Completed;
			return Task.FromResult(new ConversationResult
			{
				IsComplete = true,
				NeedsFollowUp = false,
				NextState = ChatbotDialogState.Idle
			});
		}

		private static bool NeedsParameterCollection(ChatbotIntent intent)
		{
			if (intent == null) return false;

			return intent.Type switch
			{
				ChatbotIntentType.SendMessage => !intent.Parameters.ContainsKey("body"),
				ChatbotIntentType.SetStatus => !intent.Parameters.ContainsKey("actionType") && !intent.Parameters.ContainsKey("customActionId") && !intent.Parameters.ContainsKey("statusName"),
				ChatbotIntentType.SetStaffing => !intent.Parameters.ContainsKey("staffingType") && !intent.Parameters.ContainsKey("staffingName"),
				ChatbotIntentType.DispatchCall => !intent.Parameters.ContainsKey("description"),
				ChatbotIntentType.CloseCall => !intent.Parameters.ContainsKey("callId"),
				_ => false
			};
		}

		private static string BuildParameterPrompt(ChatbotIntent intent)
		{
			return intent.Type switch
			{
				ChatbotIntentType.SendMessage when intent.Parameters.TryGetValue("recipient", out var recipient)
					=> $"What message should I send to {recipient}?",

				ChatbotIntentType.SendMessage
					=> "Who would you like to send a message to, and what should it say?",

				ChatbotIntentType.SetStatus
					=> "Which status? Text a number or name:\n(1) Responding\n(2) Not Responding\n(3) On Scene\n(4) Standing By",

				ChatbotIntentType.SetStaffing
					=> "Which staffing level? Text a number or name:\n(S1) Available\n(S2) Delayed\n(S3) Unavailable\n(S4) Committed\n(S5) On Shift",

				ChatbotIntentType.DispatchCall
					=> "Please provide the call details (e.g., 'Structure fire at 123 Main St')",

				ChatbotIntentType.CloseCall
					=> "Which call would you like to close? Reply with the call number (e.g., C1445)",

				_ => "Please provide more details."
			};
		}
	}
}
