using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
		/// 
		const int MAX_CONCURRENT_COMMANDS = 10;
		/// <summary>
		/// Returns the appropriate <see cref="SubscriptionType"/>s for the given object.
		/// </summary>
		/// <typeparam name="TRequestable">An <see cref="IRequestable"/> resource that can be synchronized by a client.</typeparam>
		/// <param name="requestable">The instance of an <see cref="IRequestable"/> object.</param>
		/// <returns>An array of <see cref="SubscriptionType"/>s (usually only one item) to keep the given <see cref="IRequestable"/> in-sync.</returns>
		/// <exception cref="KeyNotFoundException">When <typeparamref name="TRequestable"/> is not capable of being synchronized.</exception>
		public static SubscriptionType[] GetSubscriptionsByObject<TRequestable>(
			TRequestable requestable = default
		) where TRequestable : class, IRequestable => GetSubscriptionsByType(
			requestable?.GetType() ?? typeof(TRequestable)
		);
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
			switch (type.Name) {
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
		public TrakitRestfulCommander Rest = new TrakitRestfulCommander();
		/// <summary>
		/// 
		/// </summary>
		public TrakitSocketCommander Socket = new TrakitSocketCommander();
		/// <summary>
		/// 
		/// </summary>
		ConcurrentDictionary<string, ConcurrentDictionary<string, Component>> STORAGE = new ConcurrentDictionary<string, ConcurrentDictionary<string, Component>>();

		public TrakitSync() {
			this.Socket.MessageReceived += _syncHandling;
		}

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
			/// Returns a list of subscription types that are not set to expire.
			/// </summary>
			/// <returns></returns>
			internal List<SubscriptionType> GetActiveSubscriptions() {
				var subscriptions = new List<SubscriptionType>();
				foreach (var pair in this.Subscriptions) {
					if (!pair.Value.HasValue) {
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
		ConcurrentDictionary<ulong, ActiveSubscriptions> _currentSubscriptions = new ConcurrentDictionary<ulong, ActiveSubscriptions>();

		void _syncHandling(TrakitSocketCommander socket, TrakitSocketMessage message) {
			switch (message.name) {
				case "assetDeleted": {
					var deleted = this.Socket.Serializer.Deserialize<AssetGeneral>(message.body);
					if (STORAGE.TryGetValue("asset", out var storage)) {
						storage.TryRemove(deleted.GetKey(), out _);
					}
				}
				break;
				case "companyDeleted":
				case "companyGeneralMerged":
					var companyGeneral = this.Socket.Serializer.Deserialize<CompanyGeneral>(message.body);
					STORAGE.GetOrAdd("company", (k) => new ConcurrentDictionary<string, Component>()).AddOrUpdate(
						companyGeneral.GetKey(),
						new Company() { General = companyGeneral },
						(k, obj) => {
							var company = (Company)obj;
							if (companyGeneral > company.General) company.General = companyGeneral;
							return company;
						}
					);
					break;
				case "companyLabelsMerged":
					var companyLabels = this.Socket.Serializer.Deserialize<CompanyStyles>(message.body);
					STORAGE.GetOrAdd("company", (k) => new ConcurrentDictionary<string, Component>()).AddOrUpdate(
						companyLabels.GetKey(),
						new Company() { Styles = companyLabels },
						(k, obj) => {
							var company = (Company)obj;
							if (companyLabels > company.Styles) company.Styles = companyLabels;
							return company;
						}
					);
					break;
				case "companyPoliciesMerged":
					var companyPolicies = this.Socket.Serializer.Deserialize<CompanyPolicies>(message.body);
					STORAGE.GetOrAdd("company", (k) => new ConcurrentDictionary<string, Component>()).AddOrUpdate(
						companyPolicies.GetKey(),
						new Company() { Policies = companyPolicies },
						(k, obj) => {
							var company = (Company)obj;
							if (companyPolicies > company.Styles) company.Policies = companyPolicies;
							return company;
						}
					);
					break;
				case "companyResellerMerged":
					var companyReseller = this.Socket.Serializer.Deserialize<CompanyReseller>(message.body);
					STORAGE.GetOrAdd("company", (k) => new ConcurrentDictionary<string, Component>()).AddOrUpdate(
						companyReseller.GetKey(),
						new Company() { Reseller = companyReseller },
						(k, obj) => {
							var company = (Company)obj;
							if (companyReseller > company.Styles) company.Reseller = companyReseller;
							return company;
						}
					);
					break;
				default:
					// not handled
					break;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="companyId"></param>
		/// <param name="objectTypes"></param>
		/// <returns></returns>
		public Task<SubscriptionType[]> Sync(ulong companyId, params Type[] objectTypes)
			=> this.Sync(companyId, objectTypes.ToList());
		/// <summary>
		/// 
		/// </summary>
		/// <param name="companyId"></param>
		/// <param name="objectTypes"></param>
		/// <returns></returns>
		/// <exception cref="AggregateException"></exception>
		public async Task<SubscriptionType[]> Sync(ulong companyId, IEnumerable<Type> objectTypes) {
			// do this first to throw for invalid object types
			var objectSubs = objectTypes.ToDictionary(
				o => o,
				o => GetSubscriptionsByType(o).ToList()
			);

			if (this.Socket.Status == TrakitSocketStatus.Closed) {
				await this.Socket.Connect();
			} else if (this.Socket.Status != TrakitSocketStatus.Opened) {
				var conn = new TaskCompletionSource<TrakitSocketStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
				void handler(TrakitSocketCommander sender) {
					this.Socket.StatusChanged -= handler;
					if (
						this.Socket.Status != TrakitSocketStatus.Opened
						|| !conn.TrySetResult(this.Socket.Status)
					) {
						conn.TrySetCanceled();
					}
				}
				this.Socket.StatusChanged += handler;
				if (this.Socket.Status == TrakitSocketStatus.Opened) {
					// in case the status did change before the handler was bound
					conn.TrySetResult(this.Socket.Status);
				}
				await conn.Task;
			}

			var active = this._currentSubscriptions.GetOrAdd(companyId, _ => new ActiveSubscriptions());
			var existingSubscriptions = active.GetActiveSubscriptions();
			var newSubscriptions = new ConcurrentBag<SubscriptionType>();
			var errors = new ConcurrentBag<Exception>();

			using (var sauce = new CancellationTokenSource()) {
				using (var throttler = new SemaphoreSlim(Math.Min(MAX_CONCURRENT_COMMANDS, Environment.ProcessorCount))) {
					await Task.WhenAll(objectSubs.Select(async p => {
						var subs = p.Value.Except(existingSubscriptions).ToArray();
						if (subs.Length > 0) {
							try {
								await throttler.WaitAsync(sauce.Token);
								// subscribe
								Payload socketRequest = null;// make this somehow
								Reply socketResponse = await this.Socket.Command<Reply>(socketRequest);
								if (socketResponse.errorCode != ErrorCode.success) {
									//throw new CommandError(response);
								}
								// load
								Payload restRequest = null;// now make it for REST
								Reply restResponse = await this.Rest.Command<Reply>(restRequest);
								if (restResponse.errorCode != ErrorCode.success) {
									//throw new CommandError(response);
								}
								// add REST response content to storage
							} catch (Exception ex) {
								errors.Add(ex);
							} finally {
								throttler.Release();
							}
							foreach (var sub in subs) {
								active.RemoveExpiry(sub);
								newSubscriptions.Add(sub);
							}
						}
					}));
				}
			}
			return errors.Count > 0
				? throw new AggregateException(errors)
				: newSubscriptions.ToArray();
		}
	}
}