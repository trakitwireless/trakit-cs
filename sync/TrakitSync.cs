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
		/// <typeparam name="T">An <see cref="IRequestable"/> resource that can be synchronized by a client.</typeparam>
		/// <param name="cacheable">The instance of an <see cref="IRequestable"/> object.</param>
		/// <returns>An array of <see cref="SubscriptionType"/>s (usually only one item) to keep the given <see cref="IRequestable"/> in-sync.</returns>
		/// <exception cref="InvalidOperationException">When <typeparamref name="T"/> does not implement <see cref="IRequestable"/>.</exception>
		/// <exception cref="KeyNotFoundException">When <typeparamref name="T"/> is not capable of being synchronized.</exception>
		public static SubscriptionType[] GetSubscriptionsByObject<T>(T cacheable = default) where T : IRequestable
			=> GetSubscriptionsByType(cacheable?.GetType() ?? typeof(T));
		/// <summary>
		/// Returns the appropriate <see cref="SubscriptionType"/>s for the given type.
		/// </summary>
		/// <param name="type">The type of <see cref="IRequestable"/>.</param>
		/// <returns>An array of <see cref="SubscriptionType"/>s (usually only one item) to keep the given <see cref="IRequestable"/> in-sync.</returns>
		/// <exception cref="ArgumentNullException">When <paramref name="type"/> is <c>null</c>.</exception>
		/// <exception cref="InvalidOperationException">When <paramref name="type"/> does not implement <see cref="IRequestable"/>.</exception>
		/// <exception cref="KeyNotFoundException">When <paramref name="type"/> is not capable of being synchronized.</exception>
		public static SubscriptionType[] GetSubscriptionsByType(Type type) {
			if (type == null) {
				throw new ArgumentNullException(nameof(type), $"{nameof(type)} cannot be null");
			}
			if (!typeof(IRequestable).IsAssignableFrom(type)) {
				throw new InvalidOperationException($"{type.FullName} is not IRequestable");
			}
			switch (type.FullName.Split('.').Last()) {
				#region Company
				case "Company":     /// <seealso cref="Company"/>
					return new[] {
						SubscriptionType.companyGeneral,
						SubscriptionType.companyLabels,
						SubscriptionType.companyPolicies,
						SubscriptionType.companyGeneral,
						SubscriptionType.companyReseller,
					};
				case "CompanyGeneral":   /// <seealso cref="CompanyGeneral"/>
					return new[] {
						SubscriptionType.companyGeneral,
					};
				case "CompanyDirectory": /// <seealso cref="CompanyDirectory"/>
					break;    // not yet implemented
				case "CompanyStyles":    /// <seealso cref="CompanyStyles"/>
					return new[] {
						SubscriptionType.companyLabels,
					};
				case "CompanyPolicies":  /// <seealso cref="CompanyPolicies"/>
					return new[] {
						SubscriptionType.companyPolicies,
					};
				case "CompanyReseller":  /// <seealso cref="CompanyReseller"/>
					return new[] {
						SubscriptionType.companyReseller,
					};
				#endregion Company

				case "Contact":     /// <seealso cref="Contact"/>
					return new[] {
						SubscriptionType.contact,
					};

				#region Billing
				case "BillingProfile":   /// <seealso cref="BillingProfile"/>
					return new[] {
						SubscriptionType.billingProfile,
					};
				case "BillableHostingRule":   /// <seealso cref="BillableHostingRule"/>
					return new[] {
						SubscriptionType.billingHosting,
					};
				case "BillableHostingLicense":     /// <seealso cref="BillableHostingLicense"/>
					return new[] {
						SubscriptionType.billingLicense,
					};
				case "BillingReport":    /// <seealso cref="BillingReport"/>
					return new[] {
						SubscriptionType.billingReport,
					};
				#endregion Billing

				#region Behaviours
				case "Behaviour":   /// <seealso cref="Behaviour"/>
					return new[] {
						SubscriptionType.behaviour,
					};
				case "BehaviourScript":  /// <seealso cref="BehaviourScript"/>
					return new[] {
						SubscriptionType.behaviourScript,
					};
				case "BehaviourLog":     /// <seealso cref="BehaviourLog"/>
					return new[] {
						SubscriptionType.behaviourLog,
					};
				#endregion Behaviours

				#region Assets
				case "Person": /// <seealso cref="Person"/>
				case "Vehicle":     /// <seealso cref="Vehicle"/>
				case "Trailer":     /// <seealso cref="Trailer"/>
				case "Asset":  /// <seealso cref="Asset"/>
					return new[] {
						SubscriptionType.assetGeneral,
						SubscriptionType.assetAdvanced,
						SubscriptionType.assetDispatch,
					};
				case "PersonGeneral":    /// <seealso cref="PersonGeneral"/>
				case "VehicleGeneral":   /// <seealso cref="VehicleGeneral"/>
				case "TrailerGeneral":   /// <seealso cref="TrailerGeneral"/>
				case "AssetGeneral":     /// <seealso cref="AssetGeneral"/>
					return new[] {
						SubscriptionType.assetGeneral,
					};
				case "AssetAdvanced":    /// <seealso cref="AssetAdvanced"/>
				case "VehicleAdvanced":  /// <seealso cref="VehicleAdvanced"/>
					return new[] {
						SubscriptionType.assetAdvanced,
					};
				case "AssetDispatch":    /// <seealso cref="AssetDispatch"/>
					return new[] {
						SubscriptionType.assetDispatch,
					};
				#endregion Assets

				#region Dispatch
				case "DispatchTask":     /// <seealso cref="DispatchTask"/>
					return new[] {
						SubscriptionType.dispatchTask,
					};
				case "DispatchJob": /// <seealso cref="DispatchJob"/>
					return new[] {
						SubscriptionType.dispatchJob,
					};
				#endregion Dispatch

				#region Reports
				case "ReportTemplate":   /// <seealso cref="ReportTemplate"/>
					return new[] {
						SubscriptionType.reportTemplate,
					};
				case "ReportSchedule":   /// <seealso cref="ReportSchedule"/>
					return new[] {
						SubscriptionType.reportSchedule,
					};
				case "ReportResult":     /// <seealso cref="ReportResult"/>
					return new[] {
						SubscriptionType.reportResult,
					};
				#endregion Reports

				#region Places
				case "Place":  /// <seealso cref="Place"/>
				case "PlaceGeneral":     /// <seealso cref="PlaceGeneral"/>
					return new[] {
						SubscriptionType.placeGeneral,
					};
				#endregion Places

				#region Images and Files
				case "Icon":   /// <seealso cref="Icon"/>
					return new[] {
						SubscriptionType.icon,
					};
				case "Picture":     /// <seealso cref="Picture"/>
					return new[] {
						SubscriptionType.picture,
					};
				case "Document":    /// <seealso cref="Document"/>
					return new[] {
						SubscriptionType.document,
					};
				case "FormTemplate":     /// <seealso cref="FormTemplate"/>
					return new[] {
						SubscriptionType.formTemplate,
					};
				case "FormResult":  /// <seealso cref="FormResult"/>
					return new[] {
						SubscriptionType.formResult,
					};
				case "DashcamData": /// <seealso cref="DashcamData"/>
					break;// these can't be kept in sync
				#endregion Images and Files

				#region Maintenance
				case "MaintenanceJob":   /// <seealso cref="MaintenanceJob"/>
					return new[] {
						SubscriptionType.maintenanceJob,
					};
				case "MaintenanceSchedule":   /// <seealso cref="MaintenanceSchedule"/>
					return new[] {
						SubscriptionType.maintenanceSchedule,
					};
				#endregion Maintenance

				#region Providers and Configs
				case "Provider":    /// <seealso cref="Provider"/>
					return new[] {
						SubscriptionType.providerGeneral,
						SubscriptionType.providerAdvanced,
						SubscriptionType.providerControl,
					};
				case "ProviderGeneral":  /// <seealso cref="ProviderGeneral"/>
					return new[] {
						SubscriptionType.providerGeneral,
					};
				case "ProviderAdvanced": /// <seealso cref="ProviderAdvanced"/>
					return new[] {
						SubscriptionType.providerAdvanced,
					};
				case "ProviderControl":  /// <seealso cref="ProviderControl"/>
					return new[] {
						SubscriptionType.providerControl,
					};
				case "ProviderRegistration":  /// <seealso cref="ProviderRegistration"/>
					return new[] {
						SubscriptionType.providerRegistration,
					};

				case "ProviderScript":   /// <seealso cref="ProviderScript"/>
					return new[] {
						SubscriptionType.providerScript,
					};
				case "ProviderConfig":   /// <seealso cref="ProviderConfig"/>
					return new[] {
						SubscriptionType.providerConfig,
					};
				case "ProviderConfigurationType":  /// <seealso cref="ProviderConfigurationType"/>
					break;    // static data, does not need sync
				case "ProviderConfiguration": /// <seealso cref="ProviderConfiguration"/>
					return new[] {
						SubscriptionType.providerConfiguration,
					};
				#endregion Providers and Configs

				case "AssetAlert":  /// <seealso cref="AssetAlert"/>
					break;	// these are always sent to connected clients
				case "AssetMessage":     /// <seealso cref="AssetMessage"/>
					return new[] {
						SubscriptionType.assetMessage,
					};

				#region Users and Groups
				case "User":   /// <seealso cref="User"/>
					return new[] {
						SubscriptionType.userGeneral,
						SubscriptionType.userAdvanced,
					};
				case "UserGeneral": /// <seealso cref="UserGeneral"/>
					return new[] {
						SubscriptionType.userGeneral,
					};
				case "UserAdvanced":     /// <seealso cref="UserAdvanced"/>
					return new[] {
						SubscriptionType.userAdvanced,
					};
				case "UserGroup":   /// <seealso cref="UserGroup"/>
					return new[] {
						SubscriptionType.userGroup,
					};
				case "Machine":     /// <seealso cref="Machine"/>
					return new[] {
						SubscriptionType.machine,
					};
				#endregion Users and Groups

				case "Session":     /// <seealso cref="Session"/>
					break;// these can't be kept in sync
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
	}
}