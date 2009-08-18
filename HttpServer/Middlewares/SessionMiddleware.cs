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
using System.Data;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Kazuki.Net.HttpServer.Middlewares
{
	public class SessionMiddleware : IHttpApplication, IDisposable
	{
		const string COOKIE_SESSION_ID = "ksid";
		const string COOKIE_TOKEN = "ktid";
		static readonly TimeSpan COOKIE_EXPIRES = TimeSpan.FromDays (365 * 10);
		const int SessionIdBytes = 32;
		const int TokenBytes = 32;

		CreateDatabaseConnectionDelegate _createDb;
		IHttpApplication _app;
		LRU _cache;

		static RNGCryptoServiceProvider _rng = new RNGCryptoServiceProvider ();

		public SessionMiddleware (CreateDatabaseConnectionDelegate createdb, IHttpApplication app)
		{
			if (createdb == null || app == null)
				throw new ArgumentNullException ();
			_createDb = createdb;
			_app = app;
			_cache = new LRU (32);
			_cache.PageOut += UpdateDatabase;

			using (IDbConnection connection = createdb ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable))
			using (IDbCommand cmd = connection.CreateCommand ()) {
				cmd.CommandText = "CREATE TABLE IF NOT EXISTS SessionStateStore (id TEXT PRIMARY KEY, expired INTEGER, state BLOB);";
				cmd.ExecuteNonQuery ();
				transaction.Commit ();
			}
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			string sessionId;
			SessionData data = null;
			if (!req.Cookies.TryGetValue (COOKIE_SESSION_ID, out sessionId)) {
				byte[] raw = new byte[SessionIdBytes];
				_rng.GetBytes (raw);
				sessionId = ToHexString (raw);
				res[HttpHeaderNames.SetCookie] = COOKIE_SESSION_ID + "=" + sessionId +
					"; expires=" + DateTime.UtcNow.Add (COOKIE_EXPIRES).ToString ("r") +
					"; path=/";
			} else {
				data = _cache.Get (sessionId);
				if (data == null)
					data = QueryFromDatabase (sessionId);
			}
			if (data == null)
				data = new SessionData (sessionId, new Dictionary<string, object> ());
			req.Session = data;
			try {
				return _app.Process (server, req, res);
			} finally {
				_cache.Set (sessionId, data);
			}
		}

		SessionData QueryFromDatabase (string sessionId)
		{
			using (IDbConnection connection = _createDb ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.ReadCommitted))
			using (IDbCommand cmd = connection.CreateCommand ()) {
				IDataParameter param = cmd.CreateParameter ();
				param.Value = sessionId;
				cmd.CommandText = "SELECT state FROM SessionStateStore WHERE id = ?";
				cmd.Parameters.Add (param);

				byte[] binary = cmd.ExecuteScalar () as byte[];
				if (binary != null) {
					BinaryFormatter formatter = new BinaryFormatter ();
					try {
						using (MemoryStream ms = new MemoryStream (binary)) {
							Dictionary<string, object> state = formatter.Deserialize (ms) as Dictionary<string, object>;
							if (state != null)
								return new SessionData (sessionId, state);
						}
					} catch {}
				}
			}
			return null;
		}

		void UpdateDatabase (object sender, SessionData[] data)
		{
			using (IDbConnection connection = _createDb ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable))
			using (IDbCommand cmd = connection.CreateCommand ()) {
				cmd.CommandText = "INSERT OR REPLACE INTO SessionStateStore (id, state) VALUES (?, ?)";
				IDataParameter param1 = cmd.CreateParameter ();
				IDataParameter param2 = cmd.CreateParameter ();
				cmd.Parameters.Add (param1);
				cmd.Parameters.Add (param2);

				BinaryFormatter formatter = new BinaryFormatter ();
				for (int i = 0; i < data.Length; i ++) {
					param1.Value = data[i].ID;
					using (MemoryStream ms = new MemoryStream ()) {
						formatter.Serialize (ms, data[i].State);
						ms.Close ();
						param2.Value = ms.ToArray ();
					}
					try {
						cmd.ExecuteNonQuery ();
					} catch {}
				}
				transaction.Commit ();
			}
		}

		static string ToHexString (byte[] raw)
		{
			StringBuilder sb = new StringBuilder (raw.Length * 2);
			for (int i = 0; i < raw.Length; i ++)
				sb.Append (raw[i].ToString ("x2"));
			return sb.ToString ();
		}

		public void Dispose ()
		{
			_cache.ForceDropAll ();
		}

		class SessionData : ISessionData
		{
			string _id;
			Dictionary<string, object> _state;

			public SessionData (string id, Dictionary<string, object> state)
			{
				_id = id;
				_state = state;
			}

			public string ID {
				get { return _id; }
			}

			public Dictionary<string, object> State {
				get { return _state; }
			}
		}

		class LRU
		{
			LinkedList<SessionData> _lru;
			Dictionary<string, LinkedListNode<SessionData>> _dic;
			int _capacity;
			public event LRUPageOutEventHandler PageOut;

			public LRU (int capacity)
			{
				_lru = new LinkedList<SessionData> ();
				_dic = new Dictionary<string,LinkedListNode<SessionData>> (capacity);
				_capacity = capacity;
			}

			public SessionData Get (string id)
			{
				lock (_dic) {
					LinkedListNode<SessionData> node;
					if (!_dic.TryGetValue (id, out node))
						return null;

					_lru.Remove (node);
					_lru.AddFirst (node);
					return node.Value;
				}
			}

			public void Set (string id, SessionData data)
			{
				SessionData pageout_data = null;

				lock (_dic) {
					LinkedListNode<SessionData> node;
					if (!_dic.TryGetValue (id, out node)) {
						node = new LinkedListNode<SessionData> (data);
						if (_lru.Count == _capacity && _lru.Last != null && PageOut != null) {
							pageout_data = _lru.Last.Value;
							_dic.Remove (_lru.Last.Value.ID);
							_lru.Remove (_lru.Last);
						}
					} else {
						_lru.Remove (node);
					}
					_lru.AddFirst (node);
					_dic[id] = node;
				}

				if (pageout_data == null)
					return;

				try {
					PageOut (this, new SessionData[] {pageout_data});
				} catch {}
			}

			public void ForceDropAll ()
			{
				if (PageOut == null)
					return;
				lock (_dic) {
					List<SessionData> list = new List<SessionData> (_lru);
					PageOut (this, list.ToArray ());
				}
			}
		}
		delegate void LRUPageOutEventHandler (object sender, SessionData[] outdata);
	}

	public delegate IDbConnection CreateDatabaseConnectionDelegate ();
}
