using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Trakit.Commands;
using Trakit.Objects;
using Trakit.Restful;
using Trakit.Socket;

namespace Trakit.Sync {
	/// <summary>
	/// A class to help manage and synchronize <see cref="Component"/> objects.
	/// </summary>
	public class TrakitSync {
		/// <summary>
		/// Returns the appropriate <see cref="SubscriptionType"/>s for the given object.
		/// </summary>
		/// <typeparam name="TRequestable">An <see cref="IRequestable"/> resource that can be synchronized by a client.</typeparam>
		/// <param name="requestable">The instance of an <see cref="IRequestable"/> object.</param>
		/// <returns>An array of <see cref="SubscriptionType"/>s (usually only one item) to keep the given <see cref="IRequestable"/> in-sync.</returns>
		/// <exception cref="InvalidOperationException">When <typeparamref name="TRequestable"/> does not implement <see cref="IRequestable"/>.</exception>
		/// <exception cref="KeyNotFoundException">When <typeparamref name="TRequestable"/> is not capable of being synchronized.</exception>
		public static SubscriptionType[] GetSubscriptionsByObject<TRequestable>(
			TRequestable requestable = default
		) where TRequestable : IRequestable
			=> GetSubscriptionsByType(requestable?.GetType() ?? typeof(TRequestable));
		/// <summary>
		/// Returns the appropriate <see cref="SubscriptionType"/>s for the given type.
		/// </summary>
		/// <param name="type">The type of <see cref="IRequestable"/>.</param>
		/// <returns>An array of <see cref="SubscriptionType"/>s (usually only one item) to keep the given <see cref="IRequestable"/> in-sync.</returns>
		/// <exception cref="ArgumentNullException">When <paramref name="type"/> is <see langword="null"/>.</exception>
		/// <exception cref="InvalidOperationException">When <paramref name="type"/> does not implement <see cref="IRequestable"/>.</exception>
		/// <exception cref="KeyNotFoundException">When <paramref name="type"/> is not capable of being synchronized.</exception>
		public static SubscriptionType[] GetSubscriptionsByType(Type type) {
			if (type == default) {
				throw new ArgumentNullException(nameof(type), $"{nameof(type)} cannot be null");
			}
			if (!typeof(IRequestable).IsAssignableFrom(type)) {
				throw new InvalidOperationException($"{type.FullName} is not IRequestable");
			}
			switch (type.FullName.Split('.').Last()) {
				#region Company
				/// <seealso cref="Company"/>
				case "Company":
					return new[] {
						SubscriptionType.companyGeneral,
						SubscriptionType.companyLabels,
						SubscriptionType.companyPolicies,
						SubscriptionType.companyGeneral,
						SubscriptionType.companyReseller,
					};
				/// <seealso cref="CompanyGeneral"/>
				case "CompanyGeneral":
					return new[] {
						SubscriptionType.companyGeneral,
					};
				/// <seealso cref="CompanyDirectory"/>
				case "CompanyDirectory":
					break;    // not yet implemented
				/// <seealso cref="CompanyStyles"/>
				case "CompanyStyles":
					return new[] {
						SubscriptionType.companyLabels,
					};
				/// <seealso cref="CompanyPolicies"/>
				case "CompanyPolicies":
					return new[] {
						SubscriptionType.companyPolicies,
					};
				/// <seealso cref="CompanyReseller"/>
				case "CompanyReseller":
					return new[] {
						SubscriptionType.companyReseller,
					};
				#endregion Company

				#region Contacts
				/// <seealso cref="Contact"/>
				case "Contact":
					return new[] {
						SubscriptionType.contact,
					};
				#endregion Contacts

				#region Billing
				/// <seealso cref="BillingProfile"/>
				case "BillingProfile":
					return new[] {
						SubscriptionType.billingProfile,
					};
				/// <seealso cref="BillableHostingRule"/>
				case "BillableHostingRule":
					return new[] {
						SubscriptionType.billingHosting,
					};
				/// <seealso cref="BillableHostingLicense"/>
				case "BillableHostingLicense":
					return new[] {
						SubscriptionType.billingLicense,
					};
				/// <seealso cref="BillingReport"/>
				case "BillingReport":
					return new[] {
						SubscriptionType.billingReport,
					};
				#endregion Billing

				#region Behaviours
				/// <seealso cref="Behaviour"/>
				case "Behaviour":
					return new[] {
						SubscriptionType.behaviour,
					};
				/// <seealso cref="BehaviourScript"/>
				case "BehaviourScript":
					return new[] {
						SubscriptionType.behaviourScript,
					};
				/// <seealso cref="BehaviourLog"/>
				case "BehaviourLog":
					return new[] {
						SubscriptionType.behaviourLog,
					};
				#endregion Behaviours

				#region Assets
				/// <seealso cref="Person"/>
				/// <seealso cref="Vehicle"/>
				/// <seealso cref="Trailer"/>
				/// <seealso cref="Asset"/>
				case "Person":
				case "Vehicle":
				case "Trailer":
				case "Asset":
					return new[] {
						SubscriptionType.assetGeneral,
						SubscriptionType.assetAdvanced,
						SubscriptionType.assetDispatch,
					};
				/// <seealso cref="PersonGeneral"/>
				/// <seealso cref="VehicleGeneral"/>
				/// <seealso cref="TrailerGeneral"/>
				/// <seealso cref="AssetGeneral"/>
				case "PersonGeneral":
				case "VehicleGeneral":
				case "TrailerGeneral":
				case "AssetGeneral":
					return new[] {
						SubscriptionType.assetGeneral,
					};
				/// <seealso cref="AssetAdvanced"/>
				/// <seealso cref="VehicleAdvanced"/>
				case "AssetAdvanced":
				case "VehicleAdvanced":
					return new[] {
						SubscriptionType.assetAdvanced,
					};
				/// <seealso cref="AssetDispatch"/>
				case "AssetDispatch":
					return new[] {
						SubscriptionType.assetDispatch,
					};
				#endregion Assets

				#region Dispatch
				/// <seealso cref="DispatchTask"/>
				case "DispatchTask":
					return new[] {
						SubscriptionType.dispatchTask,
					};
				/// <seealso cref="DispatchJob"/>
				case "DispatchJob":
					return new[] {
						SubscriptionType.dispatchJob,
					};
				#endregion Dispatch

				#region Reports
				/// <seealso cref="ReportTemplate"/>
				case "ReportTemplate":
					return new[] {
						SubscriptionType.reportTemplate,
					};
				/// <seealso cref="ReportSchedule"/>
				case "ReportSchedule":
					return new[] {
						SubscriptionType.reportSchedule,
					};
				/// <seealso cref="ReportResult"/>
				case "ReportResult":
					return new[] {
						SubscriptionType.reportResult,
					};
				#endregion Reports

				#region Places
				/// <seealso cref="Place"/>
				case "Place":
					return new[] {
						SubscriptionType.placeGeneral,
					};
				#endregion Places

				#region Images and Files
				/// <seealso cref="Icon"/>
				case "Icon":
					return new[] {
						SubscriptionType.icon,
					};
				/// <seealso cref="Picture"/>
				case "Picture":
					return new[] {
						SubscriptionType.picture,
					};
				/// <seealso cref="Document"/>
				case "Document":
					return new[] {
						SubscriptionType.document,
					};
				/// <seealso cref="FormTemplate"/>
				case "FormTemplate":
					return new[] {
						SubscriptionType.formTemplate,
					};
				/// <seealso cref="FormResult"/>
				case "FormResult":
					return new[] {
						SubscriptionType.formResult,
					};
				/// <seealso cref="Dashcam"/>
				case "Dashcam":
				/// <seealso cref="DashcamLive"/>
				case "DashcamLive":
					break;// these can't be kept in sync
				#endregion Images and Files

				#region Maintenance
				/// <seealso cref="MaintenanceJob"/>
				case "MaintenanceJob":
					return new[] {
						SubscriptionType.maintenanceJob,
					};
				/// <seealso cref="MaintenanceSchedule"/>
				case "MaintenanceSchedule":
					return new[] {
						SubscriptionType.maintenanceSchedule,
					};
				#endregion Maintenance

				#region Providers and Configs
				/// <seealso cref="Provider"/>
				case "Provider":
					return new[] {
						SubscriptionType.providerGeneral,
						SubscriptionType.providerAdvanced,
						SubscriptionType.providerControl,
					};
				/// <seealso cref="ProviderGeneral"/>
				case "ProviderGeneral":
					return new[] {
						SubscriptionType.providerGeneral,
					};
				/// <seealso cref="ProviderAdvanced"/>
				case "ProviderAdvanced":
					return new[] {
						SubscriptionType.providerAdvanced,
					};
				/// <seealso cref="ProviderControl"/>
				case "ProviderControl":
					return new[] {
						SubscriptionType.providerControl,
					};
				/// <seealso cref="ProviderRegistration"/>
				case "ProviderRegistration":
					return new[] {
						SubscriptionType.providerRegistration,
					};

				/// <seealso cref="ProviderScript"/>
				case "ProviderScript":
					return new[] {
						SubscriptionType.providerScript,
					};
				/// <seealso cref="ProviderConfig"/>
				case "ProviderConfig":
					return new[] {
						SubscriptionType.providerConfig,
					};
				/// <seealso cref="ProviderConfigurationType"/>
				case "ProviderConfigurationType":
					break;    // static data, does not need sync
				/// <seealso cref="ProviderConfiguration"/>
				case "ProviderConfiguration":
					return new[] {
						SubscriptionType.providerConfiguration,
					};
				#endregion Providers and Configs

				#region Messaging
				/// <seealso cref="AssetAlert"/>
				case "AssetAlert":
					break;    // these are always sent to connected clients
				/// <seealso cref="AssetMessage"/>
				case "AssetMessage":
					return new[] {
						SubscriptionType.assetMessage,
					};
				#endregion Messaging

				#region Users and Groups
				/// <seealso cref="User"/>
				case "User":
					return new[] {
						SubscriptionType.userGeneral,
						SubscriptionType.userAdvanced,
					};
				/// <seealso cref="UserGeneral"/>
				case "UserGeneral":
					return new[] {
						SubscriptionType.userGeneral,
					};
				/// <seealso cref="UserAdvanced"/>
				case "UserAdvanced":
					return new[] {
						SubscriptionType.userAdvanced,
					};
				/// <seealso cref="UserGroup"/>
				case "UserGroup":
					return new[] {
						SubscriptionType.userGroup,
					};
				/// <seealso cref="Machine"/>
				case "Machine":
					return new[] {
						SubscriptionType.machine,
					};
				/// <seealso cref="Session"/>
				case "Session":
					break;// these can't be kept in sync
					#endregion Users and Groups
			}
			throw new KeyNotFoundException($"{type.FullName} cannot be kept in-sync");
		}

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

		/// <summary>
		/// A class to contain all the subscriptions for a company.
		/// This class also sets the expiration dates.
		/// </summary>
		class ActiveSubscriptions {
			/// <summary>
			/// The amount of time (in milliseconds) to wait before automatically removing a subscription.
			/// </summary>
			const int DEFAULT_TIMEOUT_MS = 5 * 60 * 1000; // 5 minutes

			/// <summary>
			/// A dictionary of subscription type to expiry date.
			/// The expiry date is when the subscription type is due to be removed.
			/// </summary>
			internal ConcurrentDictionary<SubscriptionType, DateTime?> Subscriptions = new ConcurrentDictionary<SubscriptionType, DateTime?>();

			/// <summary>
			/// Returns a list of subscription types that should be removed.
			/// When <paramref name="purge"/> is true, it will also remove the subscription type dictionary,
			/// that way it will no longer be listed as an active subscription, or as expired.
			/// </summary>
			/// <param name="purge"></param>
			/// <returns></returns>
			internal List<SubscriptionType> GetExpiredSubscriptions(bool purge) {
				var now = DateTime.UtcNow;
				var subscriptions = new List<SubscriptionType>();
				foreach (var pair in this.Subscriptions) {
					if (pair.Value.HasValue && pair.Value < now) {
						subscriptions.Add(pair.Key);
					}
				}
				if (purge) {
					foreach (var subscription in subscriptions) {
						this.Subscriptions.TryRemove(subscription, out _);
					}
				}
				return subscriptions;
			}
			/// <summary>
			/// Returns a list of subscription types that will be removed eventually.
			/// </summary>
			/// <returns></returns>
			internal List<SubscriptionType> GetExpiringSubscriptions() {
				var subscriptions = new List<SubscriptionType>();
				foreach (var pair in this.Subscriptions) {
					if (pair.Value.HasValue) {
						subscriptions.Add(pair.Key);
					}
				}
				return subscriptions;
			}
			/// <summary>
			/// Sets the given expiry date for the given subscription type.
			/// </summary>
			/// <param name="subscription"></param>
			/// <param name="date"></param>
			/// <returns></returns>
			private DateTime? SetExpiry(SubscriptionType subscription, DateTime? date)
				=> this.Subscriptions.AddOrUpdate(subscription, date, (k, d) => date);
			/// <summary>
			/// Marks the given subscription type for expiration.
			/// </summary>
			/// <param name="subscription"></param>
			/// <returns></returns>
			internal DateTime? AddToExpiry(SubscriptionType subscription)
				=> this.AddToExpiry(subscription, TimeSpan.FromMilliseconds(DEFAULT_TIMEOUT_MS));
			/// <summary>
			/// Marks the given subscription type for expiration.
			/// </summary>
			/// <param name="subscription"></param>
			/// <param name="timeout"></param>
			/// <returns></returns>
			internal DateTime? AddToExpiry(SubscriptionType subscription, TimeSpan timeout)
				=> this.SetExpiry(subscription, DateTime.UtcNow.Add(timeout));
			/// <summary>
			/// Marks the given subscription types for expiration.
			/// </summary>
			/// <param name="subscriptions"></param>
			/// <returns></returns>
			internal List<DateTime?> AddToExpiries(IEnumerable<SubscriptionType> subscriptions)
				=> this.AddToExpiries(subscriptions, TimeSpan.FromMilliseconds(DEFAULT_TIMEOUT_MS));
			/// <summary>
			/// Marks the given subscription types for expiration.
			/// </summary>
			/// <param name="subscriptions"></param>
			/// <param name="timeout"></param>
			/// <returns></returns>
			internal List<DateTime?> AddToExpiries(IEnumerable<SubscriptionType> subscriptions, TimeSpan timeout)
				=> subscriptions.Select(r => this.AddToExpiry(r)).ToList();
			/// <summary>
			/// Clears the expiration of the given subscription type.
			/// </summary>
			/// <param name="subscription"></param>
			/// <returns></returns>
			internal DateTime? RemoveExpiry(SubscriptionType subscription)
				=> this.SetExpiry(subscription, default);
			/// <summary>
			/// Clears the expiration of the given subscription types.
			/// </summary>
			/// <param name="subscriptions"></param>
			/// <returns></returns>
			internal List<DateTime?> RemoveExpiries(IEnumerable<SubscriptionType> subscriptions)
				=> subscriptions.Select(r => this.RemoveExpiry(r)).ToList();

			/// <summary>
			/// Removes all subscription types, and returns a list of those that were not going to expire.
			/// </summary>
			/// <returns></returns>
			internal List<SubscriptionType> ResetAllExpiries() {
				var subscriptions = new List<SubscriptionType>();
				foreach (var pair in this.Subscriptions) {
					if (!pair.Value.HasValue) {
						subscriptions.Add(pair.Key);
					}
				}
				this.Subscriptions.Clear();
				return subscriptions;
			}
		}
		/// <summary>
		/// 
		/// </summary>
		ConcurrentDictionary<ulong, ActiveSubscriptions> __currentSubscriptions = new ConcurrentDictionary<ulong, ActiveSubscriptions>();


		/// <summary>
		/// 
		/// </summary>
		/// <param name="companyId"></param>
		/// <param name="objectTypes"></param>
		/// <returns></returns>
		public async Task<SubscriptionType[]> Sync(ulong companyId, IEnumerable<Type> objectTypes) {




			if (this.socket.Status != TrakitSocketStatus.Opened) {
				await this.socket.Connect();
			}
			var subscribed = this.__currentSubscriptions.GetOrAdd(companyId, (k) => new ActiveSubscriptions());





			return default;
		}
	}
}