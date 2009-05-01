using System;
using System.Text;

namespace Kazuki.Net.HttpServer.Test
{
	class Program
	{
		static void Main (string[] args)
		{
			IHttpApplication app;
			app = new CompressMiddleware (new App ());
			//app = new App ();

			using (IHttpServer server = HttpServer.CreateEmbedHttpServer (app, null, true, false, true, 8080, 128)) {
				Console.ReadLine ();
			}
		}
	}

	class App : IHttpApplication
	{
		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader header)
		{
			header[HttpHeaderNames.ContentType] = "text/plain; charset=UTF-8";
			header[HttpHeaderNames.Date] = DateTime.UtcNow.ToString ("R");

			byte[] raw = Encoding.UTF8.GetBytes ("Hello World");
			header[HttpHeaderNames.ContentLength] = raw.Length.ToString ();
			return raw;
		}
	}

	class Middle : IHttpApplication
	{
		IHttpApplication _app;

		public Middle (IHttpApplication app)
		{
			_app = app;
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader header)
		{
			object o = _app.Process (server, req, header);
			if (o is string) {
				string[] lines = ((string)o).Split (new string[] {"\r\n"}, StringSplitOptions.None);
				for (int i = 0; i < lines.Length; i ++)
					lines[i] = "*" + lines[i];
				return string.Join ("\r\n", lines);
			} else
				return "";
		}
	}
}
