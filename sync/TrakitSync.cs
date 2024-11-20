using System.Collections.Concurrent;
using Trakit.Restful;
using Trakit.Objects;
using Trakit.Socket;

namespace Trakit.Sync {
	/// <summary>
	/// A class to help manage and synchronize <see cref="Component"/> objects.
	/// </summary>
	public class TrakitSync {
		/// <summary>
		/// 
		/// </summary>
		public TrakitRestfulCommander rest = new TrakitRestfulCommander();
		/// <summary>
		/// 
		/// </summary>
		public TrakitSocketCommander socket = new TrakitSocketCommander();
		/// <summary>
		/// 
		/// </summary>
		public ConcurrentDictionary<ulong, Company> companies = new ConcurrentDictionary<ulong, Company>();
	}
}