namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// Preprocesses dispatch text before it is sent to the TTS engine so that
	/// common abbreviations, codes, and jargon are spoken intelligibly.
	/// </summary>
	public interface ITextPreprocessor
	{
		/// <summary>
		/// Normalises the input text for the given voice / language so that Piper
		/// (or any downstream TTS engine) produces the most natural speech.
		/// </summary>
		string Preprocess(string text, string voice);
	}
}