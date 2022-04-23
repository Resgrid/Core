using System;
using System.IO;
using System.Reflection;

namespace Resgrid.Framework
{
	public static class FileHelper
	{
		public static string GetContentTypeByExtension(string strExtension)
		{
			switch (strExtension)
			{
				case ".pdf":
					return "application/pdf";
				case ".fif":
					return "application/fractals";
				case ".hta":
					return "application/hta";
				case ".hqx":
					return "application/mac-binhex40";
				case ".vsi":
					return "application/ms-vsi";
				case ".p10":
					return "application/pkcs10";
				case ".p7m":
					return "application/pkcs7-mime";
				case ".p7s":
					return "application/pkcs7-signature";
				case ".cer":
					return "application/pkix-cert";
				case ".crl":
					return "application/pkix-crl";
				case ".ps":
					return "application/postscript";
				case ".setpay":
					return "application/set-payment-initiation";
				case ".setreg":
					return "application/set-registration-initiation";
				case ".sst":
					return "application/vnd.ms-pki.certstore";
				case ".pko":
					return "application/vnd.ms-pki.pko";
				case ".cat":
					return "application/vnd.ms-pki.seccat";
				case ".stl":
					return "application/vnd.ms-pki.stl";
				case ".wpl":
					return "application/vnd.ms-wpl";
				case ".xps":
					return "application/vnd.ms-xpsdocument";
				case ".z":
					return "application/x-compress";
				case ".tgz":
					return "application/x-compressed";
				case ".gz":
					return "application/x-gzip";
				case ".ins":
					return "application/x-internet-signup";
				case ".iii":
					return "application/x-iphone";
				case ".jtx":
					return "application/x-jtx+xps";
				case ".latex":
					return "application/x-latex";
				case ".nix":
					return "application/x-mix-transfer";
				case ".asx":
					return "application/x-mplayer2";
				case ".application":
					return "application/x-ms-application";
				case ".wmd":
					return "application/x-ms-wmd";
				case ".wmz":
					return "application/x-ms-wmz";
				case ".xbap":
					return "application/x-ms-xbap";
				case ".p12":
					return "application/x-pkcs12";
				case ".p7b":
					return "application/x-pkcs7-certificates";
				case ".p7r":
					return "application/x-pkcs7-certreqresp";
				case ".sit":
					return "application/x-stuffit";
				case ".tar":
					return "application/x-tar";
				case ".man":
					return "application/x-troff-man";
				case ".zip":
					return "application/x-zip-compressed";
				case ".xaml":
					return "application/xaml+xml";
				case ".xml":
					return "application/xml";
				case ".aiff":
					return "audio/aiff";
				case ".au":
					return "audio/basic";
				case ".mid":
					return "audio/midi";
				case ".mp3":
					return "audio/mp3";
				case ".wav":
					return "audio/wav";
				case ".m3u":
					return "audio/x-mpegurl";
				case ".wax":
					return "audio/x-ms-wax";
				case ".wma":
					return "audio/x-ms-wma";
				case ".bmp":
					return "image/bmp";
				case ".gif":
					return "image/gif";
				case ".jpg":
					return "image/jpeg";
				case ".jpeg":
					return "image/jpeg";
				case ".png":
					return "image/png";
				case ".tiff":
					return "image/tiff";
				case ".ico":
					return "image/x-icon";
				case ".dwfx":
					return "model/vnd.dwfx+xps";
				case ".css":
					return "text/css";
				case ".323":
					return "text/h323";
				case ".htm":
					return "text/html";
				case ".uls":
					return "text/iuls";
				case ".txt":
					return "text/plain";
				case ".wsc":
					return "text/scriptlet";
				case ".htt":
					return "text/webviewhtml";
				case ".htc":
					return "text/x-component";
				case ".vcf":
					return "text/x-vcard";
				case ".avi":
					return "video/avi";
				case ".mpeg":
					return "video/mpeg";
				case ".wm":
					return "video/x-ms-wm";
				case ".wmv":
					return "video/x-ms-wmv";
				case ".wmx":
					return "video/x-ms-wmx";
				case ".wvx":
					return "video/x-ms-wvx";
			}

			return "";
		}

		public static byte[] ExtractResource(Type assemblyType, string filename)
		{
			Assembly a = Assembly.GetAssembly(assemblyType);
			if (a != null)
			{
				using (Stream resFilestream = a.GetManifestResourceStream(filename))
				{
					if (resFilestream == null) return null;
					byte[] ba = new byte[resFilestream.Length];
					resFilestream.Read(ba, 0, ba.Length);
					return ba;
				}
			}

			return null;
		}
	}
}
