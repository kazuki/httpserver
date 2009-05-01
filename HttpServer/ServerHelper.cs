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

namespace Kazuki.Net.HttpServer
{
	static class ServerHelper
	{
		public static void ParseQueryString (string query, Dictionary<string, string> dic)
		{
			String[] items = query.Split ('&');
			foreach (String item in items) {
				string[] tmp = item.Split ('=');
				if (tmp.Length == 2)
					dic[tmp[0]] = tmp[1];
				else if (tmp.Length == 1)
					dic[tmp[0]] = "";
			}
		}

		public static HttpMethod ToHttpMethod (string method)
		{
			switch (method.ToUpper ()) {
				case "GET": return HttpMethod.GET;
				case "POST": return HttpMethod.POST;
				case "HEAD": return HttpMethod.HEAD;
				default: return HttpMethod.Unknown;
			}
		}

		public static string GetStatusDescription (HttpStatusCode statusCode)
		{
			switch ((int)statusCode) {
				case 100: return "Continue";
				case 101: return "Switching Protocols";
				case 200: return "OK";
				case 201: return "Created";
				case 202: return "Accepted";
				case 203: return "Non-Authoritative Information";
				case 204: return "No Content";
				case 205: return "Reset Content";
				case 206: return "Partial Content";
				case 300: return "Multiple Choices";
				case 301: return "Moved Permanently";
				case 302: return "Found";
				case 303: return "See Other";
				case 304: return "Not Modified";
				case 305: return "Use Proxy";
				case 306: return "(Unused)";
				case 307: return "Temporary Redirect";
				case 400: return "Bad Request";
				case 401: return "Unauthorized";
				case 402: return "Payment Required";
				case 403: return "Forbidden";
				case 404: return "Not Found";
				case 405: return "Method Not Allowed";
				case 406: return "Not Acceptable";
				case 407: return "Proxy Authentication Required";
				case 408: return "Request Timeout";
				case 409: return "Conflict";
				case 410: return "Gone";
				case 411: return "Length Required";
				case 412: return "Precondition Failed";
				case 413: return "Request Entity Too Large";
				case 414: return "Request-URI Too Long";
				case 415: return "Unsupported Media Type";
				case 416: return "Requested Range Not Satisfiable";
				case 417: return "Expectation Failed";
				case 500: return "Internal Server Error";
				case 501: return "Not Implemented";
				case 502: return "Bad Gateway";
				case 503: return "Service Unavailable";
				case 504: return "Gateway Timeout";
				case 505: return "HTTP Version Not Supported";
				default: return "Unknown";
			}
		}
	}
}
