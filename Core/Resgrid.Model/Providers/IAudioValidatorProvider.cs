using System;
using System.IO;

namespace Resgrid.Model.Providers
{
	public interface IAudioValidatorProvider
	{
		TimeSpan? GetWavFileDuration(Stream fileStream);
		TimeSpan? GetMp3FileDuration(Stream fileStream);
	}
}
