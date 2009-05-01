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

namespace Kazuki.Net.HttpServer
{
	public interface IHttpRequest
	{
		HttpMethod HttpMethod { get; }
		HttpVersion HttpVersion { get; }
		Uri Url { get; }

		IPEndPoint RemoteEndPoint { get; }

		Dictionary<string, string> Headers { get; }
		Dictionary<string, string> QueryData { get; }
		Dictionary<string, string> Cookies { get; }
	}
}
