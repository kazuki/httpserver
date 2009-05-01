/*
 * Copyright (C) 2009 Kazuki Oikawa
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Kazuki.Net.HttpServer.Embed
{
	class HttpRequest : IHttpRequest
	{
		static char[] RequestLineSplitter = { ' ' };
		static char[] HeaderLineSplitter = { ':' };

		HttpMethod _method;
		HttpVersion _ver;
		Uri _uri;
		IPEndPoint _remoteEP;
		Dictionary<string, string> _headers;
		Dictionary<string, string> _queries;
		Dictionary<string, object> _formdata;
		Dictionary<string, string> _cookies = new Dictionary<string,string> ();
		DateTime _ifModifiedSince = DateTime.MinValue;
		HttpConnection _client;

		private HttpRequest () { }
		public static HttpRequest Create (HttpConnection client)
		{
			HttpRequest req = new HttpRequest ();
			StringBuilder sb = new StringBuilder ();
			req._client = client;
			req._remoteEP = (IPEndPoint)client.RawSocket.RemoteEndPoint;
			string line = ReadLine (sb, client);
			if (line == null) return null;
			string[] items = line.Split (RequestLineSplitter, 3);
			if (items.Length != 3 || items[2].Length != 8 || !items[2].StartsWith ("HTTP/")
				|| !char.IsDigit (items[2][5]) || !char.IsDigit (items[2][7])) return null;
			req._method = ServerHelper.ToHttpMethod (items[0]);
			string raw_path = items[1];
			req._ver = (HttpVersion)((items[2][5] - '0') * 10 + (items[2][7] - '0'));
			req._headers = new Dictionary<string, string> ();
			req._queries = new Dictionary<string, string> ();
			req._formdata = new Dictionary<string, object> ();

			while (true) {
				line = ReadLine (sb, client);
				if (line == null) return null;
				if (line.Length == 0) break;
				int pos = line.IndexOf (':');
				if (pos <= 0) return null;
				string name = line.Substring (0, pos).TrimEnd ();
				string value = line.Substring (pos + 1).TrimStart ();
				req._headers.Add (HttpHeaderNames.NormalizeHeaderName (name), value);
			}

			string host;
			if (!req._headers.TryGetValue ("Host", out host))
				host = "localhost";
			req._uri = new Uri ("http://" + host + raw_path);
			if (req._uri.Query.Length > 0) {
				string query = req._uri.Query.Substring (1);
				int pos = query.IndexOf ("'#'");
				if (pos >= 0)
					query = query.Substring (0, pos);
				ServerHelper.ParseQueryString (query, req._queries);
			}

			if (req._headers.ContainsKey (HttpHeaderNames.Cookie)) {
				string[] cookies = req._headers[HttpHeaderNames.Cookie].Split (';');
				for (int i = 0; i < cookies.Length; i ++) {
					items = cookies[i].Split (new char[]{'='}, 2);
					if (items.Length == 2) {
						req._cookies.Add (items[0].Trim(), items[1].Trim());
					}
				}
			}

			return req;
		}

		private static string ReadLine (StringBuilder sb, HttpConnection client)
		{
			sb.Length = 0;
			while (client.RawSocket.Connected) {
				int c = client.ReceiveByte ();
				if (c < 0)
					return null;
				if (c == 13) {
					if (client.ReceiveByte () == 10) {
						return sb.ToString ();
					} else {
						return null;
					}
				} else if (c == 10) {
					return null;
				}
				sb.Append ((char)c);
			}
			return null;
		}

		#region IHttpRequest Members

		public HttpMethod HttpMethod {
			get { return _method; }
		}

		public HttpVersion HttpVersion {
			get { return _ver; }
		}

		public Uri Url {
			get { return _uri; }
		}

		public IPEndPoint RemoteEndPoint {
			get { return _remoteEP; }
		}

		public Dictionary<string, string> Headers {
			get { return _headers; }
		}

		public Dictionary<string, string> QueryData {
			get { return _queries; }
		}

		public Dictionary<string, string> Cookies {
			get { return _cookies; }
		}

		#endregion
	}
}
