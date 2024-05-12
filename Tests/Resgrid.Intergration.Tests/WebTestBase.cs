using System;
using System.IO;
using System.Reflection;
using Resgrid.Intergration.Tests.Helpers;

//del $(ProjectDir)Website\*.*  /s /f /q

namespace Resgrid.Intergration.Tests
{
	public class WebTestBase
	{
		private TestWebServer _testWebServer;
		private const string applicationName = "Resgrid.Web";
		public int WebServerPort { get; set; }

		static WebTestBase()
		{
			//Database.DefaultConnectionFactory = new SqlCeConnectionFactory("System.Data.SqlServerCe.4.0");
			//Database.SetInitializer<DataContext>(new ResgridDbInitializer());

			if (File.Exists(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory))) + "\\bin\\x86\\Debug\\ResgridContext.sdf"))
				File.Delete(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory))) + "\\bin\\x86\\Debug\\ResgridContext.sdf");

			//var migrator = new DbMigrator(new ResgridTestConfiguration());
			//migrator.Update();
		}

		protected virtual void StartWebServer()
		{
			if (!File.Exists(GetApplicationPath("", "") + "\\Website\\bin\\ResgridContext.sdf"))
			{
				File.Copy(GetApplicationPath("", "") + "\\bin\\x86\\Debug\\ResgridContext.sdf",
						  GetApplicationPath("", "") + "\\Website\\bin\\ResgridContext.sdf");

				File.Copy(GetApplicationPath("", "") + "\\bin\\x86\\Debug\\ResgridContext.sdf",
									GetApplicationPath("", "") + "\\Website\\ResgridContext.sdf");

				File.Copy(GetApplicationPath("", "") + "\\bin\\x86\\Debug\\ResgridContext.sdf",
									GetApplicationPath("", "") + "\\Website\\App_Data\\ResgridContext.sdf");
			}

			WebServerPort = TcpPortFinder.FindOpenTcpPortInRange(8100, 8200);
			//string webAppPath = GetApplicationPath(applicationName, "\\Web");
			string webAppPath = GetApplicationPath("Website", "");
			_testWebServer = new TestWebServer(WebServerPort, @"/", webAppPath);

			_testWebServer.Start();
		}

		protected virtual void StopWebServer()
		{
			_testWebServer.Stop();
			_testWebServer.Dispose();
		}

		protected virtual string GetApplicationPath(string applicationName, string subDirectory)
		{
			//var solutionFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)))));
			var solutionFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)));

			return Path.Combine(solutionFolder + subDirectory, applicationName);
		}
	}

	#region Another WebTestBase using IIS Express
	//public class WebTestBase
	//{
	//  private const int iisPort = 55403;
	//  private const string applicationName = "Resgrid.Web";
	//  private Process _iisProcess;

	//  public void StopWebServer()
	//  {
	//    if (_iisProcess.HasExited == false)
	//    {
	//      _iisProcess.Kill();
	//    }
	//  }

	//  public void StartWebServer()
	//  {
	//    var applicationPath = GetApplicationPath(applicationName, "\\Web");
	//    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

	//    _iisProcess = new Process();
	//    _iisProcess.StartInfo.FileName = programFiles + "\\IIS Express\\iisexpress.exe";
	//    _iisProcess.StartInfo.Arguments = string.Format("/path:\"{0}\" /port:{1}", applicationPath, iisPort);
	//    ;
	//    _iisProcess.Start();
	//  }

	//  protected virtual string GetApplicationPath(string applicationName, string subDirectory)
	//  {
	//    var solutionFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory))));
	//    return Path.Combine(solutionFolder + subDirectory, applicationName);
	//  }

	//  public string GetAbsoluteUrl(string relativeUrl)
	//  {
	//    if (!relativeUrl.StartsWith("/"))
	//    {
	//      relativeUrl = "/" + relativeUrl;
	//    }
	//    return String.Format("http://localhost:{0}{1}", iisPort, relativeUrl);
	//  }
	//}
	#endregion
}
