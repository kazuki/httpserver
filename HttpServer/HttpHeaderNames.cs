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

using System.Collections.Generic;

namespace Kazuki.Net.HttpServer
{
	public static class HttpHeaderNames
	{
		// General
		public const string CacheControl = "Cache-Control";
		public const string Connection = "Connection";
		public const string Date = "Date";
		public const string Pragma = "Pragma";
		public const string Trailer = "Trailer";
		public const string TransferEncoding = "Transfer-Encoding";
		public const string Upgrade = "Upgrade";
		public const string Via = "Via";
		public const string Warning = "Warning";

		// Request
		public const string Accept = "Accept";
		public const string AcceptCharset = "Accept-Charset";
		public const string AcceptEncoding = "Accept-Encoding";
		public const string AcceptLanguage = "Accept-Language";
		public const string Authorization = "Authorization";
		public const string Expect = "Expect";
		public const string From = "From";
		public const string Host = "Host";
		public const string IfMatch = "If-Match";
		public const string IfModifiedSince = "If-Modified-Since";
		public const string IfNoneMatch = "If-None-Match";
		public const string IfRange = "If-Range";
		public const string IfUnmodifiedSince = "If-Unmodified-Since";
		public const string MaxForwards = "Max-Forwards";
		public const string ProxyAuthorization = "Proxy-Authorization";
		public const string Range = "Range";
		public const string Referer = "Referer";
		public const string TE = "TE";
		public const string UserAgent = "User-Agent";

		// Response
		public const string AcceptRanges = "Accept-Ranges";
		public const string Age = "Age";
		public const string ETag = "ETag";
		public const string Location = "Location";
		public const string ProxyAuthenticate = "Proxy-Authenticate";
		public const string RetryAfter = "Retry-After";
		public const string Server = "Server";
		public const string Vary = "Vary";
		public const string WWWAuthenticate = "WWW-Authenticate";

		// Entity
		public const string Allow = "Allow";
		public const string ContentEncoding = "Content-Encoding";
		public const string ContentLanguage = "Content-Language";
		public const string ContentLength = "Content-Length";
		public const string ContentLocation = "Content-Location";
		public const string ContentMD5 = "Content-MD5";
		public const string ContentRange = "Content-Range";
		public const string ContentType = "Content-Type";
		public const string Expires = "Expires";
		public const string LastModified = "Last-Modified";

		// Cookie
		public const string Cookie = "Cookie";
		public const string SetCookie = "Set-Cookie";

		static Dictionary<string, string> _normalizeTable = new Dictionary<string,string> ();
		static HttpHeaderNames ()
		{
			_normalizeTable[CacheControl.ToLower()] = CacheControl;
			_normalizeTable[Connection.ToLower()] = Connection;
			_normalizeTable[Date.ToLower()] = Date;
			_normalizeTable[Pragma.ToLower()] = Pragma;
			_normalizeTable[Trailer.ToLower()] = Trailer;
			_normalizeTable[TransferEncoding.ToLower()] = TransferEncoding;
			_normalizeTable[Upgrade.ToLower()] = Upgrade;
			_normalizeTable[Via.ToLower()] = Via;
			_normalizeTable[Warning.ToLower()] = Warning;
			_normalizeTable[Accept.ToLower()] = Accept;
			_normalizeTable[AcceptCharset.ToLower()] = AcceptCharset;
			_normalizeTable[AcceptEncoding.ToLower()] = AcceptEncoding;
			_normalizeTable[AcceptLanguage.ToLower()] = AcceptLanguage;
			_normalizeTable[Authorization.ToLower()] = Authorization;
			_normalizeTable[Expect.ToLower()] = Expect;
			_normalizeTable[From.ToLower()] = From;
			_normalizeTable[Host.ToLower()] = Host;
			_normalizeTable[IfMatch.ToLower()] = IfMatch;
			_normalizeTable[IfModifiedSince.ToLower()] = IfModifiedSince;
			_normalizeTable[IfNoneMatch.ToLower()] = IfNoneMatch;
			_normalizeTable[IfRange.ToLower()] = IfRange;
			_normalizeTable[IfUnmodifiedSince.ToLower()] = IfUnmodifiedSince;
			_normalizeTable[MaxForwards.ToLower()] = MaxForwards;
			_normalizeTable[ProxyAuthorization.ToLower()] = ProxyAuthorization;
			_normalizeTable[Range.ToLower()] = Range;
			_normalizeTable[Referer.ToLower()] = Referer;
			_normalizeTable[TE.ToLower()] = TE;
			_normalizeTable[UserAgent.ToLower()] = UserAgent;
			_normalizeTable[AcceptRanges.ToLower()] = AcceptRanges;
			_normalizeTable[Age.ToLower()] = Age;
			_normalizeTable[ETag.ToLower()] = ETag;
			_normalizeTable[Location.ToLower()] = Location;
			_normalizeTable[ProxyAuthenticate.ToLower()] = ProxyAuthenticate;
			_normalizeTable[RetryAfter.ToLower()] = RetryAfter;
			_normalizeTable[Server.ToLower()] = Server;
			_normalizeTable[Vary.ToLower()] = Vary;
			_normalizeTable[WWWAuthenticate.ToLower()] = WWWAuthenticate;
			_normalizeTable[Allow.ToLower()] = Allow;
			_normalizeTable[ContentEncoding.ToLower()] = ContentEncoding;
			_normalizeTable[ContentLanguage.ToLower()] = ContentLanguage;
			_normalizeTable[ContentLength.ToLower()] = ContentLength;
			_normalizeTable[ContentLocation.ToLower()] = ContentLocation;
			_normalizeTable[ContentMD5.ToLower()] = ContentMD5;
			_normalizeTable[ContentRange.ToLower()] = ContentRange;
			_normalizeTable[ContentType.ToLower()] = ContentType;
			_normalizeTable[Expires.ToLower()] = Expires;
			_normalizeTable[LastModified.ToLower()] = LastModified;
			_normalizeTable[Cookie.ToLower()] = Cookie;
			_normalizeTable[SetCookie.ToLower ()] = SetCookie;
		}
		public static string NormalizeHeaderName (string name)
		{
			string value;
			if (_normalizeTable.TryGetValue (name.ToLower (), out value))
				return value;
			return name;
		}
	}
}
