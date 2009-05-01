﻿/*
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
using System.Text;

namespace Kazuki.Net.HttpServer
{
	public static class HttpUtility
	{
		public static string UrlEncode (string str, Encoding e)
		{
			StringBuilder sb = new StringBuilder ();
			byte[] raw = e.GetBytes (str);
			for (int i = 0; i < raw.Length; i++) {
				if ((raw[i] >= 0x30 && raw[i] <= 0x39) || // 0-9
					(raw[i] >= 0x41 && raw[i] <= 0x5a) || // A-Z
					(raw[i] >= 0x61 && raw[i] <= 0x7a) || // a-z
					(raw[i] == 0x2d) || // -
					(raw[i] == 0x2e) || // .
					(raw[i] == 0x5f) || // _
					(raw[i] == 0x7e)) // ~
				{
					sb.Append ((char)raw[i]);
				} else {
					sb.Append ('%');
					sb.Append (raw[i].ToString ("X2"));
				}
			}
			return sb.ToString ();
		}

		public static string UrlDecode (string str, Encoding e)
		{
			// 1st pass
			int len = 0;
			for (int i = 0; i < str.Length; i++, len++) {
				if (str[i] == '%')
					i += 2;
			}

			// 2nd pass
			byte[] raw = new byte[len];
			for (int i = 0, q = 0; i < str.Length; i++, q ++) {
				if (str[i] == '%') {
					raw[q] = FromHex (str[i + 1], str[i + 2]);
					i += 2;
				} else {
					raw[q] = (byte)str[i];
				}
			}

			return e.GetString (raw);
		}

		static byte FromHex (char high, char low)
		{
			return (byte)((Uri.FromHex (high) << 4) | Uri.FromHex (low));
		}
	}
}
