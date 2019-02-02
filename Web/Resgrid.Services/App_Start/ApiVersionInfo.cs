using System.Web.Http.Description;

namespace Resgrid.Web.Services.App_Start
{
	public class VersionedApiDescription : ApiDescription
	{
		public ApiVersionInfo VersionInfo { get; set; }
	}

	public class ApiVersionInfo
	{
		public static ApiVersionInfo None = new ApiVersionInfo("");

		public ApiVersionInfo(string version)
		{
			Version = version ?? string.Empty;
			if (!string.IsNullOrEmpty(version))
			{
				Order = int.Parse(version);
			}
		}

		public string Version { get; private set; }
		public int? Order { get; private set; }

		public override bool Equals(object obj)
		{
			if (obj == null || GetType() != obj.GetType())
			{
				return false;
			}

			return this.Version.Equals(((ApiVersionInfo)obj).Version);
		}

		public override int GetHashCode()
		{
			return this.Version.GetHashCode();
		}

		internal static ApiVersionInfo New(string version)
		{
			if (string.IsNullOrEmpty(version))
				return None;
			return new ApiVersionInfo(version);
		}
	}
}