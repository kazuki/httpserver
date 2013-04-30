/*
 * Copyright (C) 2013 Kazuki Oikawa
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.	 See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.	 If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Text;
using System.Threading;
using System.Security.Cryptography;

namespace Kazuki.Net.HttpServer
{
	public class WebSocketInfo
	{
		public WebSocketInfo(IHttpRequest req, HttpResponseHeader res, EventHandler<WebSocketEventArgs> handler, object state)
		{
			this.Request = req;
			this.Handler = handler;
			this.State = state;

			string ws_ver = req.Headers["Sec-WebSocket-Version"];
			string ws_origin = req.Headers["Origin"];
			string ws_key = req.Headers["Sec-WebSocket-Key"];

			byte[] ws_key_raw = Encoding.ASCII.GetBytes (ws_key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
			string ws_hash;

			using (SHA1Managed sha1 = new SHA1Managed()) {
				ws_hash = Convert.ToBase64String (sha1.ComputeHash(ws_key_raw));
			}

			res.Status = HttpStatusCode.SwitchingProtocols;
			res["Upgrade"] = "websocket";
			res["Connection"] = "Upgrade";
			res["Sec-WebSocket-Accept"] = ws_hash;
		}

		public IHttpRequest Request { get; private set; }
		public EventHandler<WebSocketEventArgs> Handler { get; set; }
		public object State { get; set; }
		public object Connection { get; set; }
		internal Action<byte[], int, int> SendInternal { get; set; }

		public void SendPong()
		{
			byte[] msg = new byte[2];
			msg[0] = (byte)(0x80/*FIN*/ | 0xa);
			msg[1] = 0;
			SendInternal(msg, 0, 2);
		}

		public void Send(string data, Encoding encoding)
		{
			byte[] raw = encoding.GetBytes (data);
			Send (raw, 0, raw.Length, true);
		}

		public void Send(byte[] data, int offset, int size, bool is_text)
		{
			if (size > ushort.MaxValue)
				throw new NotImplementedException();

			byte[] msg = new byte[4 + size];
			msg[0] = (byte)(0x80/*FIN*/ | (is_text ? 0x1 : 0x2));
			int header_size = 2;
			if (size < 126) {
				msg[1] = (byte)size;
			} else {
				msg[1] = 126;
				msg[2] = (byte)(size >> 8);
				msg[3] = (byte)(size & 0xff);
				header_size = 4;
			}
			Buffer.BlockCopy(data, offset, msg, header_size, size);
			SendInternal(msg, 0, header_size + size);
		}

		public void Close()
		{
			// TODO
		}
	}
}
