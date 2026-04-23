namespace Resgrid.Web.Tts.Services
{
	public sealed record TtsAudioContent(byte[] AudioBytes, string ContentType, string EntityTag, DateTimeOffset LastModified);
}
