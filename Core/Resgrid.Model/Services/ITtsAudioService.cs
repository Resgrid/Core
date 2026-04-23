using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ITtsAudioService
	{
		Task<Uri> GenerateSpeechUrlAsync(string text, string voice = null, int? speed = null, CancellationToken cancellationToken = default);

		Task RegenerateStaticPromptsAsync(IEnumerable<string> prompts, CancellationToken cancellationToken = default);
	}
}
