using System.Collections.Generic;
using System.IO;

namespace Resgrid.Model.Providers
{
	public interface IKmlProvider
	{
		List<Coordinates> ImportFile(Stream input, bool isKmz);
	}
}