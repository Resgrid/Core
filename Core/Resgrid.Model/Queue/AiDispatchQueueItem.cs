using System;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	/// <summary>
	/// One Enrich-mode AI dispatch job (aidispatchtriage queue, ai-dispatch-template-plan.md). Identifiers only: the worker reads
	/// the message text from the call it already created, so caller and patient detail never travel on the bus.
	/// </summary>
	[ProtoContract]
	public class AiDispatchQueueItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		[ProtoMember(2)]
		public int CallId { get; set; }

		/// <summary>An <see cref="AiDispatchChannels"/> value: 1-3 dispatch email paths, 4-8 intake channels (pending calls), 9 an intake conversation's AI turn.</summary>
		[ProtoMember(3)]
		public int Channel { get; set; }

		[ProtoMember(4)]
		public DateTime QueuedOnUtc { get; set; }

		/// <summary>The department's sender allowlist excluded the sender (decided at enqueue so no address travels on the bus).</summary>
		[ProtoMember(5)]
		public bool SenderNotAllowed { get; set; }

		/// <summary>
		/// Intake channels: the call's name is the deterministic "{channel}: {sender}" fallback (no subject), so the model's
		/// title may replace it (Rename placeholder setting). A yes/no, so the sender never travels on the bus.
		/// </summary>
		[ProtoMember(6)]
		public bool NameIsPlaceholder { get; set; }

		/// <summary>
		/// Intake SMS conversations (plan §8, M0325). With <see cref="AiDispatchChannels.IntakeConversation"/>: the thread whose next
		/// Enhanced AI turn to take. With <see cref="AiDispatchChannels.IntakeSms"/>: re-enrich the pending call from the thread's whole
		/// transcript (read by the worker from the call's notes; no text travels on the bus).
		/// </summary>
		[ProtoMember(7)]
		public string IntakeConversationId { get; set; }

		/// <summary>The texter's message (IntakeConversationMessages id) that asked for this AI turn.</summary>
		[ProtoMember(8)]
		public string IntakeConversationMessageId { get; set; }

		/// <summary>A transcript re-enrichment after the conversation's last Enhanced AI turn: the AI note (summary, suggested priority) is written.</summary>
		[ProtoMember(9)]
		public bool ConversationComplete { get; set; }
	}

	/// <summary>Where an <see cref="AiDispatchQueueItem"/> came from (persisted on the bus; never renumber).</summary>
	public static class AiDispatchChannels
	{
		public const int PostmarkEmail = 1;
		public const int GroupDispatchEmail = 2;
		public const int ImapImport = 3;

		/// <summary>Intake (call-intake-and-ai-destinations-plan.md §3): the call was saved PENDING; nobody has been dispatched.</summary>
		public const int IntakeSms = 4;
		public const int IntakeEmail = 5;
		public const int IntakeChat = 6;
		public const int IntakeWebhook = 7;
		public const int IntakeVoice = 8;

		/// <summary>
		/// The next Enhanced AI turn of an intake SMS conversation (plan §8 D16): not an enrichment; the aidispatchtriage consumer hands
		/// it to the conversation service, which texts the caller and then re-queues the enrichment (IntakeSms + conversation id).
		/// </summary>
		public const int IntakeConversation = 9;

		/// <summary>True for the intake channels (4-8), whose calls are pending and gated by Ai.Intake / Ai.VoiceIntake.</summary>
		public static bool IsIntake(int channel) => channel >= IntakeSms && channel <= IntakeVoice;

		/// <summary>An enrichment that reads an intake SMS conversation's transcript instead of the call's first message.</summary>
		public static bool IsConversationRefresh(AiDispatchQueueItem item) =>
			item != null && item.Channel == IntakeSms && !string.IsNullOrWhiteSpace(item.IntakeConversationId);

		/// <summary>The Enhanced AI capability flag an intake channel needs (Ai.VoiceIntake for voice, else Ai.Intake).</summary>
		public static string IntakeCapabilityFlag(int channel) =>
			channel == IntakeVoice ? FeatureFlagKeys.AiVoiceIntake : FeatureFlagKeys.AiIntake;

		/// <summary>The AI channel for an intake transport.</summary>
		public static int ForIntake(IntakeChannelTypes channelType)
		{
			switch (channelType)
			{
				case IntakeChannelTypes.Sms:
					return IntakeSms;
				case IntakeChannelTypes.Email:
					return IntakeEmail;
				case IntakeChannelTypes.Webhook:
					return IntakeWebhook;
				case IntakeChannelTypes.Voice:
					return IntakeVoice;
				default:
					return IntakeChat;
			}
		}
	}
}
