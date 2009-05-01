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

using System.IO;
using System.IO.Compression;

namespace Kazuki.Net.HttpServer
{
	public class CompressMiddleware : IHttpApplication
	{
		IHttpApplication _app;
		static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger ();

		public CompressMiddleware (IHttpApplication app)
		{
			_app = app;
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader header)
		{
			string accept_encodings;
			if (!req.Headers.TryGetValue (HttpHeaderNames.AcceptEncoding, out accept_encodings))
				accept_encodings = "";
			bool enableGzip = accept_encodings.Contains ("gzip");
			bool enableDeflate = accept_encodings.Contains ("deflate");
			if (enableDeflate) enableGzip = false;
			if (enableGzip) enableDeflate = false;

			object result = _app.Process (server, req, header);
			if (header.Status != HttpStatusCode.OK || !(enableGzip || enableDeflate) || header.ContainsKey (HttpHeaderNames.ContentEncoding)) {
				return result;
			} else {
				byte[] ret;
				int original_size = -1;
				using (MemoryStream ms = new MemoryStream ())
				using (Stream strm = (enableGzip ? (Stream)new GZipStream (ms, CompressionMode.Compress) : (Stream)new DeflateStream (ms, CompressionMode.Compress))) {
					if (result is string) {
						byte[] raw = header.Encoding.GetBytes ((string)result);
						original_size = raw.Length;
						strm.Write (raw, 0, raw.Length);
					} else if (result is byte[]) {
						byte[] raw = (byte[])result;
						original_size = raw.Length;
						strm.Write (raw, 0, raw.Length);
					} else {
						return result;
					}
					strm.Flush ();
					strm.Close ();
					ms.Close ();
					ret = ms.ToArray ();
				}

				if (ret.Length >= original_size) {
					_logger.Trace ("Bypass compress middleware ({0} is larger than {1})", ret.Length, original_size);
					return result;
				}
				
				header[HttpHeaderNames.ContentLength] = ret.Length.ToString ();
				header[HttpHeaderNames.ContentEncoding] = enableGzip ? "gzip" : "deflate";
				_logger.Trace ("Enable {0} compression, size is {1} to {2}",
					header[HttpHeaderNames.ContentEncoding], original_size, ret.Length);

				return ret;
			}
		}
	}
}
