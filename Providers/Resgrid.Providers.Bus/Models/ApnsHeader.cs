
using Newtonsoft.Json;

namespace Resgrid.Providers.Bus.Models
{
	public class ApnsHeader
	{
		public ApnsAlert alert { get; set; }
		public string category { get; set; }
		public int badge { get; set; }
		public ApnsSound sound { get; set; }
	}
}
