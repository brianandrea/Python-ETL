﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Smartstore.Admin.Models.Modularity;
using Smartstore.Admin.Models.Stores;
using Smartstore.ComponentModel;
using Smartstore.Core.Localization;
using Smartstore.Core.Security;
using Smartstore.Core.Stores;
using Smartstore.Engine.Modularity;
using Smartstore.Licensing;
using Smartstore.Utilities.Html;
using Smartstore.Web.Controllers;

namespace Smartstore.Admin.Controllers
{
    public class ModuleController : AdminController
    {
        private readonly IModuleCatalog _moduleCatalog;
        private readonly ILanguageService _languageService;
        private readonly IXmlResourceManager _xmlResourceManager;
        private readonly IProviderManager _providerManager;
        private readonly ModuleManager _moduleManager;

        public ModuleController(
            IModuleCatalog moduleCatalog,
            ILanguageService languageService,
            IXmlResourceManager xmlResourceManager,
            IProviderManager providerManager,
            ModuleManager moduleManager)
        {
            _moduleCatalog = moduleCatalog;
            _languageService = languageService;
            _xmlResourceManager = xmlResourceManager;
            _providerManager = providerManager;
            _moduleManager = moduleManager;
        }

        #region Modules

        public IActionResult Index()
        {
            return RedirectToAction("List");
        }

        [Permission(Permissions.Configuration.Module.Read)]
        public async Task<ActionResult> List()
        {
            var model = await PrepareLocalModulesModel();
            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Module.Install)]
        public async Task<ActionResult> ExecuteTasks(IEnumerable<string> modulesToInstall, IEnumerable<string> modulesToUninstall)
        {
            try
            {
                var numTasks = 0;

                // Uninstall first.
                if (modulesToUninstall != null && modulesToUninstall.Any())
                {
                    foreach (var name in modulesToUninstall)
                    {
                        var descriptor = _moduleCatalog.GetModuleByName(name);
                        if (descriptor != null)
                        {
                            var instance = _moduleManager.CreateInstance(descriptor);
                            await instance.UninstallAsync();
                            numTasks++;
                        }
                    }
                }

                // Now execute installations (on next app start))
                if (modulesToInstall != null && modulesToInstall.Any())
                {
                    ModularState.Instance.PendingModules.AddRange(modulesToInstall);
                    ModularState.Instance.Save();
                    numTasks += modulesToInstall.Count();
                }

                // Restart application.
                if (numTasks > 0)
                {
                    return RedirectToAction("RestartApplication", "Common", new { returnUrl = Url.Action("List") });
                }
            }
            catch (Exception ex)
            {
                NotifyError(ex);
            }

            return RedirectToAction("List");
        }

        [Permission(Permissions.Configuration.Module.Read)]
        public IActionResult ReloadList()
        {
            return RedirectToAction("RestartApplication", "Common", new { returnUrl = Url.Action("List") });
        }

        [Permission(Permissions.Configuration.Module.Read)]
        public async Task <IActionResult> ConfigureModule(string systemName)
        {
            var descriptor = _moduleCatalog.GetModuleByName(systemName);
            if (descriptor == null || !descriptor.Module.IsConfigurable)
            {
                return NotFound();
            }

            var model = await PrepareModuleModelAsync(descriptor, false);
            var route = model.ConfigurationRoute;

            if (route == null)
            {
                return NotFound();
            }

            return RedirectToAction(route.Action, route.Controller, route.RouteValues);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Module.Update)]
        public async Task<IActionResult> SetSelectedStores(string systemName, int[] storeIds)
        {
            try
            {
                var descriptor = _moduleCatalog.GetModuleByName(systemName, false);
                if (descriptor == null)
                {
                    return NotFound("The plugin does not exist.");
                }

                var settingKey = descriptor.GetSettingKey("LimitedToStores");

                if (storeIds.Length > 0)
                {
                    await Services.Settings.ApplySettingAsync(settingKey, string.Join(',', storeIds));
                }
                else
                {
                    await Services.Settings.RemoveSettingAsync(settingKey);
                }

                await Services.DbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(501, ex.Message);
            }

            return StatusCode(200);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Module.Update)]
        public async Task<IActionResult> UpdateStringResources(string systemName)
        {
            var moduleDescriptor = _moduleCatalog.GetModuleByName(systemName);

            var success = false;
            var message = "";
            if (moduleDescriptor == null)
            {
                message = T("Admin.Configuration.Plugins.Resources.UpdateFailure").Value;
            }
            else
            {
                await _xmlResourceManager.ImportModuleResourcesFromXmlAsync(moduleDescriptor, null, false);

                success = true;
                message = T("Admin.Configuration.Plugins.Resources.UpdateSuccess").Value;
            }

            return Json(new
            {
                Success = success,
                Message = message
            });
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Module.Update)]
        public async Task<IActionResult> UpdateAllStringResources()
        {
            var moduleDescriptors = _moduleCatalog.Modules;

            foreach (var descriptor in moduleDescriptors)
            {
                if (descriptor.IsInstalled())
                {
                    await _xmlResourceManager.ImportModuleResourcesFromXmlAsync(descriptor, null, false);
                }
                else
                {
                    await Services.Localization.DeleteLocaleStringResourcesAsync(descriptor.ResourceRootKey);
                }
            }

            return Json(new
            {
                Success = true,
                Message = T("Admin.Configuration.Plugins.Resources.UpdateSuccess").Value
            });
        }

        #endregion

        #region Licensing

        #endregion

        #region Utilities

        private bool IsLicensable(IModuleDescriptor moduleDescriptor)
        {
            try
            {
                return LicenseChecker.IsLicensableModule(moduleDescriptor);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }

            return false;
        }

        private async Task<LicensingData> PrepareLicenseLabelModelAsync(LicenseLabelModel model, IModuleDescriptor moduleDescriptor, string url = null)
        {
            if (!IsLicensable(moduleDescriptor))
            {
                return null;
            }

            // We always show license button to serve ability to delete a key.
            model.IsLicensable = true;
            model.LicenseUrl = Url.Action("LicenseModule", new { systemName = moduleDescriptor.SystemName });

            var cachedLicense = await LicenseChecker.GetLicenseAsync(moduleDescriptor.SystemName, url);
            if (cachedLicense == null)
            {
                // Licensed module has not been used yet -> Check state.
                model.LicenseState = await LicenseChecker.CheckStateAsync(moduleDescriptor.SystemName, url);

                // And try to get license data again.
                cachedLicense = await LicenseChecker.GetLicenseAsync(moduleDescriptor.SystemName, url);
            }

            if (cachedLicense != null)
            {
                // Licensed module has been used.
                model.LicenseState = cachedLicense.State;
                model.TruncatedLicenseKey = cachedLicense.TruncatedLicenseKey;
                model.RemainingDemoUsageDays = cachedLicense.RemainingDemoDays;
            }
            else
            {
                // It's confusing to display a license state when there is no license data yet.
                model.HideLabel = true;
            }

            return cachedLicense;
        }

        protected async Task<ModuleModel> PrepareModuleModelAsync(IModuleDescriptor descriptor, bool forList = true)
        {
            var model = await MapperFactory.MapAsync<IModuleDescriptor, ModuleModel>(descriptor, parameters: null);

            // Using GetResource because T could fallback to NullLocalizer here.
            model.Group = Services.Localization.GetResource("Admin.Plugins.KnownGroup." + descriptor.Group);

            model.Installed = descriptor.IsInstalled();
            model.IsConfigurable = descriptor.Module?.IsConfigurable == true;

            if (forList)
            {
                model.FriendlyName = _moduleManager.GetLocalizedValue(descriptor, x => x.FriendlyName);
                model.Description = _moduleManager.GetLocalizedValue(descriptor, x => x.Description);
            }
            
            // Locales
            AddLocales(model.Locales, (locale, languageId) =>
            {
                locale.FriendlyName = _moduleManager.GetLocalizedValue(descriptor, x => x.FriendlyName, languageId, false);
                locale.Description = _moduleManager.GetLocalizedValue(descriptor, x => x.Description, languageId, false);
            });

            // Stores
            model.SelectedStoreIds = (await Services.Settings.GetSettingByKeyAsync<string>(descriptor.GetSettingKey("LimitedToStores"))).ToIntArray();

            // Icon
            model.IconUrl = _moduleManager.GetIconUrl(descriptor);

            if (model.Installed)
            {
                if (descriptor.Module.IsConfigurable)
                {
                    // Specify configuration URL only when a module is already installed
                    model.ConfigurationUrl = Url.Action("ConfigureModule", new { systemName = descriptor.SystemName });

                    if (!forList)
                    {
                        var configurable = _moduleManager.CreateInstance(descriptor) as IConfigurable;
                        model.ConfigurationRoute = configurable.GetConfigurationRoute();
                    }
                }

                // License label
                await PrepareLicenseLabelModelAsync(model.LicenseLabel, descriptor);
            }

            return model;
        }

        protected async Task<LocalModulesModel> PrepareLocalModulesModel()
        {
            var modules = await _moduleCatalog.Modules
                .OrderBy(p => p.Group, ModuleDescriptor.KnownGroupComparer)
                .ThenBy(p => p.Order)
                .SelectAsync(x => PrepareModuleModelAsync(x))
                .AsyncToList();

            var model = new LocalModulesModel
            {
                AvailableStores = await Services.StoreContext
                    .GetAllStores()
                    .SelectAsync(s => MapperFactory.MapAsync<Store, StoreModel>(s, parameters: null))
                    .AsyncToList()
            };

            var groupedModules = from p in modules
                                 group p by p.Group into g
                                 select g;

            foreach (var group in groupedModules)
            {
                foreach (var module in group)
                {
                    model.Groups.Add(group.Key, module);
                }
            }

            return model;
        }

        #endregion

        #region Licensing

        [Permission(Permissions.Configuration.Module.License)]
        public async Task<IActionResult> LicenseModule(string systemName, string licenseKey)
        {
            var descriptor = _moduleCatalog.GetModuleByName(systemName);
            if (descriptor == null || !descriptor.IsInstalled())
            {
                return Content(T("Admin.Common.ResourceNotFound"));
            }

            var isLicensable = LicenseChecker.IsLicensableModule(descriptor, out bool singleLicenseForAllStores);
            if (!isLicensable)
            {
                return Content(T("Admin.Common.ResourceNotFound"));
            }
            
            var stores = Services.StoreContext.GetAllStores();
            var model = new LicenseModuleModel
            {
                SystemName = systemName,
                StoreLicenses = new List<StoreLicenseModel>()
            };

            // Validate store url.
            foreach (var store in stores)
            {
                if (!Services.StoreContext.CurrentStore.IsStoreDataValid())
                {
                    model.InvalidDataStoreId = store.Id;
                    return View(model);
                }
            }

            if (singleLicenseForAllStores)
            {
                var licenseModel = new StoreLicenseModel();
                var license = await PrepareLicenseLabelModelAsync(licenseModel.LicenseLabel, descriptor);
                if (license != null)
                {
                    licenseModel.LicenseKey = license.TruncatedLicenseKey;
                }

                model.StoreLicenses.Add(licenseModel);
            }
            else
            {
                foreach (var store in stores)
                {
                    var licenseModel = new StoreLicenseModel
                    {
                        StoreId = store.Id,
                        StoreName = store.Name,
                        StoreUrl = store.Url
                    };

                    var license = await PrepareLicenseLabelModelAsync(licenseModel.LicenseLabel, descriptor, store.Url);
                    if (license != null)
                    {
                        licenseModel.LicenseKey = license.TruncatedLicenseKey;
                    }

                    model.StoreLicenses.Add(licenseModel);
                }
            }

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Module.License)]
        public async Task<IActionResult> LicenseModule(string systemName, LicenseModuleModel model)
        {
            var descriptor = _moduleCatalog.GetModuleByName(systemName);
            if (descriptor == null || !descriptor.IsInstalled())
            {
                return NotFound();
            }

            var isLicensable = IsLicensable(descriptor);
            if (!isLicensable)
            {
                return NotFound();
            }

            if (model.StoreLicenses != null)
            {
                foreach (var item in model.StoreLicenses)
                {
                    var result = await LicenseChecker.ActivateAsync(item.LicenseKey, descriptor.SystemName, item.StoreUrl);
                    if (result == null)
                    {
                        // Do nothing, skiped.
                    }
                    else if (result.Success)
                    {
                        NotifySuccess(T("Admin.Configuration.Plugins.LicenseActivated"));
                    }
                    else
                    {
                        if (result.IsFailureWarning)
                        {
                            NotifyWarning(result.ToString());
                        }
                        else
                        {
                            NotifyError(result.ToString());
                        }

                        return RedirectToAction("List");
                    }
                }
            }

            return RedirectToAction("List");
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Module.License)]
        public async Task<IActionResult> LicenseResetStatusCheck(string systemName)
        {
            // Reset state for current store.
            var result = await LicenseChecker.ResetStateAsync(systemName);
            LicenseCheckerResult subShopResult = null;

            var model = new LicenseLabelModel
            {
                IsLicensable = true,
                LicenseUrl = Url.Action("LicenseModule", new { systemName = systemName }),
                LicenseState = result.State,
                TruncatedLicenseKey = result.TruncatedLicenseKey,
                RemainingDemoUsageDays = result.RemainingDemoDays
            };

            // Reset state for all other stores.
            if (result.Success)
            {
                var currentStoreId = Services.StoreContext.CurrentStore.Id;
                var allStores = Services.StoreContext.GetAllStores();

                foreach (var store in allStores.Where(x => x.Id != currentStoreId && x.Url.HasValue()))
                {
                    subShopResult = await LicenseChecker.ResetStateAsync(systemName, store.Url);
                    if (!subShopResult.Success)
                    {
                        result = subShopResult;
                        break;
                    }
                }
            }
            
            // Notify about result.
            if (result.Success)
            {
                NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
            }
            else
            {
                var message = HtmlUtility.ConvertPlainTextToHtml(result.ToString());
                if (result.IsFailureWarning)
                {
                    NotifyWarning(message);
                }
                else
                {
                    NotifyError(message);
                }
            }

            return PartialView("_LicenseLabel", model);
        }

        #endregion
    }
}