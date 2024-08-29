using Resgrid.Config;
using Sentry;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Resgrid.Framework
{
	public static class Logging
	{
		private static Logger _logger;
		private static bool _isInitialized;
		private static bool _consoleVisible;

		public static void Initialize(string key)
		{
			if (!_isInitialized)
			{
				if (SystemBehaviorConfig.ErrorLoggerType == ErrorLoggerTypes.Sentry)
				{
					string dsn = ExternalErrorConfig.ExternalErrorServiceUrlForApi;
					if (!String.IsNullOrWhiteSpace(key))
						dsn = key;

					_logger = new LoggerConfiguration()
											.Enrich.FromLogContext()
											.MinimumLevel.Debug()
											.WriteTo.Sentry(o =>
											{
												o.MinimumBreadcrumbLevel = LogEventLevel.Debug;
												o.MinimumEventLevel = LogEventLevel.Error;
												o.Dsn = dsn;
												o.Environment = ExternalErrorConfig.Environment;
												o.Release = Assembly.GetEntryAssembly().GetName().Version.ToString();
											}).CreateLogger();
				}
				else
				{
					_logger = new LoggerConfiguration()
											.Enrich.FromLogContext()
											.MinimumLevel.Debug()
											.WriteTo.Console()
											.CreateLogger();
				}

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

		public static void LogException(Exception exception, string extraMessage = "", string correlationId = "", 
			[CallerFilePath] string callerFilePath = "", [CallerMemberName] string callerMemberName = "", [CallerLineNumber] int callerLineNumber = 0)
		{
			Initialize(null);
			string msgToLog = string.Format("{0}\r\n{4}\r\n\r\nAssemblyName:{5}\r\nCallerFilePath:{1}\r\nCallerMemberName:{2}\r\nCallerLineNumber:{3}r\nCorrelationId:{6}", extraMessage,
				callerFilePath, callerMemberName, callerLineNumber, exception.ToString(), Assembly.GetExecutingAssembly().FullName, correlationId);


			if (_logger != null)
				_logger.Fatal(exception, msgToLog);

			Console.WriteLine(exception.ToString() + $" {extraMessage}");
		}

		public static void LogError(string message)
		{
			Initialize(null);


			if (_logger != null)
				_logger.Error(message);

		}

		public static void LogDebug(string message)
		{
			Initialize(null);


			if (_logger != null)
				_logger.Debug(message);

		}

		public static void LogInfo(string message)
		{
			Initialize(null);


			if (_logger != null)
				_logger.Information(message);

		}

		public static void LogTrace(string message)
		{
			Initialize(null);


			if (_logger != null)
				_logger.Debug(message);

		}

		public static void SendExceptionEmail(Exception exmail, string processName, int departmentId = 0, string userName = "")
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
				Sub = "Exception occurred" + " " + "in [" + processName + "]";

				string errortomail = EmailHead + "<b>Log Written Date: </b>" + " " + DateTime.Now.ToString() + newline + "<b>Error Line No :</b>" + " " + ErrorlineNo + "\t\n" + " " + newline + "<b>Error Message:</b>" + " " + Errormsg + newline + "<b>Exception Type:</b>" + " " + extype + newline + "<b> Error Details :</b>" + " " + ErrorLocation + newline + "<b> Inner Details :</b>" + " " + InnerException + newline + "<b>System:</b>" + " " + processName + newline + $"<b>DepartmentId: {departmentId}</b>" + newline + $"<b>Username: {userName}</b>" + newline + newline + exmail.StackTrace + newline + newline + EmailSing;

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
			catch { }
		}

		public static void Write(string message)
		{
			ShowConsole();
			Console.WriteLine(message);
		}
	}
}
