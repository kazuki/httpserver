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
using System.Net;
using System.Net.Sockets;

namespace Kazuki.Net.HttpServer.Embed
{
	class HttpConnection
	{
		public static int ReceiveBuffer = 1024;
		Socket _sock;
		DateTime _startTime;
		byte[] _recvBuffer = new byte[ReceiveBuffer];
		int _recvOffset = 0, _recvFilled = 0;
		IPEndPoint _ep;

		public HttpConnection (Socket sock)
		{
			_sock = sock;
			_startTime = DateTime.Now;
			_ep = (IPEndPoint)sock.RemoteEndPoint;
		}

		public void Close ()
		{
			try {
				_sock.Shutdown (SocketShutdown.Both);
				_sock.Close ();
			} catch {}
		}

		#region Socket Methods
		public int ReceiveByte ()
		{
			if (!FillReceiveBuffer ())
				return -1;
			return _recvBuffer[_recvOffset++];
		}
		public int ReceiveBytes (byte[] buffer, int offset, int size)
		{
			int received = 0;
			while (received < size) {
				if (!FillReceiveBuffer ())
					break;
				int copy_size = Math.Min (_recvFilled - _recvOffset, size - received);
				Buffer.BlockCopy (_recvBuffer, _recvOffset, buffer, offset + received, copy_size);
				_recvOffset += copy_size;
				received += copy_size;
			}
			return received;
		}
		bool FillReceiveBuffer ()
		{
			if (_recvOffset >= _recvFilled) {
				if (!_sock.Poll (-1, SelectMode.SelectRead))
					return false;
				_recvFilled = _sock.Receive (_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None);
				_recvOffset = 0;
				if (_recvFilled <= 0)
					return false;
			}
			return true;
		}
		public void Send (byte[] raw)
		{
			Send (raw, 0, raw.Length);
		}
		public void Send (byte[] raw, int offset, int size)
		{
			int sent = 0;
			while (sent < size) {
				if (!_sock.Poll (-1, SelectMode.SelectWrite))
					throw new SocketException ();
				int ret = _sock.Send (raw, offset + sent, size - sent, SocketFlags.None);
				if (ret <= 0)
					throw new SocketException ();
				sent += ret;
			}
		}
		#endregion

		#region Properties
		public Socket RawSocket {
			get { return _sock; }
		}

		public DateTime StartTime {
			get { return _startTime; }
		}

		public IPEndPoint RemoteEndPoint {
			get { return _ep; }
		}
		#endregion
	}
}
