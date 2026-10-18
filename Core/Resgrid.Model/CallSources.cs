namespace Resgrid.Model
{
	public enum CallSources
	{
		User		= 1,
		EmailImport	= 2,
		AudioImport = 3,
		Chatbot		= 4,

		/// <summary>A closed historical call brought in by Department Import (wizard plan section 16.1). Never dispatched; excluded from reports by default.</summary>
		Imported	= 5
	}
}
