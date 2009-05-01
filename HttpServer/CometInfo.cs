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
using System.Threading;

namespace Kazuki.Net.HttpServer
{
	public class CometInfo
	{
		WaitHandle _waitHandle;
		object _connection, _ctx;
		IHttpRequest _req;
		HttpResponseHeader _res;
		DateTime _timeout;
		bool _isTimeout = false;
		CometHandler _handler;

		public CometInfo (WaitHandle waitHandle, IHttpRequest req, HttpResponseHeader res, object ctx, DateTime timeout, CometHandler handler)
		{
			_waitHandle = waitHandle;
			_connection = null;
			_req = req;
			_res = res;
			_ctx = ctx;
			_timeout = timeout;
			_handler = handler;
		}

		public WaitHandle WaitHandle {
			get { return _waitHandle; }
		}

		public object Connection {
			get { return _connection; }
			set { _connection = value;}
		}

		public IHttpRequest Request {
			get { return _req; }
		}

		public HttpResponseHeader Response {
			get { return _res; }
		}

		public object Context {
			get { return _ctx; }
		}

		public DateTime Timeout {
			get { return _timeout; }
		}

		public bool IsTimeout {
			get { return _isTimeout; }
		}

		public bool CheckTimeout (DateTime now)
		{
			_isTimeout = (_timeout <= now);
			return _isTimeout;
		}

		public CometHandler Handler {
			get { return _handler; }
		}
	}
}
