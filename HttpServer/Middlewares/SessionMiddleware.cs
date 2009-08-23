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
		static readonly string COOKIE_EXPIRES = DateTime.UtcNow.Add (TimeSpan.FromDays (365 * 32)).ToString ("r");
		const int SessionIdBytes = 32;
		const int TokenBytes = 32;

		const string SQL_CREATE_TABLE = "CREATE TABLE IF NOT EXISTS SessionData (id TEXT, key TEXT, data BLOB, PRIMARY KEY (id, key));";
		const string SQL_SELECT = "SELECT data FROM SessionData WHERE id=:id AND key=:key;";
		const string SQL_UPDATE = "INSERT OR REPLACE INTO SessionData (id, key, data) VALUES (:id, :key, :data);";
		const string SQL_DELETE = "DELETE FROM SessionData WHERE id=:id AND key=:key;";

		CreateDatabaseConnectionDelegate _createDb;
		IHttpApplication _app;

		static RNGCryptoServiceProvider _rng = new RNGCryptoServiceProvider ();

		public SessionMiddleware (CreateDatabaseConnectionDelegate createdb, IHttpApplication app)
		{
			if (createdb == null || app == null)
				throw new ArgumentNullException ();
			_createDb = createdb;
			_app = app;

			using (IDbConnection connection = createdb ())
			using (IDbTransaction transaction = connection.BeginTransaction (IsolationLevel.Serializable))
			using (IDbCommand cmd = connection.CreateCommand ()) {
				cmd.CommandText = "DROP TABLE IF EXISTS SessionStateStore;";
				cmd.ExecuteNonQuery ();

				cmd.CommandText = SQL_CREATE_TABLE;
				cmd.ExecuteNonQuery ();

				transaction.Commit ();
			}
		}

		public object Process (IHttpServer server, IHttpRequest req, HttpResponseHeader res)
		{
			string sessionId;
			if (!req.Cookies.TryGetValue (COOKIE_SESSION_ID, out sessionId)) {
				byte[] raw = new byte[SessionIdBytes];
				_rng.GetBytes (raw);
				sessionId = ToHexString (raw);
				res[HttpHeaderNames.SetCookie] = COOKIE_SESSION_ID + "=" + sessionId +
					"; expires=" + COOKIE_EXPIRES + "; path=/";
			}
			req.Session = new SessionData (sessionId, _createDb);
			return _app.Process (server, req, res);
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
		}

		class SessionData : ISessionData
		{
			string _id;
			CreateDatabaseConnectionDelegate _create;

			public SessionData (string id, CreateDatabaseConnectionDelegate create)
			{
				_id = id;
				_create = create;
			}

			public string ID {
				get { return _id; }
			}

			public ISessionTransaction BeginTransaction (IsolationLevel isolation)
			{
				return DbTransaction.Create (this, _create, isolation);
			}

			public object ReadState (string key)
			{
				using (DbTransaction transaction = DbTransaction.Create (this, _create, IsolationLevel.ReadCommitted)) {
					return ReadState (key, transaction);
				}
			}

			public object ReadState (string key, ISessionTransaction transaction)
			{
				DbTransaction t = (DbTransaction)transaction;

				using (IDbCommand cmd = t.Connection.CreateCommand ()) {
					cmd.CommandText = SQL_SELECT;

					IDataParameter p_id = cmd.CreateParameter (), p_key = cmd.CreateParameter ();
					p_id.ParameterName = "id";
					p_key.ParameterName = "key";
					p_id.Value = _id;
					p_key.Value = key;
					cmd.Parameters.Add (p_id);
					cmd.Parameters.Add (p_key);

					using (IDataReader reader = cmd.ExecuteReader ()) {
						if (reader.Read ()) {
							try {
								BinaryFormatter formatter = new BinaryFormatter ();
								using (MemoryStream ms = new MemoryStream ((byte[])reader.GetValue (0))) {
									return formatter.Deserialize (ms);
								}
							} catch {}
						}
					}
				}
				return null;
			}

			public void UpdateState (string key, object state)
			{
				using (ISessionTransaction transaction = DbTransaction.Create (this, _create, IsolationLevel.Serializable)) {
					UpdateState (key, state, transaction);
				}
			}

			public void UpdateState (string key, object state, ISessionTransaction transaction)
			{
				DbTransaction t = (DbTransaction)transaction;

				using (IDbCommand cmd = t.Connection.CreateCommand ()) {
					cmd.CommandText = (state == null ? SQL_DELETE : SQL_UPDATE);

					IDataParameter p_id = cmd.CreateParameter (), p_key = cmd.CreateParameter ();
					p_id.ParameterName = "id";
					p_key.ParameterName = "key";
					p_id.Value = _id;
					p_key.Value = key;
					cmd.Parameters.Add (p_id);
					cmd.Parameters.Add (p_key);

					if (state != null) {
						IDataParameter p_data = cmd.CreateParameter ();
						p_data.ParameterName = "data";
						using (MemoryStream ms = new MemoryStream ()) {
							BinaryFormatter formatter = new BinaryFormatter ();
							formatter.Serialize (ms, state);
							ms.Close ();
							p_data.Value = ms.ToArray ();
						}
						cmd.Parameters.Add (p_data);
					}

					cmd.ExecuteNonQuery ();
				}
			}

			class DbTransaction : ISessionTransaction
			{
				IDbConnection _connection;
				IDbTransaction _transaction;
				ISessionData _session;

				public static DbTransaction Create (ISessionData session, CreateDatabaseConnectionDelegate create, IsolationLevel level)
				{
					DbTransaction t = new DbTransaction ();
					t._session = session;
					t._connection = create ();
					t._transaction = t.Connection.BeginTransaction (level);
					return t;
				}

				public void Commit ()
				{
					_transaction.Commit ();
				}

				public void Rollback ()
				{
					_transaction.Rollback ();
				}
				
				public object ReadState (string key)
				{
					return _session.ReadState (key, this);
				}

				public void UpdateState (string key, object state)
				{
					_session.UpdateState (key, state, this);
				}

				public IDbConnection Connection {
					get { return _connection; }
				}

				public IDbTransaction Transaction {
					get { return _transaction; }
				}

				public void Dispose ()
				{
					_transaction.Dispose ();
					_connection.Dispose ();
				}
			}
		}

#if false
		class LRU<T> where T : class
		{
			LinkedList<T> _lru;
			Dictionary<string, LinkedListNode<T>> _dic;
			int _capacity;
			public event LRUPageOutEventHandler<T> PageOut;

			public LRU (int capacity)
			{
				_lru = new LinkedList<T> ();
				_dic = new Dictionary<string,LinkedListNode<T>> (capacity);
				_capacity = capacity;
			}

			public T Get (string id)
			{
				lock (_dic) {
					LinkedListNode<T> node;
					if (!_dic.TryGetValue (id, out node))
						return null;

					_lru.Remove (node);
					_lru.AddFirst (node);
					return node.Value;
				}
			}

			public void Set (string id, T data)
			{
				T pageout_data = null;

				lock (_dic) {
					LinkedListNode<T> node;
					if (!_dic.TryGetValue (id, out node)) {
						node = new LinkedListNode<T> (data);
						if (_lru.Count == _capacity && _lru.Last != null && PageOut != null) {
							pageout_data = _lru.Last.Value;
							_dic.Remove (id);
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
					PageOut (this, new T[] {pageout_data});
				} catch {}
			}

			public void ForceDropAll ()
			{
				if (PageOut == null)
					return;
				lock (_dic) {
					List<T> list = new List<T> (_lru);
					PageOut (this, list.ToArray ());
				}
			}
		}
		delegate void LRUPageOutEventHandler<T> (object sender, T[] outdata);
#endif
	}

	public delegate IDbConnection CreateDatabaseConnectionDelegate ();
}
