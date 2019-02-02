using NAudio.Wave;
using Resgrid.Model.Providers;
using System;
using System.IO;

namespace Resgrid.Providers.Audio
{
	public class AudioValidatorProvider: IAudioValidatorProvider
	{
		public TimeSpan? GetWavFileDuration(Stream fileStream)
		{
			try
			{
				WaveFileReader wf = new WaveFileReader(fileStream);
				return wf.TotalTime;
			}
			catch
			{
				return null;
			}
		}

		public TimeSpan? GetMp3FileDuration(Stream fileStream)
		{
			try
			{
				Mp3FileReader mpf = new Mp3FileReader(fileStream);
				return mpf.TotalTime;
			}
			catch
			{
				return null;
			}
		}
	}
}
