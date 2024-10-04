using System.Collections.Concurrent;
using trakit.https;
using trakit.objects;
using trakit.wss;

namespace trakit.sync {
	/// <summary>
	/// A class to help manage and synchronize <see cref="Component"/> objects.
	/// </summary>
	public class TrakitSync {
		/// <summary>
		/// 
		/// </summary>
		public TrakitRestful rest = new TrakitRestful();
		/// <summary>
		/// 
		/// </summary>
		public TrakitSocket socket = new TrakitSocket();
		/// <summary>
		/// 
		/// </summary>
		public ConcurrentDictionary<ulong, Company> companies = new ConcurrentDictionary<ulong, Company>();
	}
}