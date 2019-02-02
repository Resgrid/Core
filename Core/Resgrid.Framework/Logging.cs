using System;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Runtime.CompilerServices;
using Resgrid.Config;
using SharpRaven;
using SharpRaven.Data;

namespace Resgrid.Framework
{
	public static class Logging
	{
		private static RavenClient _ravenClient;
		private static bool _isInitialized;
		private static bool _consoleVisible;

		public static void Initialize(string key)
		{
			if (_isInitialized == false)
			{
				if (String.IsNullOrWhiteSpace(key))
					_ravenClient = new RavenClient(ExternalErrorConfig.ExternalErrorServiceUrl);
				else
					_ravenClient = new RavenClient(key);

				_isInitialized = true;
			}
		}

		private static void ShowConsole()
		{
			if (!_consoleVisible)
			{
				//Win32.AllocConsole();
				_consoleVisible = true;
			}
		}

		public static void LogException(Exception exception, string extraMessage = "", 
			[CallerFilePath] string callerFilePath = "", [CallerMemberName] string callerMemberName = "", [CallerLineNumber] int callerLineNumber = 0)
		{
			Initialize(null);
			string msgToLog = string.Format("{0}\r\n{4}\r\n\r\nAssemblyName:{5}\r\nCallerFilePath:{1}\r\nCallerMemberName:{2}\r\nCallerLineNumber:{3}", extraMessage,
				callerFilePath, callerMemberName, callerLineNumber, exception.ToString(), Assembly.GetExecutingAssembly().FullName);

			_ravenClient.Capture(new SentryEvent(exception));
			//_logger.Log(LogLevel.Fatal, msgToLog, exception);
		}

		public static void LogError(string message)
		{
			Initialize(null);

			//_logger.Error(message);
			_ravenClient.Capture(new SentryEvent(message));
		}

		public static void LogDebug(string message)
		{
			Initialize(null);

			_ravenClient.Capture(new SentryEvent(message));
			//_logger.Debug(message);
		}

		public static void LogInfo(string message)
		{
			Initialize(null);

			_ravenClient.Capture(new SentryEvent(message));
			//_logger.Info(string.Format("Message:{1}", message));
		}

		public static void LogTrace(string message)
		{
			Initialize(null);

			_ravenClient.Capture(new SentryEvent(message));
			//_logger.Trace(message);
		}

		public static void SendExceptionEmail(Exception exmail, string processName)
		{
			String ErrorlineNo, Errormsg, ErrorLocation, InnerException, extype, Frommail, ToMail, Sub, HostAdd, EmailHead, EmailSing;

			try
			{
				var newline = "<br/>";
				ErrorlineNo = exmail.StackTrace.Substring(exmail.StackTrace.Length - 7, 7);
				Errormsg = exmail.GetType().Name.ToString();
				extype = exmail.GetType().ToString();
				ErrorLocation = exmail.Message.ToString();

				if (exmail.InnerException != null)
					InnerException = exmail.InnerException.ToString();
				else
					InnerException = "No Inner Exception";

				EmailHead = "<b>Dear Team,</b>" + "<br/>" + "An exception occurred in [" + processName + "] " + "With following Details" + "<br/>" + "<br/>";
				EmailSing = newline + "Thanks and Regards" + newline + "    " + "     " + "<b>Application Admin </b>" + "</br>";
				Sub = "Exception occurred" + " " + "in [" +  processName + "]";
				
				string errortomail = EmailHead + "<b>Log Written Date: </b>" + " " + DateTime.Now.ToString() + newline + "<b>Error Line No :</b>" + " " + ErrorlineNo + "\t\n" + " " + newline + "<b>Error Message:</b>" + " " + Errormsg + newline + "<b>Exception Type:</b>" + " " + extype + newline + "<b> Error Details :</b>" + " " + ErrorLocation + newline + "<b> Inner Details :</b>" + " " + InnerException + newline + "<b>System:</b>" + " " + processName + newline + newline + newline + newline + EmailSing;

				using (MailMessage mailMessage = new MailMessage())
				{
					Frommail = OutboundEmailServerConfig.FromMail;
					ToMail = OutboundEmailServerConfig.ToMail;
					mailMessage.From = new MailAddress(Frommail);
					mailMessage.Subject = Sub;
					mailMessage.Body = errortomail;
					mailMessage.IsBodyHtml = true;

					string[] MultiEmailId = ToMail.Split(',');
					foreach (string userEmails in MultiEmailId)
					{
						mailMessage.To.Add(new MailAddress(userEmails));
					}

					SmtpClient smtp = new SmtpClient();
					smtp.Host = OutboundEmailServerConfig.Host;
					smtp.EnableSsl = OutboundEmailServerConfig.EnableSsl;
					NetworkCredential NetworkCred = new NetworkCredential();
					NetworkCred.UserName = OutboundEmailServerConfig.UserName;
					NetworkCred.Password = OutboundEmailServerConfig.Password;
					smtp.UseDefaultCredentials = true;
					smtp.Credentials = NetworkCred;
					smtp.Port = OutboundEmailServerConfig.Port;
					smtp.Send(mailMessage); //sending Email  

				}
			}
			catch{}
		}

		public static void Write(string message)
		{
			ShowConsole();
			Console.WriteLine(message);
		}
	}
}
