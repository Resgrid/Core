using System;
using System.IO;
using System.Reflection;

namespace Resgrid.Framework
{
	public static class EmbeddedResources
	{
		public static byte[] GetApiRequestFile(Type typeInAssembly, string namespaceAndFileName)
		{
			try
			{
				using (var stream = typeInAssembly.GetTypeInfo().Assembly.GetManifestResourceStream(namespaceAndFileName))
				using (var memoryStream = new MemoryStream())
				{
					stream.CopyTo(memoryStream);
					return memoryStream.ToArray();
				}
			}

			catch(Exception exception)
			{
				throw new Exception($"Failed to read Embedded Resource {namespaceAndFileName}");
			}
		}
	}
}
