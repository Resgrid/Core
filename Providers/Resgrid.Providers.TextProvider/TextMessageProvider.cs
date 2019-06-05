using System;
using System.Net;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.NumberProvider.Models;
using RestSharp;
using RestSharp.Authenticators;
using RestSharp.Extensions.MonoHttp;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Resgrid.Providers.NumberProvider
{
	public class TextMessageProvider : ITextMessageProvider
	{
		public void SendTextMessage(string number, string message, string departmentNumber, MobileCarriers carrier, bool forceGateway = false)
		{
			if (carrier == MobileCarriers.Telstra)
				SendTextMessageViaNexmo(number, message, departmentNumber);
			else if (Carriers.OnPremSmsGatewayCarriers.Contains(carrier) || forceGateway)
			{
				if (!SendTextMessageViaDiafaan(number, message))
				{
					SendTextMessageViaSignalWire(number, message, departmentNumber);
				}
			}
			else if (Config.SystemBehaviorConfig.SmsProviderType == Config.SmsProviderTypes.SignalWire)
			{
				if (!SendTextMessageViaSignalWire(number, message, departmentNumber))
				{
					SendTextMessageViaTwillio(number, message, departmentNumber);
				}
			}
			else
			{
				if (!SendTextMessageViaTwillio(number, message, departmentNumber))
				{
					SendTextMessageViaSignalWire(number, message, departmentNumber);
				}
			}
		}

		private void SendTextMessageViaNexmo(string number, string message, string departmentNumber)
		{
			var client = new RestClient(Config.NumberProviderConfig.BaseNexmoUrl);
			var request = new RestRequest(GenerateSendTextMessageUrl(number, message, departmentNumber), Method.GET);

			var response = client.Execute(request);

			if (response.ResponseStatus == ResponseStatus.Completed)
			{
				if (response.Content.Contains("rejected"))
				{
					// Error
				}
			}
		}

		private bool SendTextMessageViaTwillio(string number, string message, string departmentNumber)
		{
			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);
			MessageResource messageResource;
			try
			{
				if (String.IsNullOrWhiteSpace(departmentNumber) || NumberHelper.IsNexmoNumber(departmentNumber))
				{
					//textMessage = twilio.SendMessage(Settings.Default.TwilioResgridNumber, number, message);

					messageResource = MessageResource.Create(
						from: new PhoneNumber(Config.NumberProviderConfig.TwilioResgridNumber),
						to: new PhoneNumber(number),
						body: message);

					if (messageResource != null)
						return true;
					else
						return false;
				}
				else
				{
					//textMessage = twilio.SendMessage(departmentNumber, number, message);

					messageResource = MessageResource.Create(
						from: new PhoneNumber(departmentNumber),
						to: new PhoneNumber(number),
						body: message);

					if (messageResource != null)
						return true;
					else
						return false;
				}
			}
			catch (Exception ex)
			{
				return false;
			}

		}

		private bool SendTextMessageViaSignalWire(string number, string message, string departmentNumber)
		{
			try
			{
				var client = new RestClient(Config.NumberProviderConfig.SignalWireApiUrl);
				client.Authenticator = new HttpBasicAuthenticator(Config.NumberProviderConfig.SignalWireAccountSid, Config.NumberProviderConfig.SignalWireApiKey);
				var request = new RestRequest(GenerateSendTextMessageUrlForSignalWire(), Method.POST);

				if (!number.StartsWith("+"))
				{
					if (number.Length >= 11)
						number = $"+{number}";
					else
					{
						if (number.Length == 10)
						{
							number = $"+1{number}";
						}
					}

				}

				request.AddJsonBody(new
				{
					From = Config.NumberProviderConfig.SignalWireResgridNumber,
					To = number,
					Body = message
				});

				var response = client.Execute<SignalWireMessageResponse>(request);

				if (response.ResponseStatus == ResponseStatus.Completed)
				{
					if (response.StatusCode == HttpStatusCode.Created && response.Data.error_code == null)
						return true;
					else
						return false;
				}
				else
				{
					return false;
				}
			}
			catch
			{
				return false;
			}
		}

		public bool SendTextMessageViaDiafaan(string number, string message)
		{
			var client = new RestClient(Config.NumberProviderConfig.DiafaanSmsGatewayUrl);
			var request = new RestRequest(GenerateSendTextMessageUrlForDiafaan(number, message), Method.GET);

			var response = client.Execute(request);

			if (response.ResponseStatus == ResponseStatus.Completed)
			{
				if (response.StatusCode == HttpStatusCode.OK)
					return true;
				else
					return false;
			}
			else
			{
				return false;
			}
		}

		private static string GenerateSendTextMessageUrl(string number, string message, string departmentNumber)
		{
			if (!string.IsNullOrWhiteSpace(departmentNumber))
				return string.Format("sms/json?api_key={0}&api_secret={1}&from={2}&to={3}&text={4}", Config.NumberProviderConfig.NexmoApiKey, Config.NumberProviderConfig.NexmoApiSecret, departmentNumber, number, HttpUtility.UrlEncode(message));
			else
				return string.Format("sms/json?api_key={0}&api_secret={1}&from={2}&to={3}&text={4}", Config.NumberProviderConfig.NexmoApiKey, Config.NumberProviderConfig.NexmoApiSecret, "12132633666", number, HttpUtility.UrlEncode(message));
		}

		private static string GenerateSendTextMessageUrlForDiafaan(string number, string message)
		{
			return $"http/send-message/?username={HttpUtility.UrlEncode(Config.NumberProviderConfig.DiafaanSmsGatewayUserName)}&password={HttpUtility.UrlEncode(Config.NumberProviderConfig.DiafaanSmsGatewayPassword)}&to={HttpUtility.UrlEncode(number)}&message={HttpUtility.UrlEncode(message)}";
		}

		private static string GenerateSendTextMessageUrlForSignalWire()
		{
			return $"/api/laml/2010-04-01/Accounts/{Resgrid.Config.NumberProviderConfig.SignalWireAccountSid}/Messages.json";
		}
	}
}
