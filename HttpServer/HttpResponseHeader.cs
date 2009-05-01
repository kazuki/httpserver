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

namespace Kazuki.Net.HttpServer
{
	public class HttpResponseHeader : Dictionary<string, string>
	{
		Encoding _encoding = Encoding.UTF8;
		HttpStatusCode _status = HttpStatusCode.OK;
		List<Cookie> _cookies = new List<Cookie> ();

		public HttpResponseHeader (IHttpRequest req)
		{
			string reqConnection;
			bool setDefault = !req.Headers.TryGetValue (HttpHeaderNames.Connection, out reqConnection);
			if (setDefault) {
				if (req.HttpVersion == HttpVersion.Http10)
					reqConnection = "close";
				else
					reqConnection = "Keep-Alive";
			}
			this[HttpHeaderNames.Connection] = reqConnection;
			this[HttpHeaderNames.Date] = DateTime.Now.ToString ("r");
		}

		public byte[] CreateResponseHeaderBytes ()
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append ("HTTP/1.1 ");
			sb.Append ((int)_status);
			sb.Append (" ");
			sb.Append (_status);
			sb.Append ("\r\n");
			foreach (KeyValuePair<string, string> pair in this) {
				if (pair.Value == null || pair.Value.Length == 0)
					continue;
				sb.Append (pair.Key);
				sb.Append (": ");
				sb.Append (pair.Value);
				sb.Append ("\r\n");
			}
			for (int i = 0; i < _cookies.Count; i ++) {
				sb.Append (HttpHeaderNames.SetCookie);
				sb.Append (": ");
				sb.Append (_cookies[i].ToString ());
				if (_cookies[i].Path != null && _cookies[i].Path.Length > 0) {
					sb.Append ("; path=");
					sb.Append (_cookies[i].Path);
				}
				sb.Append ("\r\n");
			}
			sb.Append ("\r\n");
			return Encoding.ASCII.GetBytes (sb.ToString ());
		}

		public string GetNotNullValue (string key)
		{
			string value;
			if (TryGetValue (key, out value) && value != null)
				return value;
			return string.Empty;
		}

		public Encoding Encoding {
			get { return _encoding; }
		}

		public HttpStatusCode Status {
			get { return _status; }
			set { _status = value;}
		}

		public List<Cookie> Cookies {
			get { return _cookies; }
		}
	}
}
