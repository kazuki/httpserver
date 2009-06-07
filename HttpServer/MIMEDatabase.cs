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
	public static class MIMEDatabase
	{
		static Dictionary<string, string> _mapping = new Dictionary<string,string> ();
		const string UnknownType = "application/octet-stream";

		static MIMEDatabase ()
		{
			_mapping["txt"] = "text/plain";
			_mapping["js"] = "text/javascript";
			_mapping["css"] = "text/css";
			_mapping["xml"] = "text/xml";
			_mapping["html"] = "text/html";

			_mapping["png"] = "image/png";
			_mapping["jpg"] = _mapping["jpeg"] = "image/jpeg";
			_mapping["gif"] = "image/gif";
		}

		public static void RegisterMIME (string ext, string mime)
		{
			if (ext == null || mime == null)
				throw new ArgumentNullException ();
			if (ext.Length > 0 && ext[0] == '.') ext = ext.Substring (1);
			if (ext.Length == 0 || !mime.Contains ("/"))
				throw new ArgumentException ();
			_mapping[ext] = mime;
		}

		public static string GetMIMEType (string ext)
		{
			if (ext == null || ext.Length == 0)
				return UnknownType;
			if (ext[0] == '.')
				return GetMIMEType (ext.Substring (1));
			string type;
			if (_mapping.TryGetValue (ext, out type))
				return type;
			return UnknownType;
		}
	}
}
