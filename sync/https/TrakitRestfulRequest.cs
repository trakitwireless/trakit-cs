﻿using System;
using System.Collections.Generic;
using trakit.objects;

namespace trakit.https {
	/// <summary>
	/// 
	/// </summary>
	public class TrakitRestfulRequest {
		public static string listByParent<T>(T cacheable) where T : Subscribable, IAmCompany
			=> listByParent(cacheable?.GetType() ?? throw new ArgumentNullException("cacheable"), cacheable.id);
		public static string listByCompany<T>(T cacheable) where T : Subscribable, IBelongCompany
			=> listByCompany(cacheable?.GetType() ?? throw new ArgumentNullException("cacheable"), cacheable.company);
		public static string listByAsset<T>(T cacheable) where T : Subscribable, IBelongAsset
			=> listByAsset(cacheable?.GetType() ?? throw new ArgumentNullException("cacheable"), cacheable.asset);
		public static string listByBillingProfile<T>(T cacheable) where T : Subscribable, IBelongBillingProfile
			=> listByBillingProfile(cacheable?.GetType() ?? throw new ArgumentNullException("cacheable"), cacheable.profile);

		public static string listByParent(Type type, ulong parentId) {
			switch (type?.Name) {
				case "CompanyGeneral":
					return $"/companies/{parentId}/tree";
			}
			throw new KeyNotFoundException($"{type?.FullName} cannot be listed by parent Company");
		}
		public static string listByCompany(Type type, ulong companyId) {
			switch (type?.Name) {
				#region Company
				case "CompanyGeneral":
					return $"/companies/{companyId}/generals";
				case "CompanyDirectory":
					return $"/companies/{companyId}/directories";
				case "CompanyStyles":
					return $"/companies/{companyId}/styles";
				case "CompanyPolicies":
					return $"/companies/{companyId}/policies";
				case "CompanyReseller":
					return $"/companies/{companyId}/reseller";
				case "CompanySettings":
					break;// will throw
				#endregion Company

				case "Contact":
					return $"/companies/{companyId}/contacts";

				#region Billing
				case "BillingProfile":
					return $"/companies/{companyId}/billing/profiles";
				#endregion Billing

				#region Behaviours
				case "Behaviour":
					return $"/companies/{companyId}/behaviours";
				case "BehaviourScript":
					return $"/companies/{companyId}/behaviours/scripts";
				case "BehaviourLog":
					return $"/companies/{companyId}/behaviours/logs";
				#endregion Behaviours

				#region Assets
				case "Person":
				case "Vehicle":
				case "Trailer":
				case "Asset":
					return $"/companies/{companyId}/assets";
				case "PersonGeneral":
				case "VehicleGeneral":
				case "TrailerGeneral":
				case "AssetGeneral":
					return $"/companies/{companyId}/assets/generals";
				case "AssetAdvanced":
				case "VehicleAdvanced":
					return $"/companies/{companyId}/assets/advanceds";
				case "AssetDispatch":
					return $"/companies/{companyId}/assets/dispatches";
				#endregion Assets

				#region Messages
				case "AssetAlert":
				case "AssetMessage":
					return $"/companies/{companyId}/assets/messages";
				#endregion Messages

				#region Dispatch
				case "DispatchJob":
					return $"/companies/{companyId}/assets/dispatch/jobs";
				case "DispatchTask":
					return $"/companies/{companyId}/assets/dispatch/tasks";
				#endregion Dispatch

				#region Reports
				case "ReportTemplate":
					return $"/companies/{companyId}/reports/templates";
				case "ReportSchedule":
					return $"/companies/{companyId}/reports/schedules";
				case "ReportResult":
					return $"/companies/{companyId}/reports/results";
				#endregion Reports

				#region Places
				case "Place":
				case "PlaceGeneral":
					return $"/companies/{companyId}/places";
				#endregion Places

				#region Images and Files
				case "Icon":
					return $"/companies/{companyId}/icons";
				case "Picture":
					return $"/companies/{companyId}/pictures";
				case "Document":
					return $"/companies/{companyId}/documents";
				case "FormTemplate":
					return $"/companies/{companyId}/forms/templates";
				case "FormResult":
					return $"/companies/{companyId}/forms";
				case "DashcamData":
					return $"/companies/{companyId}/dashcams";
				case "DashcamDataLive":
					return $"/companies/{companyId}/dashcams/live";
				#endregion Images and Files

				#region Maintenance
				case "MaintenanceJob":
					return $"/companies/{companyId}/maintenance/jobs";
				case "MaintenanceSchedule":
					return $"/companies/{companyId}/maintenance/schedules";
				#endregion Maintenance

				#region Providers and Configs
				case "Provider":
					return $"/companies/{companyId}/providers";
				case "ProviderGeneral":
					return $"/companies/{companyId}/providers/generals";
				case "ProviderAdvanced":
					return $"/companies/{companyId}/providers/advanceds";
				case "ProviderControl":
					return $"/companies/{companyId}/providers/controls";
				case "ProviderRegistration":
					return $"/companies/{companyId}/providers/registrations";


				case "ProviderScript":
					return $"/companies/{companyId}/providers/scripts";
				case "ProviderConfig":
					return $"/companies/{companyId}/providers/configs";
				case "ProviderConfiguration":
					return $"/companies/{companyId}/providers/configurations";
				case "ProviderConfigurationType":
					return "/providers/configurations/types";
				#endregion Providers and Configs

				#region Users and Groups
				case "User":
					return $"/companies/{companyId}/users";
				case "UserGeneral":
					return $"/companies/{companyId}/users/generals";
				case "UserAdvanced":
					return $"/companies/{companyId}/users/advanceds";
				case "UserGroup":
					return $"/companies/{companyId}/users/groups";
				case "Machine":
					return $"/companies/{companyId}/machines";
					#endregion Users and Groups
			}
			throw new KeyNotFoundException($"{type?.FullName} cannot be listed by Company");
		}
		public static string listByAsset(Type type, ulong assetId) {
			switch (type?.Name) {
				case "AssetAlert":
				case "AssetMessage":
					return $"/assets/{assetId}/messages";

				case "BehaviourLog":
					return $"/behaviours/log?asset={assetId}";

				case "DispatchJob":
					return $"/assets/{assetId}/assets/dispatch/jobs";
				case "DispatchTask":
					return $"/assets/{assetId}/assets/dispatch/tasks";

				case "Picture":
					return $"/assets/{assetId}/pictures";
				case "Document":
					return $"/assets/{assetId}/documents";
				case "FormResult":
					return $"/assets/{assetId}/forms";
				case "DashcamData":
					return $"/assets/{assetId}/dashcams";
				case "DashcamDataLive":
					return $"/assets/{assetId}/dashcams/live";

				case "MaintenanceJob":
					return $"/assets/{assetId}/maintenance/jobs";
			}
			throw new KeyNotFoundException($"{type?.FullName} cannot be listed by Asset");
		}
		public static string listByBehaviour(Type type, ulong behaviourId) {
			switch (type?.Name) {
				case "BehaviourLog":
					return $"/behaviours/{behaviourId}/log";
			}
			throw new KeyNotFoundException($"{type?.FullName} cannot be listed by Behaviour");
		}
		public static string listByBehaviourScript(Type type, ulong scriptId) {
			switch (type?.Name) {
				case "BehaviourLog":
					return $"/behaviours/scripts/{scriptId}/log";
			}
			throw new KeyNotFoundException($"{type?.FullName} cannot be listed by BehaviourScript");
		}
		public static string listByBillingProfile(Type type, ulong profileId) {
			switch (type?.Name) {
				case "BillableHostingRule":
					return $"/billing/profiles/{profileId}/rules";
				case "BillableHostingLicense":
					return $"/billing/profiles/{profileId}/licenses";
				case "BillingReport":
					return $"/billing/profiles/{profileId}/reports";
			}
			throw new KeyNotFoundException($"{type?.FullName} cannot be listed by BillingProfile");
		}
	}
}