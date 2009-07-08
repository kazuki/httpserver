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
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace Kazuki.Net.HttpServer.TemplateEngines
{
	public class XslTemplateEngine
	{
		const string MIME_HTML = "text/html";
		const string MIME_XHTML = "application/xhtml+xml";
		Dictionary<string, XslCache> _cache = new Dictionary<string,XslCache> ();
		ReaderWriterLock _cacheLock = new ReaderWriterLock ();

		public object Render (IHttpServer server, IHttpRequest req, HttpResponseHeader res, XmlDocument doc, string xsl_path)
		{
			XslCache cache;
			_cacheLock.AcquireReaderLock (Timeout.Infinite);
			try {
				if (!_cache.TryGetValue (xsl_path, out cache)) {
					cache = new XslCache (xsl_path);
					LockCookie cookie = _cacheLock.UpgradeToWriterLock (Timeout.Infinite);
					try {
						_cache[xsl_path] = cache;
					} finally {
						_cacheLock.DowngradeFromWriterLock (ref cookie);
					}
				}
			} finally {
				_cacheLock.ReleaseReaderLock ();
			}

			bool enable_xhtml = (req.Headers.ContainsKey (HttpHeaderNames.Accept) && req.Headers[HttpHeaderNames.Accept].Contains (MIME_XHTML));
			if (enable_xhtml) {
				res[HttpHeaderNames.ContentType] = MIME_XHTML + "; charset=utf-8";
			} else {
				res[HttpHeaderNames.ContentType] = MIME_HTML + "; charset=utf-8";
			}
			return cache.Transform (doc, !enable_xhtml);
		}

		class XslCache
		{
			const string NS_XSL = "http://www.w3.org/1999/XSL/Transform";
			const string NS_XHTML = "http://www.w3.org/1999/xhtml";
			const bool INDENT = true;

			string _path;
			DependencyFile[] _deps = null;
			XslCompiledTransform _xsl_html4 = null;
			XslCompiledTransform _xsl_xhtml = null;
			ReaderWriterLock _lock = new ReaderWriterLock ();

			public XslCache (string path)
			{
				_path = path;
			}

			void Check ()
			{
				_lock.AcquireReaderLock (Timeout.Infinite);
				try {
					bool check = (_deps == null);
					if (_deps != null) {
						for (int i = 0; i < _deps.Length; i ++) {
							if (_deps[i].IsUpdate ()) {
								check = true;
								break;
							}
						}
					}

					if (check) {
						LockCookie cookie = _lock.UpgradeToWriterLock (Timeout.Infinite);
						try {
							check = (_deps == null);
							if (_deps != null) {
								for (int i = 0; i < _deps.Length; i++) {
									if (_deps[i].IsUpdate ()) {
										check = true;
										break;
									}
								}
							}
							if (check) {
								List<string> files = new List<string> ();
								Resolver resolver = new Resolver (false, _path, INDENT, files);
								_xsl_xhtml = new XslCompiledTransform ();
								using (XmlTextReader reader = new XmlTextReader (_path)) {
									reader.XmlResolver = resolver;
									_xsl_xhtml.Load (new XPathDocument (reader), XsltSettings.Default, resolver);
								}

								resolver = new Resolver (true, _path, INDENT, null);
								_xsl_html4 = new XslCompiledTransform ();
								using (XmlTextReader reader = new XmlTextReader (_path)) {
									reader.XmlResolver = resolver;
									_xsl_html4.Load (new XPathDocument (reader), XsltSettings.Default, resolver);
								}

								_deps = new DependencyFile[files.Count];
								for (int i = 0; i < files.Count; i ++) {
									_deps[i] = new DependencyFile ();
									_deps[i].Path = files[i];
									_deps[i].LastWriteTimeUtc = _deps[i].GetLastWriteTime ();
								}
							}
						} finally {
							_lock.DowngradeFromWriterLock (ref cookie);
						}
					}
				} finally {
					_lock.ReleaseReaderLock ();
				}
			}

			public byte[] Transform (XmlDocument doc, bool is_html4)
			{
				Check ();
				XslCompiledTransform xsl = (is_html4 ? _xsl_html4 : _xsl_xhtml);

				byte[] raw;
				using (MemoryStream ms = new MemoryStream ()) {
					_lock.AcquireReaderLock (Timeout.Infinite);
					try {
						xsl.Transform (doc, null, ms);
					} finally {
						_lock.ReleaseReaderLock ();
					}
					ms.Close ();
					raw = ms.ToArray ();
				}
				return raw;
			}

			class Resolver : XmlResolver
			{
				bool _html4;
				string _path;
				List<string> _files;
				bool _indent;

				public Resolver (bool html4, string path, bool indent, List<string> files)
				{
					_html4 = html4;
					_path = path;
					_files = files;
					_indent = indent;
				}

				public override System.Net.ICredentials Credentials {
					set { throw new NotImplementedException (); }
				}

				public override object GetEntity (Uri absoluteUri, string role, Type ofObjectToReturn)
				{
					string local_path = absoluteUri.LocalPath;
					bool root_xsl = local_path.Equals (local_path, StringComparison.CurrentCultureIgnoreCase);

					XmlDocument doc = new XmlDocument ();
					doc.Load (local_path);
					if (_files != null)
						_files.Add (local_path);

					XmlNodeList list = doc.DocumentElement.GetElementsByTagName ("output", NS_XSL);
					for (int i = 0; i < list.Count; i++)
						doc.DocumentElement.RemoveChild (list[i]);

					if (!_html4) {
						if (root_xsl) {
							SetupXSLOutput (doc, "xml", "utf-8", "-//W3C//DTD XHTML 1.1//EN", "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd", _indent);
						}
					} else {
						XmlElement new_root = ConvertXHTMLtoHTML (doc, doc.DocumentElement);
						doc.ReplaceChild (new_root, doc.DocumentElement);
						if (root_xsl) {
							SetupXSLOutput (doc, "html", "utf-8", "-//W3C//DTD HTML 4.01//EN", "http://www.w3.org/TR/html4/strict.dtd", _indent);
						}
					}

					using (MemoryStream ms = new MemoryStream ())
					using (XmlTextWriter writer = new XmlTextWriter (ms, System.Text.Encoding.UTF8)) {
						doc.WriteTo (writer);
						writer.Close ();
						ms.Close ();
						return new MemoryStream (ms.ToArray ());
					}
				}

				void SetupXSLOutput (XmlDocument doc, string method, string encoding, string docpublic, string docsystem, bool indent)
				{
					XmlElement outputSetting = doc.CreateElement ("xsl", "output", NS_XSL);
					outputSetting.SetAttribute ("method", method);
					outputSetting.SetAttribute ("encoding", encoding);
					outputSetting.SetAttribute ("doctype-public", docpublic);
					outputSetting.SetAttribute ("doctype-system", docsystem);
					outputSetting.SetAttribute ("indent", indent ? "yes" : "no");

					XmlNode element = doc.DocumentElement.FirstChild;
					while (element != null && element.LocalName == "import" && element.NamespaceURI == NS_XSL)
						element = element.NextSibling;
					if (element != null)
						doc.DocumentElement.InsertBefore (outputSetting, element);
					else
						doc.DocumentElement.AppendChild (outputSetting);
				}

				XmlElement ConvertXHTMLtoHTML (XmlDocument doc, XmlElement element)
				{
					XmlElement new_element;
					if (element.NamespaceURI == NS_XHTML) {
						new_element = doc.CreateElement (string.Empty, element.LocalName, string.Empty);
					} else {
						new_element = doc.CreateElement (element.Name, element.NamespaceURI);
					}

					foreach (XmlAttribute att in element.Attributes) {
						if ((att.Name == "xmlns" || att.Name.StartsWith ("xmlns:")) && att.Value == NS_XHTML)
							continue;
						if (att.NamespaceURI == NS_XHTML)
							new_element.SetAttribute (att.LocalName, string.Empty, att.Value);
						else
							new_element.SetAttributeNode ((XmlAttribute)att.Clone ());
					}

					foreach (XmlNode child in element.ChildNodes) {
						XmlElement child_element = child as XmlElement;
						if (child_element != null) {
							new_element.AppendChild (ConvertXHTMLtoHTML (doc, child_element));
						} else {
							new_element.AppendChild (child.Clone ());
						}
					}

					return new_element;
				}
			}


			class DependencyFile
			{
				public string Path;
				public DateTime LastWriteTimeUtc;
				public DateTime GetLastWriteTime ()
				{
					return File.GetLastWriteTimeUtc (this.Path);
				}
				public bool IsUpdate ()
				{
					return LastWriteTimeUtc != GetLastWriteTime ();
				}
			}
		}
	}
}
