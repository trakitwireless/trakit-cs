using System.Collections.Concurrent;
using Trakit.Https;
using Trakit.Objects;
using Trakit.Wss;

namespace Trakit.Sync {
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