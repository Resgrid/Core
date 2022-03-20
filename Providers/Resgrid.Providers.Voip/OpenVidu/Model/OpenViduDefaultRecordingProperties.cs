using Newtonsoft.Json;

namespace Resgrid.Providers.Voip.OpenVidu.Model
{
	public class OpenViduDefaultRecordingProperties
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("hasAudio")]
		public bool HasAudio { get; set; }

		[JsonProperty("hasVideo")]
		public bool HasVideo { get; set; }

		[JsonProperty("outputMode")]
		public string OutputMode { get; set; }

		[JsonProperty("recordingLayout")]
		public string RecordingLayout { get; set; }

		[JsonProperty("resolution")]
		public string Resolution { get; set; }

		[JsonProperty("frameRate")]
		public int FrameRate { get; set; }

		[JsonProperty("shmSize")]
		public int ShmSize { get; set; }
	}
}
