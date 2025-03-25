using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Voice
{
	public class VoiceIndexModel
	{
		public bool CanUseVoice { get; set; }
		public DepartmentVoice Voice { get; set; }
		public List<DepartmentAudio> Audios { get; set; }
	}
}
