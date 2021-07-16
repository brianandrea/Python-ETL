﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text;
using System.Threading.Tasks;
using Dasync.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Smartstore.Admin.Models.Catalog;
using Smartstore.ComponentModel;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Localization;
using Smartstore.Core.Logging;
using Smartstore.Core.Rules.Filters;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Data.Batching;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling.DataGrid;

namespace Smartstore.Admin.Controllers
{
    public partial class ProductController : AdminControllerBase
    {
        #region Product specification attributes

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> ProductSpecAttrList(GridCommand command, int productId)
        {
            var model = new GridModel<ProductSpecificationAttributeModel>();
            var productSpecAttributes = await _db.ProductSpecificationAttributes
                .AsNoTracking()
                .ApplyProductsFilter(new[] { productId })
                .ApplyGridCommand(command)
                .Include(x => x.SpecificationAttributeOption)
                .ThenInclude(x => x.SpecificationAttribute)
                //.OrderBy(x => x.DisplayOrder)
                .ToListAsync();

            var specAttributeIds = productSpecAttributes.Select(x => x.SpecificationAttributeOption.SpecificationAttributeId).ToArray();
            var options = await _db.SpecificationAttributeOptions
                .AsNoTracking()
                .Where(x => specAttributeIds.Contains(x.SpecificationAttributeId))
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync();

            var specOptions = options.ToMultimap(x => x.SpecificationAttributeId, x => x);

            var productSpecModel = productSpecAttributes
                .Select(x =>
                {
                    var attributeId = x.SpecificationAttributeOption.SpecificationAttributeId;
                    var psaModel = new ProductSpecificationAttributeModel
                    {
                        Id = x.Id,
                        SpecificationAttributeName = x.SpecificationAttributeOption.SpecificationAttribute.Name,
                        SpecificationAttributeOptionName = x.SpecificationAttributeOption.Name,
                        SpecificationAttributeId = attributeId,
                        SpecificationAttributeOptionId = x.SpecificationAttributeOptionId,
                        AllowFiltering = x.AllowFiltering,
                        ShowOnProductPage = x.ShowOnProductPage,
                        DisplayOrder = x.DisplayOrder
                    };

                    if (specOptions.ContainsKey(attributeId))
                    {
                        psaModel.SpecificationAttributeOptionsUrl = Url.Action("GetOptionsByAttributeId", "SpecificationAttribute", new { attributeId = attributeId });
                    }

                    return psaModel;
                })
                .ToList();

            model.Rows = productSpecModel;
            model.Total = productSpecModel.Count;

            return Json(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditAttribute)]
        public async Task<IActionResult> ProductSpecificationAttributeAdd(
            int specificationAttributeOptionId,
            bool? allowFiltering,
            bool? showOnProductPage,
            int displayOrder,
            int productId)
        {
            var success = false;
            var message = string.Empty;

            var psa = new ProductSpecificationAttribute
            {
                SpecificationAttributeOptionId = specificationAttributeOptionId,
                ProductId = productId,
                AllowFiltering = allowFiltering,
                ShowOnProductPage = showOnProductPage,
                DisplayOrder = displayOrder,
            };

            try
            {
                _db.ProductSpecificationAttributes.Add(psa);
                await _db.SaveChangesAsync();
                success = true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            return Json(new { success, message });
        }

        [Permission(Permissions.Catalog.Product.EditAttribute)]
        public async Task<IActionResult> ProductSpecAttrUpdate(ProductSpecificationAttributeModel model)
        {
            var psa = await _db.ProductSpecificationAttributes.FindByIdAsync(model.Id);

            psa.AllowFiltering = model.AllowFiltering ?? false;
            psa.ShowOnProductPage = model.ShowOnProductPage ?? false;
            psa.DisplayOrder = model.DisplayOrder;
            psa.SpecificationAttributeOptionId = model.SpecificationAttributeOptionId;

            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        [Permission(Permissions.Catalog.Product.EditAttribute)]
        public async Task<IActionResult> ProductSpecAttrDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                numDeleted = await _db.ProductSpecificationAttributes
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .BatchDeleteAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        #endregion

        #region Product variant attributes

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> ProductVariantAttributeList(GridCommand command, int productId)
        {
            var model = new GridModel<ProductModel.ProductVariantAttributeModel>();
            var productVariantAttributes = await _db.ProductVariantAttributes
                .ApplyProductFilter(new[] { productId })
                .ApplyGridCommand(command)
                .ToListAsync();

            var productVariantAttributesModel = await productVariantAttributes
                .SelectAsync(async x =>
                {
                    var attr = await _db.ProductAttributes.FindByIdAsync(x.ProductAttributeId);

                    var pvaModel = new ProductModel.ProductVariantAttributeModel
                    {
                        Id = x.Id,
                        ProductId = x.ProductId,
                        ProductAttribute = attr.Name,
                        ProductAttributeId = x.ProductAttributeId,
                        TextPrompt = x.TextPrompt,
                        CustomData = x.CustomData,
                        IsRequired = x.IsRequired,
                        AttributeControlType = await x.AttributeControlType.GetLocalizedEnumAsync(),
                        AttributeControlTypeId = x.AttributeControlTypeId,
                        DisplayOrder1 = x.DisplayOrder
                    };

                    if (x.ShouldHaveValues())
                    {
                        pvaModel.ValueCount = x.ProductVariantAttributeValues != null ? x.ProductVariantAttributeValues.Count : 0;
                        pvaModel.EditUrl = Url.Action("EditAttributeValues", "Product", new { productVariantAttributeId = x.Id });
                        pvaModel.EditText = T("Admin.Catalog.Products.ProductVariantAttributes.Attributes.Values.ViewLink", pvaModel.ValueCount);

                        if (x.ProductAttribute.ProductAttributeOptionsSets.Any())
                        {
                            var optionsSets = new StringBuilder($"<option>{T("Admin.Catalog.Products.ProductVariantAttributes.Attributes.Values.CopyOptions")}</option>");
                            pvaModel.OptionSets.Add(new { Id = "", Name = T("Admin.Catalog.Products.ProductVariantAttributes.Attributes.Values.CopyOptions").Value });
                            x.ProductAttribute.ProductAttributeOptionsSets.Each(set => {
                                pvaModel.OptionSets.Add(new { set.Id, set.Name });
                            });
                        }
                    }

                    return pvaModel;
                })
                .AsyncToList();

            model.Rows = productVariantAttributesModel;
            model.Total = productVariantAttributesModel.Count;

            return Json(model);
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductVariantAttributeInsert(ProductModel.ProductVariantAttributeModel model, int productId)
        {
            // TODO: (mh) (core) Throws if no attribute was selected (also in classic code). Fix it!

            var pva = new ProductVariantAttribute
            {
                ProductId = productId,
                ProductAttributeId = model.ProductAttributeId,
                TextPrompt = model.TextPrompt,
                CustomData = model.CustomData,
                IsRequired = model.IsRequired,
                AttributeControlTypeId = model.AttributeControlTypeId,
                DisplayOrder = model.DisplayOrder1
            };

            try
            {
                _db.ProductVariantAttributes.Add(pva);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Services.Notifier.Error(ex.Message);
                return Json(new { success = false });
            }

            return Json(new { success = true });
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductVariantAttributeUpdate(ProductModel.ProductVariantAttributeModel model)
        {
            var pva = await _db.ProductVariantAttributes.FindByIdAsync(model.Id);

            pva.ProductAttributeId = model.ProductAttributeId;
            pva.TextPrompt = model.TextPrompt;
            pva.CustomData = model.CustomData;
            pva.IsRequired = model.IsRequired;
            pva.AttributeControlTypeId = model.AttributeControlTypeId;
            pva.DisplayOrder = model.DisplayOrder1;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                NotifyError(ex.Message);
                return Json(new { success = false });
            }

            return Json(new { success = true });
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductVariantAttributeDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                numDeleted = await _db.ProductVariantAttributes
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .BatchDeleteAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> CopyAttributeOptions(int productVariantAttributeId, int optionsSetId, bool deleteExistingValues)
        {
            var pva = await _db.ProductVariantAttributes.FindByIdAsync(productVariantAttributeId, false);

            if (pva == null)
            {
                NotifyError(T("Products.Variants.NotFound", productVariantAttributeId));
            }
            else
            {
                try
                {
                    var numberOfCopiedOptions = await _productAttributeService.CopyAttributeOptionsAsync(pva, optionsSetId, deleteExistingValues);

                    NotifySuccess(string.Concat(T("Admin.Common.TaskSuccessfullyProcessed"), " ",
                        T("Admin.Catalog.Products.ProductVariantAttributes.Attributes.Values.NumberOfCopiedOptions", numberOfCopiedOptions)));
                }
                catch (Exception ex)
                {
                    NotifyError(ex.Message);
                }
            }

            return Json(string.Empty);
        }

        #endregion

        #region Product variant attribute values

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> ProductAttributeValueList(int productVariantAttributeId, GridCommand command)
        {
            var gridModel = new GridModel<ProductModel.ProductVariantAttributeValueModel>();
            var pva = await _db.ProductVariantAttributes.FindByIdAsync(productVariantAttributeId, false);
            var values = await _db.ProductVariantAttributeValues
                .AsNoTracking()
                .ApplyProductAttributeFilter(productVariantAttributeId)
                .ApplyGridCommand(command)
                .ToListAsync();

            gridModel.Rows = await values.SelectAsync(async x =>
            {
                var linkedProduct = await _db.Products.FindByIdAsync(x.LinkedProductId);

                var model = new ProductModel.ProductVariantAttributeValueModel
                {
                    Id = x.Id,
                    ProductVariantAttributeId = x.ProductVariantAttributeId,
                    Name = x.Name,
                    NameString = (x.Color.IsEmpty() ? x.Name : $"{x.Name} - {x.Color}").HtmlEncode(),
                    Alias = x.Alias,
                    Color = x.Color,
                    HasColor = !x.Color.IsEmpty(),
                    PictureId = x.MediaFileId,
                    PriceAdjustment = x.PriceAdjustment,
                    WeightAdjustment = x.WeightAdjustment,
                    PriceAdjustmentString = x.ValueType == ProductVariantAttributeValueType.Simple ? x.PriceAdjustment.ToString("G29") : string.Empty,
                    WeightAdjustmentString = x.ValueType == ProductVariantAttributeValueType.Simple ? x.WeightAdjustment.ToString("G29") : string.Empty,
                    IsPreSelected = x.IsPreSelected,
                    DisplayOrder = x.DisplayOrder,
                    ValueTypeId = x.ValueTypeId,
                    TypeName = await x.ValueType.GetLocalizedEnumAsync(),
                    TypeNameClass = x.ValueType == ProductVariantAttributeValueType.ProductLinkage ? "fa fa-link mr-2" : "d-none hide hidden-xs-up",
                    LinkedProductId = x.LinkedProductId,
                    Quantity = x.Quantity
                };

                if (linkedProduct != null)
                {
                    model.LinkedProductName = linkedProduct.GetLocalized(p => p.Name);
                    model.LinkedProductTypeName = linkedProduct.GetProductTypeLabel(_localizationService);
                    model.LinkedProductTypeLabelHint = linkedProduct.ProductTypeLabelHint;
                    model.LinkedProductEditUrl = Url.Action("Edit", "Product", new { id = linkedProduct.Id });

                    if (model.Quantity > 1)
                    {
                        model.QuantityInfo = $" × {model.Quantity}";
                    }
                }

                return model;
            }).AsyncToList();

            gridModel.Total = values.Count;

            return Json(gridModel);
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> EditAttributeValues(int productVariantAttributeId)
        {
            var pva = await _db.ProductVariantAttributes
                .Include(x => x.ProductAttribute)
                .FindByIdAsync(productVariantAttributeId, false);

            if (pva == null)
                throw new ArgumentException(T("Products.Variants.NotFound", productVariantAttributeId));

            var product = await _db.Products.FindByIdAsync(pva.ProductId, false);
            if (product == null)
                throw new ArgumentException(T("Products.NotFound", pva.ProductId));

            var model = new ProductModel.ProductVariantAttributeValueListModel
            {
                ProductName = product.Name,
                ProductId = pva.ProductId,
                ProductVariantAttributeName = pva.ProductAttribute.Name,
                ProductVariantAttributeId = pva.Id
            };

            return View(model);
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductAttributeValueCreatePopup(string btnId, string formId, int productVariantAttributeId)
        {
            var pva = await _db.ProductVariantAttributes.FindByIdAsync(productVariantAttributeId, false);
            if (pva == null)
                throw new ArgumentException(T("Products.Variants.NotFound", productVariantAttributeId));

            var model = new ProductModel.ProductVariantAttributeValueModel
            {
                ProductId = pva.ProductId,
                ProductVariantAttributeId = productVariantAttributeId,
                IsListTypeAttribute = pva.IsListTypeAttribute(),
                Color = string.Empty,
                Quantity = 1
            };

            AddLocales(model.Locales);

            ViewBag.btnId = btnId;
            ViewBag.formId = formId;

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductAttributeValueCreatePopup(string btnId, string formId, ProductModel.ProductVariantAttributeValueModel model)
        {
            var pva = await _db.ProductVariantAttributes.FindByIdAsync(model.ProductVariantAttributeId);
            if (pva == null)
            {
                return RedirectToAction("List", "Product");
            }

            if (model.ValueTypeId == (int)ProductVariantAttributeValueType.ProductLinkage && await IsBundleItemAsync(pva.ProductId))
            {
                var product = await _db.Products.FindByIdAsync(pva.ProductId, false);
                var productName = product?.Name.NaIfEmpty();

                ModelState.AddModelError(string.Empty, T("Admin.Catalog.Products.BundleItems.NoProductLinkageForBundleItem", productName));
            }

            if (ModelState.IsValid)
            {
                var pvav = new ProductVariantAttributeValue();
                MiniMapper.Map(model, pvav);
                pvav.MediaFileId = model.PictureId;
                pvav.LinkedProductId = pvav.ValueType == ProductVariantAttributeValueType.Simple ? 0 : model.LinkedProductId;

                try
                {
                    _db.ProductVariantAttributeValues.Add(pvav);
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    return View(model);
                }

                try
                {
                    await UpdateLocalesAsync(pvav, model);
                }
                catch { }

                ViewBag.RefreshPage = true;
                ViewBag.btnId = btnId;
                ViewBag.formId = formId;
                return View(model);
            }

            // If we got this far something failed. Redisplay form!
            return View(model);
        }

        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> ProductAttributeValueEditPopup(string btnId, string formId, int id)
        {
            var pvav = await _db.ProductVariantAttributeValues
                .Include(x => x.ProductVariantAttribute)
                .FindByIdAsync(id, false);

            if (pvav == null)
            {
                return RedirectToAction("List", "Product");
            }

            var linkedProduct = await _db.Products.FindByIdAsync(pvav.LinkedProductId, false);

            var model = new ProductModel.ProductVariantAttributeValueModel
            {
                ProductId = pvav.ProductVariantAttribute.ProductId,
                ProductVariantAttributeId = pvav.ProductVariantAttributeId,
                Name = pvav.Name,
                Alias = pvav.Alias,
                Color = pvav.Color,
                PictureId = pvav.MediaFileId,
                IsListTypeAttribute = pvav.ProductVariantAttribute.IsListTypeAttribute(),
                PriceAdjustment = pvav.PriceAdjustment,
                WeightAdjustment = pvav.WeightAdjustment,
                IsPreSelected = pvav.IsPreSelected,
                DisplayOrder = pvav.DisplayOrder,
                ValueTypeId = pvav.ValueTypeId,
                TypeName = await pvav.ValueType.GetLocalizedEnumAsync(),
                TypeNameClass = pvav.ValueType == ProductVariantAttributeValueType.ProductLinkage ? "fa fa-link mr-2" : "d-none hide hidden-xs-up",
                LinkedProductId = pvav.LinkedProductId,
                Quantity = pvav.Quantity
            };

            if (linkedProduct != null)
            {
                model.LinkedProductName = linkedProduct.GetLocalized(p => p.Name);
                model.LinkedProductTypeName = linkedProduct.GetProductTypeLabel(_localizationService);
                model.LinkedProductTypeLabelHint = linkedProduct.ProductTypeLabelHint;
                model.LinkedProductEditUrl = Url.Action("Edit", "Product", new { id = linkedProduct.Id });

                if (model.Quantity > 1)
                {
                    model.QuantityInfo = $" × {model.Quantity}";
                }
            }

            AddLocales(model.Locales, (locale, languageId) =>
            {
                locale.Name = pvav.GetLocalized(x => x.Name, languageId, false, false);
                locale.Alias = pvav.GetLocalized(x => x.Alias, languageId, false, false);
            });

            ViewBag.btnId = btnId;
            ViewBag.formId = formId;

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductAttributeValueEditPopup(string btnId, string formId, ProductModel.ProductVariantAttributeValueModel model)
        {
            var pvav = await _db.ProductVariantAttributeValues.FindByIdAsync(model.Id);
            if (pvav == null)
            {
                return RedirectToAction("List", "Product");
            }

            if (model.ValueTypeId == (int)ProductVariantAttributeValueType.ProductLinkage && await IsBundleItemAsync(pvav.ProductVariantAttribute.ProductId))
            {
                var product = await _db.Products.FindByIdAsync(pvav.ProductVariantAttribute.ProductId, false);
                var productName = product?.Name.NaIfEmpty();

                ModelState.AddModelError(string.Empty, T("Admin.Catalog.Products.BundleItems.NoProductLinkageForBundleItem", productName));
            }

            if (ModelState.IsValid)
            {
                MiniMapper.Map(model, pvav);
                pvav.MediaFileId = model.PictureId;
                pvav.LinkedProductId = pvav.ValueType == ProductVariantAttributeValueType.Simple ? 0 : model.LinkedProductId;

                try
                {
                    await UpdateLocalesAsync(pvav, model);
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    return View(model);
                }

                ViewBag.RefreshPage = true;
                ViewBag.btnId = btnId;
                ViewBag.formId = formId;
            }

            return View(model);
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductAttributeValueDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                numDeleted = await _db.ProductVariantAttributeValues
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .BatchDeleteAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        [NonAction]
        private async Task UpdateLocalesAsync(ProductVariantAttributeValue pvav, ProductModel.ProductVariantAttributeValueModel model)
        {
            foreach (var localized in model.Locales)
            {
                await _localizedEntityService.ApplyLocalizedValueAsync(pvav, x => x.Name, localized.Name, localized.LanguageId);
                await _localizedEntityService.ApplyLocalizedValueAsync(pvav, x => x.Alias, localized.Alias, localized.LanguageId);
            }
        }

        #endregion

        #region Product variant attribute combinations

        private async Task PrepareProductAttributeCombinationModelAsync(
            ProductVariantAttributeCombinationModel model,
            ProductVariantAttributeCombination entity,
            Product product, bool formatAttributes = false)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (product == null)
                throw new ArgumentNullException(nameof(product));

            var baseDimension = await _db.MeasureDimensions.FindByIdAsync(_measureSettings.BaseDimensionId);

            model.ProductId = product.Id;
            model.PrimaryStoreCurrencyCode = _services.StoreContext.CurrentStore.PrimaryStoreCurrency.CurrencyCode;
            model.BaseDimensionIn = baseDimension?.GetLocalized(x => x.Name) ?? string.Empty;

            if (entity == null)
            {
                // It's a new entity, so initialize it properly.
                model.StockQuantity = 10000;
                model.IsActive = true;
                model.AllowOutOfStockOrders = true;
            }

            if (formatAttributes && entity != null)
            {
                model.AttributesXml = await _productAttributeFormatter.FormatAttributesAsync(
                    entity.AttributeSelection,
                    product,
                    _workContext.CurrentCustomer,
                    "<br />",
                    includeHyperlinks: false);
            }
        }

        private async Task PrepareVariantCombinationAttributesAsync(ProductVariantAttributeCombinationModel model, Product product)
        {
            var productVariantAttributes = await _db.ProductVariantAttributes
                .AsNoTracking()
                .Include(x => x.ProductAttribute)
                .ApplyProductFilter(new[] { product.Id })
                .ToListAsync();

            foreach (var attribute in productVariantAttributes)
            {
                var pvaModel = new ProductVariantAttributeCombinationModel.ProductVariantAttributeModel()
                {
                    Id = attribute.Id,
                    ProductAttributeId = attribute.ProductAttributeId,
                    Name = attribute.ProductAttribute.Name,
                    TextPrompt = attribute.TextPrompt,
                    IsRequired = attribute.IsRequired,
                    AttributeControlType = attribute.AttributeControlType
                };

                if (attribute.ShouldHaveValues())
                {
                    var pvaValues = await _db.ProductVariantAttributeValues
                        .AsNoTracking()
                        .ApplyProductAttributeFilter(attribute.Id)
                        .ToListAsync();

                    foreach (var pvaValue in pvaValues)
                    {
                        var pvaValueModel = new ProductVariantAttributeCombinationModel.ProductVariantAttributeValueModel()
                        {
                            Id = pvaValue.Id,
                            Name = pvaValue.Name,
                            IsPreSelected = pvaValue.IsPreSelected
                        };
                        pvaModel.Values.Add(pvaValueModel);
                    }
                }

                model.ProductVariantAttributes.Add(pvaModel);
            }
        }

        private async Task PrepareVariantCombinationPicturesAsync(ProductVariantAttributeCombinationModel model, Product product)
        {
            var files = (await _db.ProductMediaFiles
                .ApplyProductFilter(product.Id)
                .Include(x => x.MediaFile)
                .ToListAsync())
                .Select(x => x.MediaFile)
                .ToList(); ;

            foreach (var file in files)
            {
                model.AssignablePictures.Add(new ProductVariantAttributeCombinationModel.PictureSelectItemModel
                {
                    Id = file.Id,
                    IsAssigned = model.AssignedPictureIds.Contains(file.Id),
                    Media = _mediaService.ConvertMediaFile(file)
                });
            }
        }
        private void PrepareViewBag(string btnId, string formId, bool refreshPage = false, bool isEdit = true)
        {
            ViewBag.btnId = btnId;
            ViewBag.formId = formId;
            ViewBag.RefreshPage = refreshPage;
            ViewBag.IsEdit = isEdit;
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> ProductVariantAttributeCombinationList(GridCommand command, int productId)
        {
            var model = new GridModel<ProductVariantAttributeCombinationModel>();
            var customer = _workContext.CurrentCustomer;
            var product = await _db.Products.FindByIdAsync(productId);
            var productUrlTitle = T("Common.OpenInShop");
            var productSeName = await product.GetActiveSlugAsync();
            var allCombinations = await _db.ProductVariantAttributeCombinations
                .AsNoTracking()
                .Where(x => x.ProductId == product.Id)
                .ApplyGridCommand(command)
                .ToListAsync();

            await _productAttributeMaterializer.PrefetchProductVariantAttributesAsync(allCombinations.Select(x => x.AttributeSelection));

            var productVariantAttributesModel = await allCombinations.SelectAsync(async x =>
            {
                var pvacModel = await MapperFactory.MapAsync<ProductVariantAttributeCombination, ProductVariantAttributeCombinationModel>(x);
                pvacModel.ProductId = product.Id;
                pvacModel.ProductUrlTitle = productUrlTitle;
                pvacModel.ProductUrl = await _productUrlHelper.GetProductUrlAsync(product.Id, productSeName, x.AttributeSelection);
                pvacModel.AttributesXml = await _productAttributeFormatter.FormatAttributesAsync(x.AttributeSelection, product, customer, "<br />", htmlEncode: false, includeHyperlinks: false);

                return pvacModel;
            })
            .AsyncToList();

            model.Rows = productVariantAttributesModel;
            model.Total = allCombinations.Count;

            return Json(model);
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> ProductVariantAttributeCombinationDelete(GridSelection selection)
        {
            var ids = selection.GetEntityIds();
            var numDeleted = 0;

            if (ids.Any())
            {
                numDeleted = await _db.ProductBundleItem
                    .AsQueryable()
                    .Where(x => ids.Contains(x.Id))
                    .BatchDeleteAsync();

                foreach (var id in ids)
                {
                    var pvac = await _db.ProductVariantAttributeCombinations.FindByIdAsync(id);
                    var productId = pvac.ProductId;

                    _db.ProductVariantAttributeCombinations.Remove(pvac);
                }

                await _db.SaveChangesAsync();
            }

            return Json(new { Success = true, Count = numDeleted });
        }

        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> AttributeCombinationCreatePopup(string btnId, string formId, int productId)
        {
            var product = await _db.Products.FindByIdAsync(productId);
            if (product == null)
            {
                return RedirectToAction("List", "Product");
            }

            var model = new ProductVariantAttributeCombinationModel();
            await PrepareProductAttributeCombinationModelAsync(model, null, product);
            await PrepareVariantCombinationAttributesAsync(model, product);
            await PrepareVariantCombinationPicturesAsync(model, product);
            PrepareViewBag(btnId, formId, false, false);

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> AttributeCombinationCreatePopup(
            string btnId,
            string formId,
            int productId,
            ProductVariantAttributeCombinationModel model,
            ProductVariantQuery query)
        {
            var product = await _db.Products.FindByIdAsync(productId);
            if (product == null)
            {
                return RedirectToAction("List", "Product");
            }

            var warnings = new List<string>();
            var variantAttributes = _db.ProductVariantAttributes.ApplyProductFilter(new[] { product.Id });

            // INFO: (mh) (core) Old code
            //var attributeXml = query.CreateSelectedAttributesXml(product.Id, 0, variantAttributes, _productAttributeParser, _localizationService,
            //    _downloadService, _catalogSettings, this.Request, warnings);

            //warnings.AddRange(_shoppingCartService.GetShoppingCartItemAttributeWarnings(_workContext.CurrentCustomer, ShoppingCartType.ShoppingCart, product, attributeXml));

            // TODO: (mh) (core) What a fucked up mess! Nothing can be found easily...
            //var selection = await _productAttributeMaterializer.CreateAttributeSelectionAsync(query, variantAttributes, product.Id, 0);

            //var cart = await _shoppingCartService.GetCartAsync(_workContext.CurrentCustomer, ShoppingCartType.ShoppingCart);
            //await _shoppingCartValidator.ValidateProductAttributesAsync(new ShoppingCartItem { Product = product, AttributeSelection = selection.Selection }, cart.Items, warnings);

            // TODO: (mh) (core) Lets try this again when the code abaove was implemented correctly.
            //if (_productAttributeParser.FindProductVariantAttributeCombination(product.Id, attributeXml) != null)
            //{
            //    warnings.Add(_localizationService.GetResource("Admin.Catalog.Products.ProductVariantAttributes.AttributeCombinations.CombiExists"));
            //}

            if (warnings.Count == 0)
            {
                var combination = await MapperFactory.MapAsync<ProductVariantAttributeCombinationModel, ProductVariantAttributeCombination>(model);
                // TODO: (mh) (core) Lets try this again when the code above was implemented correctly.
                //combination.RawAttributes = attributeXml;
                combination.SetAssignedMediaIds(model.AssignedPictureIds);

                _db.ProductVariantAttributeCombinations.Add(combination);
                await _db.SaveChangesAsync();
            }

            await PrepareProductAttributeCombinationModelAsync(model, null, product);
            await PrepareVariantCombinationAttributesAsync(model, product);
            await PrepareVariantCombinationPicturesAsync(model, product);
            PrepareViewBag(btnId, formId, warnings.Count == 0, false);

            if (warnings.Count > 0)
            {
                model.Warnings = warnings;
            }

            return View(model);
        }

        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> AttributeCombinationEditPopup(int id, string btnId, string formId)
        {
            var combination = await _db.ProductVariantAttributeCombinations.FindByIdAsync(id, false);
            if (combination == null)
            {
                return RedirectToAction("List", "Product");
            }

            var product = await _db.Products.FindByIdAsync(combination.ProductId, false);
            if (product == null)
            {
                return RedirectToAction("List", "Product");
            }

            var model = await MapperFactory.MapAsync<ProductVariantAttributeCombination, ProductVariantAttributeCombinationModel>(combination);

            await PrepareProductAttributeCombinationModelAsync(model, combination, product, true);
            await PrepareVariantCombinationAttributesAsync(model, product);
            await PrepareVariantCombinationPicturesAsync(model, product);
            PrepareViewBag(btnId, formId);

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> AttributeCombinationEditPopup(string btnId, string formId, ProductVariantAttributeCombinationModel model)
        {
            if (ModelState.IsValid)
            {
                var combination = await _db.ProductVariantAttributeCombinations.FindByIdAsync(model.Id);
                if (combination == null)
                {
                    return RedirectToAction("List", "Product");
                }

                var attributeXml = combination.RawAttributes;
                await MapperFactory.MapAsync(model, combination);
                combination.RawAttributes = attributeXml;
                combination.SetAssignedMediaIds(model.AssignedPictureIds);

                await _db.SaveChangesAsync();

                PrepareViewBag(btnId, formId, true);
            }

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> CreateAllAttributeCombinations(ProductVariantAttributeCombinationModel model, int productId)
        {
            var product = await _db.Products.FindByIdAsync(productId, false);
            if (product == null)
            {
                throw new ArgumentException(T("Products.NotFound", productId));
            }

            await _productAttributeService.CreateAllAttributeCombinationsAsync(productId);

            return Json(string.Empty);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.EditVariant)]
        public async Task<IActionResult> DeleteAllAttributeCombinations(ProductVariantAttributeCombinationModel model, int productId)
        {
            var product = await _db.Products.FindByIdAsync(productId, false);
            if (product == null)
            {
                throw new ArgumentException(T("Products.NotFound", productId));
            }

            await _db.ProductVariantAttributeCombinations
                .AsQueryable()
                .Where(x => x.ProductId == product.Id)
                .BatchDeleteAsync();

            return Json(string.Empty);
        }

        [HttpPost]
        [Permission(Permissions.Catalog.Product.Read)]
        public async Task<IActionResult> CombinationExistenceNote(int productId, ProductVariantQuery query)
        {
            var warnings = new List<string>();
            var attributes = await _db.ProductVariantAttributes.ApplyProductFilter(new[] { productId }).ToListAsync();

            // TODO: (mh) (core) Do this right!
            var exists = false;
            //var attributeXml = query.CreateSelectedAttributesXml(productId, 0, attributes, _productAttributeParser,
            //    _localizationService, _downloadService, _catalogSettings, Request, warnings);

            //var exists = _productAttributeParser.FindProductVariantAttributeCombination(productId, attributeXml) != null;
            //if (!exists)
            //{
            //    var product = await _db.Products.FindByIdAsync(productId, false);
            //    if (product != null)
            //    {
            //        warnings.AddRange(_shoppingCartService.GetShoppingCartItemAttributeWarnings(_workContext.CurrentCustomer, ShoppingCartType.ShoppingCart, product, attributeXml));
            //    }
            //}

            if (warnings.Count > 0)
            {
                return new JsonResult(new { Message = warnings[0], HasWarning = true });
            }

            return new JsonResult(
                new
                {
                    Message = T(exists ?
                        "Admin.Catalog.Products.ProductVariantAttributes.AttributeCombinations.CombiExists" :
                        "Admin.Catalog.Products.Variants.ProductVariantAttributes.AttributeCombinations.CombiNotExists"
                    ),
                    HasWarning = exists
                }
            );
        }

        #endregion
    }
}