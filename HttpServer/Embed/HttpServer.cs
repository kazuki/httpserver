/*
 * Copyright (C) 2009,2013 Kazuki Oikawa
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

		Thread _wsThread;
		Dictionary<Socket, WebSocketInfo> _wsDict = new Dictionary<Socket, WebSocketInfo>();

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

			_wsThread = new Thread(WebSocketThread);
			_wsThread.Start();

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
					WebSocketInfo socketInfo;
					StartApplication (req, client, DefaultEncoding, out keepAlive, out cometInfo, out socketInfo);
					if (cometInfo == null && socketInfo == null) {
						TerminateApplication (client, keepAlive);
					} else {
						if (cometInfo != null) {
							cometInfo.Connection = client;
							AddCometInfo(cometInfo);
						} else if (socketInfo != null) {
							socketInfo.Connection = client;
							socketInfo.SendInternal = delegate(byte[] msg, int offset, int size) {
								client.RawSocket.Send(msg, offset, size, SocketFlags.None);
							};
							AddSocketInfo(socketInfo);
						}
					}
				} catch (Exception e) {
					_logger.WarnException ("Unhandled Exception " + client.RemoteEndPoint.ToString (), e);
					client.Close ();
				}
			} catch {
			}
		}

		void StartApplication (IHttpRequest req, HttpConnection conn, Encoding encoding, out bool keepAlive, out CometInfo cometInfo, out WebSocketInfo socketInfo)
		{
			bool header_sent = false;
			HttpResponseHeader header = new HttpResponseHeader (req);
			keepAlive = header.GetNotNullValue (HttpHeaderNames.Connection).ToLower ().Equals ("keep-alive");
			cometInfo = null;
			socketInfo = null;

			try {
				object result = _app.Process (this, req, header);
				cometInfo = result as CometInfo;
				socketInfo = result as WebSocketInfo;
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
			bool webSocketMode = false;
			if (result is Stream) {
				try {
					header[HttpHeaderNames.ContentLength] = (result as Stream).Length.ToString ();
				} catch { }
			} else if (result is WebSocketInfo) {
				webSocketMode = true;
				keepAlive = false;
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
			if (req.HttpMethod == HttpMethod.HEAD || webSocketMode)
				return;
			if (rawBody != null) {
				conn.Send (rawBody);
			} else if (result is Stream) {
				conn.Send(result as Stream);
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

		void AddSocketInfo(WebSocketInfo info)
		{
			lock (_wsDict) {
				_wsDict.Add(((HttpConnection)info.Connection).RawSocket, info);
			}
		}

		void WebSocketThread()
		{
			List<Socket> readList = new List<Socket>();
			List<Socket> errList = new List<Socket>();
			List<WebSocketInfo> closeList = new List<WebSocketInfo> ();

			byte[] basic_header = new byte[2];
			byte[] ext_payload_header = new byte[8];
			byte[] ext_mask_header = new byte[4];
			byte[] payload = new byte[1024 * 1024];

			while (!_closed) {
				readList.Clear(); errList.Clear();

				lock (_wsDict) {
					foreach (WebSocketInfo wsi in _wsDict.Values) {
						HttpConnection c = (HttpConnection)wsi.Connection;
						readList.Add(c.RawSocket);
						errList.Add(c.RawSocket);
					}
				}

				if (readList.Count != 0 || errList.Count != 0) {
					Socket.Select(readList, null, errList, 1000);
				} else {
					Thread.Sleep(1);
				}

				foreach (Socket sock in readList) {
					WebSocketInfo wi;
					lock (_wsDict) {
						if (!_wsDict.TryGetValue(sock, out wi))
							continue;
					}
					HttpConnection c = (HttpConnection)wi.Connection;

					// read header
					if (c.ReceiveBytes(basic_header, 0, 2) != 2)
						goto OnError;
					bool mask = (basic_header[1] >= 0x80);
					ulong payload_len = (ulong)(basic_header[1] & 0x7f);
					if (payload_len == 126 || payload_len == 127) {
						int ext_payload_header_len = (payload_len == 126 ? 2 : 8);
						if (c.ReceiveBytes(ext_payload_header, 0, ext_payload_header_len) != ext_payload_header_len)
							goto OnError;
						if (ext_payload_header_len == 2) {
							payload_len = (((ulong)ext_payload_header[0]) << 8) | (ulong)ext_payload_header[1];
						} else {
							payload_len = (((ulong)ext_payload_header[0]) << 58)
								| (((ulong)ext_payload_header[1]) << 48)
								| (((ulong)ext_payload_header[2]) << 40)
								| (((ulong)ext_payload_header[3]) << 32)
								| (((ulong)ext_payload_header[4]) << 24)
								| (((ulong)ext_payload_header[5]) << 16)
								| (((ulong)ext_payload_header[6]) << 8)
								| (((ulong)ext_payload_header[7]) << 0);
						}
						if (payload_len > (ulong)int.MaxValue)
							goto OnError; // support 0-2GB
						if ((ulong)payload.Length < payload_len)
							payload = new byte[(ulong)Math.Pow(2, Math.Ceiling(Math.Log((double)payload_len, 2)))];
					}
					if (mask) {
						if (c.ReceiveBytes(ext_mask_header, 0, 4) != 4)
							goto OnError;
					}
					if (c.ReceiveBytes(payload, 0, (int)payload_len) != (int)payload_len)
						goto OnError;
					if (mask) {
						for (int i = 0; i < (int)payload_len; ++i) {
							payload[i] ^= ext_mask_header[i & 3];
						}
					}

					int opcode = basic_header[0] & 0xf;
					if (opcode == 0x9) {
						Console.WriteLine("recv ping");
						wi.SendPong();
						continue;
					}

					if (wi.Handler != null) {
						try {
							wi.Handler (this, new WebSocketEventArgs { Info = wi, Payload = payload, PayloadSize = (long)payload_len,
								IsBinaryFrame = (opcode == 0x2), IsTextFrame = (opcode == 0x1), IsClose = (opcode == 0x8)});
						} catch (Exception ex) {
							Console.WriteLine(ex.ToString());
						}
					}

					continue;
				OnError:
					closeList.Add(wi);
				}

				if (errList.Count > 0 || closeList.Count > 0) {
					lock (_wsDict) {
						for (int i = 0; i < errList.Count; ++i) {
							closeList.Add(_wsDict[errList[i]]);
							_wsDict.Remove(errList[i]);
						}
					}
					for (int i = 0; i < closeList.Count; ++i)
						((HttpConnection)closeList[i].Connection).Close();
					closeList.Clear();
				}
			}
		}
	}
}
