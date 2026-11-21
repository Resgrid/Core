namespace Resgrid.Model
{
	public enum CallSources
	{
		User		= 1,
		EmailImport	= 2,
		AudioImport = 3,
		Chatbot		= 4,

		/// <summary>A closed historical call brought in by Department Import (wizard plan section 16.1). Never dispatched; excluded from reports by default.</summary>
		Imported	= 5,

		/// <summary>Created from an intake channel message (SMS, email, chat, webhook; M0323). Saved pending.</summary>
		Intake		= 6,

		/// <summary>Created by Enhanced AI voice intake on the department's dedicated number (M0323). Saved pending.</summary>
		VoiceIntake	= 7
	}
}
