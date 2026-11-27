using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 86 did.</summary>
	public class ProtocolAcknowledgmentRunResult
	{
		public int AttachmentsQueued { get; set; }
		public int TextsExtracted { get; set; }
		public int QuizzesCompleted { get; set; }
		public int RemindersSent { get; set; }
		public int MicroLearningSent { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 86 (Protocols P2, and the P1 wave-2 file text): queues PDF and DOCX attachments without text, extracts queued files
	/// (the ones too large for the upload request, bounded per file), completes quiz-gated acknowledgments whose training is now
	/// passed (the linked training and, with the LMS, the question-bank quiz), sends acknowledgment reminders at the due date and after
	/// with the escalation to the supervisor or group admin, and (LMS, round 5F) sends the weekly micro-learning question. Every step
	/// starts from a query for due work across departments (pending extractions, attachments without a text row, rows waiting for a
	/// quiz, rows owing a reminder stage, departments whose micro-learning week is due); it never sweeps every department. Only the
	/// micro-learning step asks the LMS gate, once per due department (acknowledgment itself is free).
	/// </summary>
	public class ProtocolAcknowledgmentLogic
	{
		public async Task<ProtocolAcknowledgmentRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<IProtocolDocumentService>(), scope.Resolve<IProtocolAcknowledgmentService>(), scope.Resolve<IProtocolLearningExtensions>(),
				DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<ProtocolAcknowledgmentRunResult> Process(IProtocolDocumentService documents, IProtocolAcknowledgmentService acknowledgments,
			IProtocolLearningExtensions learning, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new ProtocolAcknowledgmentRunResult();

			result.AttachmentsQueued = await Step(() => documents.QueueAttachmentExtractionsAsync(utcNow, cancellationToken), result, "attachment queue", cancellationToken);
			result.TextsExtracted = await Step(() => documents.ProcessPendingExtractionsAsync(utcNow, cancellationToken), result, "text extraction", cancellationToken);
			result.QuizzesCompleted = await Step(() => acknowledgments.CompletePassedQuizzesAsync(utcNow, cancellationToken), result, "quiz completion", cancellationToken);
			result.RemindersSent = await Step(() => acknowledgments.SendRemindersAsync(utcNow, cancellationToken), result, "reminders", cancellationToken);
			result.MicroLearningSent = await Step(() => learning.RunMicroLearningAsync(utcNow, cancellationToken), result, "micro-learning", cancellationToken);

			return result;
		}

		private static async Task<int> Step(Func<Task<int>> work, ProtocolAcknowledgmentRunResult result, string name, CancellationToken cancellationToken)
		{
			try
			{
				return await work();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// One failing step does not stop the others; the next run picks the work up again.
				Logging.LogException(ex, $"Protocol acknowledgment {name} failed.");
				result.Failures++;
				return 0;
			}
		}
	}
}
