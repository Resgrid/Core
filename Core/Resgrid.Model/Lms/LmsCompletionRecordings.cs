using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	// Round 7B (permission 135): what the class roster's "Record completion" panel shows (LMS layer view types).

	/// <summary>One roster row: who it is, whether they attended, whether a completion can be recorded and the recording they already have.</summary>
	public sealed class LmsClassRecordingRow
	{
		public LmsClassEnrollment Enrollment { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool IsExternal { get; set; }
		public bool Attended { get; set; }
		/// <summary>Why a completion cannot be recorded for them now ("training_record_*"); null when it can.</summary>
		public string Refusal { get; set; }
		/// <summary>The live (not voided) recording of this class's target for them, when there is one.</summary>
		public TrainingCompletionRecording Recording { get; set; }
		/// <summary>Ticked by default: attended and recordable.</summary>
		public bool Selected => Attended && Refusal == null && Recording == null;
	}

	public sealed class LmsClassRecordingView
	{
		public LmsClassSession Session { get; set; }
		/// <summary>What the class's completion records: the linked LMS course or baseline training (null: none linked).</summary>
		public TrainingCompletionRecordingTargets? Target { get; set; }
		public int? TargetId { get; set; }
		public string TargetName { get; set; }
		/// <summary>The viewer holds Record Training Completions (135) at all.</summary>
		public bool CanRecord { get; set; }
		/// <summary>Why nothing can be recorded from this class now (no linked training, not started, cancelled); null when it can.</summary>
		public string Refusal { get; set; }
		public List<LmsClassRecordingRow> Rows { get; set; } = new List<LmsClassRecordingRow>();
		public List<TrainingCompletionRecording> Recordings { get; set; } = new List<TrainingCompletionRecording>();
		public string AttestationVersion { get; set; } = TrainingCompletionAttestation.CurrentVersion;
		public int Selectable => Rows.Count(r => r.Refusal == null && r.Recording == null);
	}
}
