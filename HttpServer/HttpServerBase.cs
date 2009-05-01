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
using System.Threading;

namespace Kazuki.Net.HttpServer
{
	abstract class HttpServerBase<T> : IHttpServer
	{
		Thread _acceptThread;
		Thread _cometThread;
		protected bool _closed = false;
		protected IHttpApplication _app, _errApp;
		List<WaitHandle> _cometHandles = new List<WaitHandle> ();
		Dictionary<WaitHandle, List<CometInfo>> _comets = new Dictionary<WaitHandle,List<CometInfo>> ();
		const int CometWaitTimeout = 500;

		protected HttpServerBase (IHttpApplication app, IHttpApplication errApp)
		{
			_app = app;
			_errApp = errApp;
			_acceptThread = new Thread (AcceptThread);
			_cometThread = new Thread (CometThread);
			_cometThread.Start ();
		}

		#region Accept
		protected abstract T[] Accept ();

		protected void StartAcceptThread ()
		{
			_acceptThread.Start ();
		}

		private void AcceptThread ()
		{
			while (!_closed) {
				try {
					T[] list = Accept ();
					if (list == null)
						continue;
					for (int i = 0; i < list.Length; i++)
						ThreadPool.QueueUserWorkItem (ProcessClientConnection, list[i]);
				} catch {}
			}
		}
		#endregion

		#region Process
		protected abstract void ProcessClientConnection (T ctx);
		protected void ProcessClientConnection (object ctx)
		{
			ProcessClientConnection ((T)ctx);
		}
		#endregion

		#region Comet
		protected void AddCometInfo (CometInfo info)
		{
			lock (_cometHandles) {
				List<CometInfo> list;
				if (!_comets.TryGetValue (info.WaitHandle, out list)) {
					list = new List<CometInfo> ();
					_comets.Add (info.WaitHandle, list);
					_cometHandles.Add (info.WaitHandle);
				}
				list.Add (info);
			}
		}

		void CometThread ()
		{
			while (!_closed) {
				WaitHandle[] handles;
				lock (_cometHandles) {
					handles = (_cometHandles.Count == 0 ? null : _cometHandles.ToArray ());
				}
				if (handles == null) {
					Thread.Sleep (500);
					continue;
				}
				int ret = WaitHandle.WaitAny (handles, CometWaitTimeout, false);
				if (ret != WaitHandle.WaitTimeout) {
					List<CometInfo> list;
					lock (_cometHandles) {
						list = _comets[handles[ret]];
						_comets.Remove (handles[ret]);
						_cometHandles.RemoveAt (ret);
					}
					for (int i = 0; i < list.Count; i ++)
						ThreadPool.QueueUserWorkItem (ProcessComet, list[i]);
				}

				List<CometInfo> timeoutList = new List<CometInfo> ();
				DateTime now = DateTime.Now;
				lock (_cometHandles) {
					for (int q = 0; q < _cometHandles.Count; q ++) {
						List<CometInfo> list = _comets[_cometHandles[q]];
						for (int i = 0; i < list.Count; i ++) {
							if (list[i].CheckTimeout (now)) {
								timeoutList.Add (list[i]);
								list.RemoveAt (i);
								i --;
								if (list.Count == 0) {
									_comets.Remove (_cometHandles[q]);
									_cometHandles.RemoveAt (q);
									q --;
									break;
								}
							}
						}
					}
				}
				for (int i = 0; i < timeoutList.Count; i ++)
					ThreadPool.QueueUserWorkItem (ProcessComet, timeoutList[i]);
			}
		}

		protected abstract void ProcessComet (object cometInfoObj);
		#endregion

		#region Dispose
		protected abstract void DisposeInternal ();
		public void Dispose ()
		{
			_closed = true;
			DisposeInternal ();
			if (!_cometThread.Join (1000)) {
				try {
					_cometThread.Abort ();
				} catch {}
			}
		}
		#endregion
	}
}
