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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Kazuki.Net.HttpServer.Embed
{
	class HttpServer : HttpServerBase<HttpConnection>
	{
		const int KeepAliveTimeoutSeconds = 120;

		Socket[] _listeners;

		Thread _keepAliveThread;
		Dictionary<Socket, HttpConnection> _keepAliveWaits = new Dictionary<Socket,HttpConnection> ();
		ManualResetEvent _keepAliveWaitHandle = new ManualResetEvent (false);

		static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger ();
		static Encoding DefaultEncoding = Encoding.UTF8;

		public HttpServer (IHttpApplication app, IHttpApplication errApp, bool enableIPv4, bool enableIPv6, bool bindAny, int port, int backlog) : base (app, errApp)
		{
			List<Socket> listeners = new List<Socket> ();
			AddressFamily[] families = new AddressFamily[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 };
			bool[] enables = new bool[] {enableIPv4 && Socket.SupportsIPv4, enableIPv6 && Socket.OSSupportsIPv6};
			IPAddress[] binds = new IPAddress[] {bindAny ? IPAddress.Any : IPAddress.Loopback, bindAny ? IPAddress.IPv6Any : IPAddress.IPv6Loopback};
			for (int i = 0; i < enables.Length; i++)
				if (enables[i]) {
					IPEndPoint ep = new IPEndPoint (binds[i], port);
					try {
						Socket sock = new Socket (families[i], SocketType.Stream, ProtocolType.Tcp);
						sock.Bind (ep);
						sock.Listen (backlog);
						listeners.Add (sock);
					} catch (Exception e) {
						_logger.Error ("Cannot bind {0}. Exception message is \"{1}\"", ep, e.Message);
					}
				}
			if (listeners.Count == 0)
				throw new Exception ("No binded endpoints");
			_listeners = listeners.ToArray ();
			_keepAliveThread = new Thread (KeepAliveCheckThread);
			_keepAliveThread.Start ();

			StartAcceptThread ();
		}

		protected override HttpConnection[] Accept ()
		{
			List<Socket> list = new List<Socket> (_listeners);
			Socket.Select (list, null, null, 1000000);
			if (_listeners == null)
				return null;
			HttpConnection[] clients = new HttpConnection[list.Count];
			for (int i = 0; i < list.Count; i++) {
				clients[i] = new HttpConnection (list[i].Accept ());
				_logger.Trace ("Accept TCP Connection from {0}", clients[i].RemoteEndPoint);
			}
			return clients;
		}

		void KeepAliveCheckThread ()
		{
			while (!_closed) {
				List<Socket> list;
				_keepAliveWaitHandle.WaitOne ();
				if (_closed)
					return;
				lock (_keepAliveWaits) {
					list = new List<Socket> (_keepAliveWaits.Keys);
				}
				Socket.Select (list, null, null, 10000);
				lock (_keepAliveWaits) {
					for (int i = 0; i < list.Count; i ++) {
						HttpConnection c = _keepAliveWaits [list[i]];
						_keepAliveWaits.Remove (list[i]);
						if (c.RawSocket.Available > 0) {
							_logger.Trace ("Receive new http request, goto working thread from keep-alive wait queue (EP:{0})", c.RemoteEndPoint);
							ThreadPool.QueueUserWorkItem (ProcessClientConnection, c);
						} else {
							_logger.Trace ("Closed (EP:{0})", c.RemoteEndPoint);
							c.Close ();
						}
					}
					if (_keepAliveWaits.Count == 0)
						_keepAliveWaitHandle.Reset ();
				}
			}
		}

		protected override void ProcessClientConnection (HttpConnection client)
		{
			try {
				if (client.RawSocket.Available <= 0) {
					if (!client.RawSocket.Connected || client.StartTime.AddSeconds (KeepAliveTimeoutSeconds) < DateTime.Now) {
						_logger.Trace ("Keep-Alive Timeouts or closed ({0}sec, EP:{1})", KeepAliveTimeoutSeconds, client.RemoteEndPoint);
						client.Close ();
						return;
					} else {
						_logger.Trace ("Goto Keep-alive wait queue (EP:{0})", client.RemoteEndPoint);
						lock (_keepAliveWaits) {
							_keepAliveWaits.Add (client.RawSocket, client);
							_keepAliveWaitHandle.Set ();
						}
						return;
					}
				}
			} catch {
				_logger.Trace ("Catched exception when cheking available (EP: {0})", client.RemoteEndPoint);
				client.Close ();
				return;
			}

			try {
				HttpRequest req = HttpRequest.Create (client);
				if (req == null) {
					client.Close ();
					return;
				}
				_logger.Trace ("Access from {0} to {1}", client.RemoteEndPoint, req.Url);
				try {
					bool keepAlive;
					CometInfo cometInfo;
					StartApplication (req, client, DefaultEncoding, out keepAlive, out cometInfo);
					if (cometInfo == null) {
						TerminateApplication (client, keepAlive);
					} else {
						cometInfo.Connection = client;
						AddCometInfo (cometInfo);
					}
				} catch (Exception e) {
					_logger.WarnException ("Unhandled Exception " + client.RemoteEndPoint.ToString (), e);
					client.Close ();
				}
			} catch {
			}
		}

		void StartApplication (IHttpRequest req, HttpConnection conn, Encoding encoding, out bool keepAlive, out CometInfo cometInfo)
		{
			bool header_sent = false;
			HttpResponseHeader header = new HttpResponseHeader (req);
			keepAlive = header.GetNotNullValue (HttpHeaderNames.Connection).ToLower ().Equals ("keep-alive");
			cometInfo = null;

			try {
				object result = _app.Process (this, req, header);
				cometInfo = result as CometInfo;
				if (cometInfo != null) return;
				ProcessResponse (conn, req, header, result, ref keepAlive, ref header_sent);
			} catch (Exception e) {
				ProcessInternalError (conn, req, header, header_sent, e, ref keepAlive);
			}
		}

		void TerminateApplication (HttpConnection client, bool keepAlive)
		{
			if (keepAlive) {
				_logger.Trace ("Goto Keep-alive wait queue (EP:{0})", client.RemoteEndPoint);
				lock (_keepAliveWaits) {
					_keepAliveWaits.Add (client.RawSocket, client);
					_keepAliveWaitHandle.Set ();
				}
			} else {
				client.Close ();
			}
		}

		void ProcessInternalError (HttpConnection conn, IHttpRequest req, HttpResponseHeader header, bool header_sent, Exception exception, ref bool keepAlive)
		{
			if (!header_sent) {
				object result = new byte[0];
				if (exception is HttpException) {
					header.Status = (exception as HttpException).HttpStatusCode;
				} else {
					header.Status = HttpStatusCode.InternalServerError;
				}

				if (header.Status == HttpStatusCode.NotModified /*|| header.Status == HttpStatusCode.ResetContent(205)*/) {
					header.Remove (HttpHeaderNames.ContentLength); // Not allowed contains response body
				} else {
					if (req.HttpMethod == HttpMethod.HEAD || _errApp == null) {
						header[HttpHeaderNames.ContentLength] = "0";
					} else {
					}
				}

				_logger.Trace ("HTTP {0} {1}", (int)header.Status, ServerHelper.GetStatusDescription (header.Status));
				ProcessResponse (conn, req, header, result, ref keepAlive, ref header_sent);
				//byte[] raw = header.CreateResponseHeaderBytes ();
				//conn.Send (raw);
			} else {
				keepAlive = false; // force disconnect
			}
		}

		protected override void ProcessComet (object cometInfoObj)
		{
			CometInfo cometInfo = cometInfoObj as CometInfo;
			HttpConnection conn = (HttpConnection)cometInfo.Connection;
			bool keepAlive = false, header_sent = false;
			try {
				object result = cometInfo.Handler (cometInfo);
				ProcessResponse (conn, cometInfo.Request, cometInfo.Response, result, ref keepAlive, ref header_sent);
			} catch (Exception e) {
				ProcessInternalError (conn, cometInfo.Request, cometInfo.Response, header_sent, e, ref keepAlive);
			}
			TerminateApplication (conn, keepAlive);
		}

		void ProcessResponse (HttpConnection conn, IHttpRequest req, HttpResponseHeader header, object result, ref bool keepAlive, ref bool header_sent)
		{
			byte[] rawBody = null;
			if (result is Stream) {
				try {
					header[HttpHeaderNames.ContentLength] = (result as Stream).Length.ToString ();
				} catch { }
			} else {
				rawBody = ResponseBodyToBytes (result);
				header[HttpHeaderNames.ContentLength] = rawBody.Length.ToString ();
			}

			if (keepAlive && header.GetNotNullValue (HttpHeaderNames.ContentLength).Length == 0 && !header.GetNotNullValue (HttpHeaderNames.TransferEncoding).ToLower ().Equals ("chunked")) {
				keepAlive = false;
				header[HttpHeaderNames.Connection] = "close";
			} else if (header[HttpHeaderNames.Connection] == "close") {
				keepAlive = false;
			}

			byte[] raw = header.CreateResponseHeaderBytes ();
			header_sent = true;
			conn.Send (raw);
			if (req.HttpMethod == HttpMethod.HEAD)
				return;
			if (rawBody != null) {
				conn.Send (rawBody);
			} else {
				// TODO: impelements to send stream data
				throw new NotImplementedException ();
			}
		}

		byte[] ResponseBodyToBytes (object ret)
		{
			if (ret is string) {
				return DefaultEncoding.GetBytes ((string)ret);
			} else if (ret is byte[]) {
				return (byte[])ret;
			} else {
				throw new ApplicationException ();
			}
		}

		protected override void DisposeInternal ()
		{
			if (_listeners != null) {
				Socket[] tmp = _listeners;
				_listeners = null;
				for (int i = 0; i < tmp.Length; i++)
					tmp[i].Close ();
			}
			_keepAliveWaitHandle.Set ();
			_keepAliveThread.Join ();
			_keepAliveWaitHandle.Close ();
		}
	}
}
