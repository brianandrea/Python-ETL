﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Smartstore.Collections;
using Smartstore.ComponentModel;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Tax.Domain;
using Smartstore.Tax.Models;
using Smartstore.Web.Controllers;
using Smartstore.Web.Models.DataGrid;

namespace Smartstore.Tax.Controllers
{
    [Area("Admin")]
    [Route("[area]/taxbyregion/[action]/{id?}")]
    public class TaxByRegionController : AdminController
    {
        private readonly SmartDbContext _db;
        private readonly ITaxService _taxService;

        public TaxByRegionController(SmartDbContext db, ITaxService taxService)
        {
            _db = db;
            _taxService = taxService;
        }

        private async Task PrepareViewBagAsync()
        {
            var taxCategories = await _db.TaxCategories
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id);

            var countries = await _db.Countries
                .AsNoTracking()
                .ApplyStandardFilter(true)
                .ToDictionaryAsync(x => x.Id);

            var stateProvinces = await _db.StateProvinces
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id);

            var stateProvincesOfFirstCountry = stateProvinces.Values.Where(x => x.CountryId == countries.Values.FirstOrDefault().Id).ToList();

            ViewBag.AvailableTaxCategories = taxCategories.Values.Select(x => new SelectListItem
            {
                Text = x.Name,
                Value = x.Id.ToString()
            })
            .ToList();

            ViewBag.AvailableCountries = countries.Values.Select(x => new SelectListItem
            {
                Text = x.Name,
                Value = x.Id.ToString()
            })
            .ToList();

            ViewBag.AvailableStates = stateProvincesOfFirstCountry.Select(x => new SelectListItem
            {
                Text = x.Name,
                Value = x.Id.ToString()
            })
            .ToList();
            ViewBag.AvailableStates.Insert(0, new SelectListItem { Text = "*", Value = "0" });

            ViewBag.Provider = _taxService.LoadTaxProviderBySystemName("Tax.CountryStateZip").Metadata;
        }

        private async Task<List<ByRegionTaxRateModel>> PrepareModelAsync(IPagedList<TaxRateEntity> taxRates)
        {
            var taxCategories = await _db.TaxCategories
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id);

            var countries = await _db.Countries
                .AsNoTracking()
                .ApplyStandardFilter(true)
                .ToDictionaryAsync(x => x.Id);

            var stateProvinces = await _db.StateProvinces
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id);

            var unavailable = T("Common.Unavailable").Value;

            var taxRateModels = taxRates.Select(x =>
            {
                var m = new ByRegionTaxRateModel
                {
                    Id = x.Id,
                    TaxCategoryId = x.TaxCategoryId,
                    CountryId = x.CountryId,
                    StateProvinceId = x.StateProvinceId,
                    Zip = x.Zip.HasValue() ? x.Zip : "*",
                    Percentage = x.Percentage
                };

                // TODO: (mh) (core) You don't need following stuff with navigation properties which you include in your query.
                taxCategories.TryGetValue(x.TaxCategoryId, out TaxCategory tc);
                m.TaxCategoryName = tc?.Name.EmptyNull();

                countries.TryGetValue(x.CountryId, out Country c);
                m.CountryName = c?.Name ?? unavailable;

                stateProvinces.TryGetValue(x.StateProvinceId, out StateProvince s);
                m.StateProvinceName = s?.Name ?? "*";

                return m;
            })
            .ToList();

            return taxRateModels;
        }

        public IActionResult Index()
        {
            return RedirectToAction(nameof(Configure));
        }

        public async Task<IActionResult> Configure()
        {
            if (!await _db.TaxCategories.AnyAsync())
            {
                NotifyWarning(T("Plugins.Tax.CountryStateZip.NoTaxCategoriesFound"));
            }

            await PrepareViewBagAsync();

            return View();
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Tax.Read)]
        public async Task<IActionResult> List(GridCommand command)
        {
            var taxRates = await _db.TaxRates()
                .AsNoTracking()
                .ApplyStandardFilter(null, null, null, null) // INFO: (mh) (core) Don't violate DRY principle
                .ApplyGridCommand(command, false)
                .ToPagedList(command)
                .LoadAsync();

            var taxRateModels = await PrepareModelAsync(taxRates);

            var gridModel = new GridModel<ByRegionTaxRateModel>
            {
                Rows = taxRateModels,
                Total = await taxRates.GetTotalCountAsync()
            };

            return Json(gridModel);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Tax.Update)]
        public async Task<IActionResult> Update(ByRegionTaxRateModel model)
        {
            var success = false;

            var taxRate = await _db.TaxRates().FindByIdAsync(model.Id);
            taxRate.Zip = model.Zip == "*" ? null : model.Zip;
            taxRate.Percentage = model.Percentage;

            if (taxRate != null)
            {
                await MapperFactory.MapAsync(model, taxRate);
                await _db.SaveChangesAsync();
                success = true;
            }

            return Json(new { success });
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Tax.Delete)]
        public async Task<IActionResult> Delete(GridSelection selection)
        {
            var success = false;
            var numDeleted = 0;
            var ids = selection.GetEntityIds();

            if (ids.Any())
            {
                var taxRates = await _db.TaxRates().GetManyAsync(ids, true);

                _db.TaxRates().RemoveRange(taxRates);

                numDeleted = await _db.SaveChangesAsync();
                success = true;
            }

            return Json(new { Success = success, Count = numDeleted });
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Tax.Create)]
        public async Task<IActionResult> AddTaxByRegionRecord(ByRegionTaxRateModel model)
        {
            _db.TaxRates().Add(new TaxRateEntity
            {
                TaxCategoryId = model.TaxCategoryId,
                CountryId = model.CountryId,
                StateProvinceId = model.StateProvinceId,
                Zip = model.Zip,
                Percentage = model.Percentage
            });

            await _db.SaveChangesAsync();

            NotifySuccess(T("Plugins.Tax.CountryStateZip.AddNewRecord.Success"));

            return Json(new { Result = true });
        }
    }
}