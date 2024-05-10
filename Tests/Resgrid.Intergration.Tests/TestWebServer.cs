using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

namespace Resgrid.Intergration.Tests
{
	/// <summary>
	/// <para>A Web Server useful for unit tests.  Uses the same code used by the
	/// built in WebServer (formerly known as Cassini) in VisualStudio.NET 2005.
	/// Specifically, this needs a reference to WebServer.WebHost.dll located in
	/// the GAC.
	/// </para>
	/// </summary>
	/// <remarks>
	/// <para>If you unseal this class, make sure to make Dispose(bool disposing) a protected
	/// virtual method instead of private.
	/// </para>
	/// <para>
	/// For more information, check out: http://haacked.com/archive/2006/12/12/Using_WebServer.WebDev_For_Unit_Tests.aspx
	/// </para>
	/// </remarks>
	public sealed class TestWebServer : IDisposable
	{
		private bool started = false;
		//private CassiniDevServer webServer;
		private int webServerPort;
		private string webServerVDir;
		private string sourceBinDir = AppDomain.CurrentDomain.BaseDirectory;
		private string webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebRoot");
		private string webBinDir = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebRoot"), "bin");
		private string webServerUrl; //built in Start

		/// <summary>
		/// Initializes a new instance of the <see cref="TestWebServer"/> class on port 8085.
		/// </summary>
		public TestWebServer()
			: this(8085, "/", null)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="TestWebServer"/> class
		/// using the specified port and virtual dir.
		/// </summary>
		/// <param name="port">The port.</param>
		/// <param name="virtualDir">The virtual dir.</param>
		public TestWebServer(int port, string virtualDir, string serverRoot)
		{
			this.webServerPort = port;
			this.webServerVDir = virtualDir;

			if (!String.IsNullOrEmpty(serverRoot))
			{
				webRoot = serverRoot;
				webBinDir = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebRoot"), "bin");
			}
		}

		/// <summary>
		/// Starts the webserver and returns the URL.
		/// </summary>
		public Uri Start()
		{
			//NOTE: WebServer.WebHost is going to load itself AGAIN into another AppDomain,
			// and will be getting it's Assemblies from the BIN, including another copy of itself!
			// Therefore we need to do this step FIRST because I've removed WebServer.WebHost from the GAC
			if (!Directory.Exists(webRoot))
				Directory.CreateDirectory(webRoot);

			if (!Directory.Exists(webBinDir))
				Directory.CreateDirectory(webBinDir);

			CopyAssembliesToWebServerBinDirectory();

			//Start the internal Web Server pointing to our test webroot
			//webServer = new Server(webServerPort, webServerVDir, this.webRoot);
			//webServer = new CassiniDevServer();
			//webServer.StartServer(webRoot, webServerPort, webServerVDir, "localhost");

			webServerUrl = String.Format("http://localhost:{0}{1}", webServerPort, webServerVDir);

			//webServer.Start();
			started = true;
			Debug.WriteLine(String.Format("Web Server started on port {0} with VDir {1} in physical directory {2}", webServerPort, webServerVDir, this.webRoot));
			return new Uri(webServerUrl);
		}

		private void CopyAssembliesToWebServerBinDirectory()
		{
			foreach (string file in Directory.GetFiles(this.sourceBinDir, "*.dll"))
			{
				string newFile = Path.Combine(this.webBinDir, Path.GetFileName(file));
				if (File.Exists(newFile))
				{
					File.Delete(newFile);
				}
				File.Copy(file, newFile);
			}
		}

		/// <summary>
		/// Makes a  simple GET request to the web server and returns
		/// the result as a string.
		/// </summary>
		/// <param name="page">The page.</param>
		/// <returns></returns>
		public string RequestPage(string page)
		{
			WebClient client = new WebClient();
			string url = new Uri(new Uri(this.webServerUrl), page).ToString();
			using (StreamReader reader = new StreamReader(client.OpenRead(url)))
			{
				string result = reader.ReadToEnd();
				return result;
			}
		}

		/// <summary>
		/// Makes a  simple POST request to the web server and returns
		/// the result as a string.
		/// </summary>
		/// <param name="page">The page.</param>
		/// <param name="formParameters">
		/// The form paramater to post. Should be in the format "Name=Value&Name2=Value2&...&NameN=ValueN"
		/// For extra credit, build a version of this method that uses a NameValue collection. I use a
		/// string because it's possible you may want to post flat text.
		/// </param>
		/// <returns></returns>
		public string RequestPage(string page, string formParameters)
		{
			HttpWebRequest request = WebRequest.Create(new Uri(new Uri(this.webServerUrl), page)) as HttpWebRequest;

			if (request == null)
				return null; //can't imagine this happening.

			request.UserAgent = "Resgrid Test Webserver";
			request.Timeout = 10000; //10 secs is reasonable, no?
			request.Method = "POST";
			request.ContentLength = formParameters.Length;
			request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
			request.KeepAlive = true;

			using (StreamWriter myWriter = new StreamWriter(request.GetRequestStream()))
			{
				myWriter.Write(formParameters);
			}

			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			if (response.StatusCode < HttpStatusCode.OK && response.StatusCode >= HttpStatusCode.Ambiguous)
				return "Http Status" + response.StatusCode;

			string responseText;
			using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
			{
				responseText = reader.ReadToEnd();
			}

			return responseText;
		}

		/// <summary>
		/// Extracts a resources such as an html file or aspx page to the webroot directory
		/// and returns the filepath.
		/// </summary>
		/// <param name="resourceName">Name of the resource.</param>
		/// <param name="destinationFileName">Name of the destination file.</param>
		/// <returns></returns>
		public string ExtractResource(string resourceName, string destinationFileName)
		{
			if (!started)
				throw new InvalidOperationException("Please start the webserver before extracting resources.");

			//NOTE: if you decide to drop this class into a separate assembly (for example,
			//into a unit test helper assembly to make it more reusable),
			//call Assembly.GetCallingAssembly() instead.
			Assembly a = Assembly.GetExecutingAssembly();
			string filePath;
			using (Stream stream = a.GetManifestResourceStream(resourceName))
			{
				filePath = Path.Combine(this.webRoot, destinationFileName);
				using (StreamWriter outfile = File.CreateText(filePath))
				{
					using (StreamReader infile = new StreamReader(stream))
					{
						outfile.Write(infile.ReadToEnd());
					}
				}
			}
			return filePath;
		}

		/// <summary>
		/// Stops this instance.
		/// </summary>
		public void Stop()
		{
			Dispose();
		}

		~TestWebServer()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		///<summary>
		///Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
		///</summary>
		/// <remarks>
		/// If we unseal this class, make sure this is protected virtual.
		/// </remarks>
		///<filterpriority>2</filterpriority>
		private void Dispose(bool disposing)
		{
			if (disposing)
			{
				ReleaseManagedResources();
			}
		}

		// Cleans up the directories we created.
		private void ReleaseManagedResources()
		{
			//if (this.webServer != null)
			//{
			//	//this.webServer.Stop();
			//	this.webServer.StopServer();
			//	this.webServer = null;
			//	this.started = false;
			//}

			//if (Directory.Exists(this.webBinDir))
			//  Directory.Delete(this.webBinDir, true);

			//if (Directory.Exists(this.webRoot))
			//  Directory.Delete(this.webRoot, true);
		}
	}
}
