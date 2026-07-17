using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	public class ConversationEngine : IConversationEngine
	{
		private readonly ICustomStateService _customStateService;

		public ConversationEngine(ICustomStateService customStateService = null)
		{
			_customStateService = customStateService;
		}

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
							// SendMessage is single-turn (handled inline by MessageSendHandler — see
							// IsMultiTurnIntent), so it never parks here and no fake "Message sent" is reported.
							default:
								session.Context["parameterValue"] = message.Text;
								session.State = ChatbotDialogState.Idle;
								session.PendingIntent = null;
								return new ChatbotResponse
								{
									Text = ChatbotResources.Get("Conv_ReceivedProcessing", session.Culture),
									Processed = true
								};
						}
					}
					break;

				case ChatbotDialogState.AwaitingConfirmation:
					if (ChatbotResources.IsAffirmative(message.Text, session.Culture))
					{
						session.State = ChatbotDialogState.Idle;
						session.PendingIntent = null;
						return new ChatbotResponse { Text = ChatbotResources.Get("Conv_Confirmed", session.Culture), Processed = true };
					}
					if (ChatbotResources.IsNegative(message.Text, session.Culture))
					{
						session.Reset();
						return new ChatbotResponse { Text = ChatbotResources.Get("Conv_Cancelled", session.Culture), Processed = true };
					}
					return new ChatbotResponse { Text = ChatbotResources.Get("Conv_ConfirmPrompt", session.Culture), Processed = true };

				case ChatbotDialogState.AwaitingAuth:
					session.Context["linkingCode"] = message.Text?.Trim();
					session.State = ChatbotDialogState.Idle;
					session.PendingIntent = null;
					return new ChatbotResponse { Text = ChatbotResources.Get("Conv_ProcessingLinkingCode", session.Culture), Processed = true };
			}

			return null;
		}

		/// <summary>
		/// Phase 2: Determines if an intent type requires multi-turn dialog.
		/// </summary>
		public bool NeedsParameterCollection(ChatbotIntent intent)
		{
			if (intent == null)
				return false;

			return intent.Type switch
			{
				// SendMessage is handled single-turn by MessageSendHandler (recipient+body are captured
				// inline by the classifier). It is intentionally NOT multi-turn here: the previous
				// multi-turn path only *faked* the send. A guided "ask for the body" flow can be added later.
				ChatbotIntentType.SetStatus => !intent.Parameters.ContainsKey("actionType")
					&& !intent.Parameters.ContainsKey("customActionId")
					&& !intent.Parameters.ContainsKey("statusName"),
				ChatbotIntentType.SetStaffing => !intent.Parameters.ContainsKey("staffingType")
					&& !intent.Parameters.ContainsKey("customStaffingId")
					&& !intent.Parameters.ContainsKey("staffingName"),
				// CloseCall/DispatchCall are single-turn: their handlers run a confirmation pass via
				// AwaitingConfirmation, which ChatbotIngressService re-dispatches on YES (addendum §5).
				_ => false
			};
		}

		/// <summary>
		/// Phase 2: Begins a multi-turn dialog for an intent, returning the first prompt.
		/// </summary>
		public async Task<ChatbotResponse> BeginDialogAsync(ChatbotIntent intent, ChatbotSession session)
		{
			session.State = ChatbotDialogState.AwaitingParameter;
			session.PendingIntent = intent.Type;

			var prompt = await BuildParameterPromptAsync(intent, session);
			return new ChatbotResponse
			{
				Text = prompt,
				Processed = true,
				Intent = intent
			};
		}

		private async Task<ConversationResult> HandleNewIntent(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent)
		{
			// Check if the intent requires multi-turn (needs parameters not provided)
			if (NeedsParameterCollection(intent))
			{
				session.State = ChatbotDialogState.AwaitingParameter;
				session.PendingIntent = intent.Type;

				var prompt = await BuildParameterPromptAsync(intent, session);
				return new ConversationResult
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
				};
			}

			// Simple intent - complete in one turn
			session.State = ChatbotDialogState.Completed;
			return new ConversationResult
			{
				IsComplete = true,
				NeedsFollowUp = false,
				NextState = ChatbotDialogState.Idle
			};
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
					// SendMessage is single-turn (handled inline by MessageSendHandler — see IsMultiTurnIntent),
					// so it never reaches this multi-turn path; no fake send is reported.

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

		private async Task<string> BuildParameterPromptAsync(ChatbotIntent intent, ChatbotSession session)
		{
			if (_customStateService != null && intent.Type == ChatbotIntentType.SetStatus)
			{
				var customState = await _customStateService.GetActivePersonnelStateForDepartmentAsync(session.DepartmentId);
				var details = customState?.GetActiveDetails();
				if (details?.Any() == true)
					return BuildOptionsPrompt("Which status? Reply with a number or name:", details);
			}

			if (_customStateService != null && intent.Type == ChatbotIntentType.SetStaffing)
			{
				var customState = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(session.DepartmentId);
				var details = customState?.GetActiveDetails();
				if (details?.Any() == true)
					return BuildOptionsPrompt("Which staffing level? Reply with a number or name:", details);
			}

			return intent.Type switch
			{
				ChatbotIntentType.SendMessage when intent.Parameters.TryGetValue("recipient", out var recipient)
					=> ChatbotResources.Get("Conv_PromptSendMessageTo", session.Culture, recipient),

				ChatbotIntentType.SendMessage
					=> ChatbotResources.Get("Conv_PromptSendMessage", session.Culture),

				ChatbotIntentType.SetStatus
					=> ChatbotResources.Get("Conv_PromptSetStatus", session.Culture),

				ChatbotIntentType.SetStaffing
					=> ChatbotResources.Get("Conv_PromptSetStaffing", session.Culture),

				ChatbotIntentType.DispatchCall
					=> ChatbotResources.Get("Conv_PromptDispatchCall", session.Culture),

				ChatbotIntentType.CloseCall
					=> ChatbotResources.Get("Conv_PromptCloseCall", session.Culture),

				_ => ChatbotResources.Get("Conv_PromptDefault", session.Culture)
			};
		}

		private static string BuildOptionsPrompt(string heading, IEnumerable<CustomStateDetail> details)
		{
			var builder = new StringBuilder(heading);
			var options = details.Where(x => x != null && !x.IsDeleted).OrderBy(x => x.Order).Take(10).ToList();
			for (var i = 0; i < options.Count; i++)
				builder.Append($"\n({i + 1}) {options[i].ButtonText}");

			return builder.ToString();
		}
	}
}
