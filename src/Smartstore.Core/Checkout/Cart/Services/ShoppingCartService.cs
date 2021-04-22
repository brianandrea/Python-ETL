﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Smartstore.Caching;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Attributes;
using Smartstore.Core.Checkout.Cart.Events;
using Smartstore.Core.Common;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Stores;
using Smartstore.Data.Batching;
using Smartstore.Events;

namespace Smartstore.Core.Checkout.Cart
{
    /// <summary>
    /// Shopping cart service methods.
    /// </summary>
    public partial class ShoppingCartService : IShoppingCartService
    {
        // 0 = CustomerId, 1 = CartType, 2 = StoreId
        const string CartItemsKey = "sm.cartitems-{0}-{1}-{2}";
        const string CartItemsPatternKey = "sm.cartitems-*";

        private readonly SmartDbContext _db;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IRequestCache _requestCache;
        private readonly IEventPublisher _eventPublisher;
        private readonly IShoppingCartValidator _cartValidator;
        private readonly IProductAttributeMaterializer _productAttributeMaterializer;
        private readonly ICheckoutAttributeMaterializer _checkoutAttributeMaterializer;
        private readonly Currency _primaryCurrency;

        public ShoppingCartService(
            SmartDbContext db,
            IWorkContext workContext,
            IStoreContext storeContext,
            IRequestCache requestCache,
            IEventPublisher eventPublisher,
            IShoppingCartValidator cartValidator,
            IProductAttributeMaterializer productAttributeMaterializer,
            ICheckoutAttributeMaterializer checkoutAttributeMaterializer)
        {
            _db = db;
            _workContext = workContext;
            _storeContext = storeContext;
            _requestCache = requestCache;
            _eventPublisher = eventPublisher;
            _cartValidator = cartValidator;
            _productAttributeMaterializer = productAttributeMaterializer;
            _checkoutAttributeMaterializer = checkoutAttributeMaterializer;

            _primaryCurrency = storeContext.CurrentStore.PrimaryStoreCurrency;
        }

        public Localizer T { get; set; } = NullLocalizer.Instance;

        public virtual async Task AddItemToCartAsync(AddToCartContext ctx)
        {
            Guard.NotNull(ctx, nameof(ctx));
            Guard.NotNull(ctx.Item, nameof(ctx.Item));

            var customer = ctx.Customer ?? _workContext.CurrentCustomer;
            customer.ShoppingCartItems.Add(ctx.Item);

            _db.TryUpdate(customer);
            await _db.SaveChangesAsync();

            if (ctx.ChildItems.Any())
            {
                foreach (var childItem in ctx.ChildItems)
                {
                    childItem.ParentItemId = ctx.Item.Id;
                }

                customer.ShoppingCartItems.AddRange(ctx.ChildItems);
                _db.TryUpdate(customer);
                await _db.SaveChangesAsync();
            }
        }

        protected virtual async Task<List<OrganizedShoppingCartItem>> OrganizeCartItemsAsync(ICollection<ShoppingCartItem> cart)
        {
            var result = new List<OrganizedShoppingCartItem>();

            if (cart.IsNullOrEmpty())
                return result;

            var parents = cart.Where(x => x.ParentItemId is null);

            // TODO: (ms) (core) to reduce db roundtrips -> load and filter children by parents (id and so on) into lists and try to get from db as batch request
            foreach (var parent in parents)
            {
                var parentItem = new OrganizedShoppingCartItem(parent);

                var children = cart.Where(x => x.ParentItemId != null
                        && x.ParentItemId == parent.Id
                        && x.Id != parent.Id
                        && x.ShoppingCartTypeId == parent.ShoppingCartTypeId
                        && x.Product.CanBeBundleItem());

                // TODO: (ms) (core) Reduce database roundtrips in OrganizeCartItemsAsync
                foreach (var child in children)
                {
                    var childItem = new OrganizedShoppingCartItem(child);

                    if (child.RawAttributes.HasValue()
                        && (parent.Product?.BundlePerItemPricing ?? false)
                        && child.BundleItem != null)
                    {
                        var selection = new ProductVariantAttributeSelection(child.RawAttributes);

                        await _productAttributeMaterializer.MergeWithCombinationAsync(child.Product, selection);

                        var attributeValues = await _productAttributeMaterializer
                            .MaterializeProductVariantAttributeValuesAsync(selection);

                        if (!attributeValues.IsNullOrEmpty())
                        {
                            childItem.BundleItemData.AdditionalCharge = attributeValues.Sum(x => x.PriceAdjustment);
                        }
                    }

                    parentItem.ChildItems.Add(childItem);
                }

                result.Add(parentItem);
            }

            return result;
        }

        // TODO: (ms) (core) TESTING! Make sure it works in any case - Works for ReOrder().
        // TODO: (ms) (core) Test this for bundle & grouped products. Test for items with variants!
        public virtual async Task<bool> AddToCartAsync(AddToCartContext ctx)
        {
            Guard.NotNull(ctx, nameof(ctx));

            // This is called when customer adds a product to cart
            ctx.Customer ??= _workContext.CurrentCustomer;
            ctx.StoreId ??= _storeContext.CurrentStore.Id;

            ctx.Customer.ResetCheckoutData(ctx.StoreId.Value);

            // Checks whether attributes have been selected
            if (ctx.VariantQuery != null)
            {
                // TODO: (ms) (core) fix wrong porting of attribute selection processing in AddToCartAsync.
                // Use _productAttributeMaterializer.CreateAttributeSelectionAsync to process them in context of VariantQuery.

                var attributes = await _db.ProductVariantAttributes
                    //.Include(x => x.ProductAttribute)
                    .ApplyProductFilter(new[] { ctx.Product.Id })
                    .ToListAsync();

                var attributeSelection = await _productAttributeMaterializer.CreateAttributeSelectionAsync(
                    ctx.VariantQuery,
                    attributes,
                    ctx.Product.Id,
                    ctx.BundleItemId);

                ctx.RawAttributes = attributeSelection.Selection.AsJson();

                // Check context for bundle item errors
                if (ctx.Product.ProductType == ProductType.BundledProduct && ctx.RawAttributes.HasValue())
                {
                    ctx.Warnings.Add(T("ShoppingCart.Bundle.NoAttributes"));

                    if (ctx.BundleItem != null)
                        return false;
                }
            }

            if (!await _cartValidator.ValidateAccessPermissionsAsync(ctx.Customer, ctx.CartType, ctx.Warnings))
            {
                return false;
            }

            var cartItems = await GetCartItemsAsync(ctx.Customer, ctx.CartType, ctx.StoreId.Value);

            // Adds required products automatically if it is enabled
            if (ctx.AutomaticallyAddRequiredProducts)
            {
                var requiredProductIds = ctx.Product.ParseRequiredProductIds();
                if (requiredProductIds.Any())
                {
                    var cartProductIds = cartItems.Select(x => x.Item.ProductId);
                    var missingRequiredProductIds = requiredProductIds.Except(cartProductIds);
                    var missingRequiredProducts = await _db.Products.GetManyAsync(missingRequiredProductIds);

                    foreach (var product in missingRequiredProducts)
                    {
                        var item = new ShoppingCartItem
                        {
                            CustomerEnteredPrice = ctx.CustomerEnteredPrice.Amount,
                            RawAttributes = ctx.AttributeSelection.AsJson(),
                            ShoppingCartType = ctx.CartType,
                            StoreId = ctx.StoreId.Value,
                            Quantity = ctx.Quantity,
                            Customer = ctx.Customer,
                            Product = product,
                            ParentItemId = product.ParentGroupedProductId,
                            BundleItemId = ctx.BundleItem?.Id
                        };

                        await AddItemToCartAsync(new AddToCartContext
                        {
                            Item = item,
                            ChildItems = ctx.ChildItems,
                            Customer = ctx.Customer
                        });
                    }
                }
            }

            // Checks whether required products are still missing
            await _cartValidator.ValidateRequiredProductsAsync(ctx.Product, cartItems, ctx.Warnings);

            ShoppingCartItem existingCartItem = null;

            if (ctx.BundleItem == null)
            {
                existingCartItem = cartItems.FindItemInCart(ctx.CartType, ctx.Product, ctx.AttributeSelection, ctx.CustomerEnteredPrice)?.Item;
            }

            // Add item to cart (if no warnings accured)
            if (existingCartItem != null)
            {
                // Product is already in cart, find existing item
                var newQuantity = ctx.Quantity + existingCartItem.Quantity;

                if (!await _cartValidator.ValidateAddToCartItemAsync(ctx, existingCartItem, cartItems))
                {
                    return false;
                }

                // Update cart item
                existingCartItem.Quantity = newQuantity;
                existingCartItem.UpdatedOnUtc = DateTime.UtcNow;
                existingCartItem.RawAttributes = ctx.AttributeSelection.AsJson();
                _db.TryUpdate(ctx.Customer);
                await _db.SaveChangesAsync();
            }
            else
            {
                if (!_cartValidator.ValidateItemsMaximumCartQuantity(ctx.CartType, cartItems.Count, ctx.Warnings))
                {
                    return false;
                }

                // Product is not in cart yet, create new item
                var cartItem = new ShoppingCartItem
                {
                    CustomerEnteredPrice = ctx.CustomerEnteredPrice.Amount,
                    RawAttributes = ctx.RawAttributes,
                    ShoppingCartType = ctx.CartType,
                    StoreId = ctx.StoreId.Value,
                    Quantity = ctx.Quantity,
                    Customer = ctx.Customer,
                    Product = ctx.Product,
                    ProductId = ctx.Product.Id,
                    ParentItemId = null,
                    BundleItemId = ctx.BundleItem?.Id
                };

                if (!await _cartValidator.ValidateAddToCartItemAsync(ctx, cartItem, cartItems))
                {
                    return false;
                }

                // Check whether the product is part of a bundle, the bundle item or just any item.
                // If product is no child of bundle or no bundle at all
                if (ctx.BundleItem == null)
                {
                    // Set cart item as item for simple & bundle products, only if its not set by the caller
                    ctx.Item ??= cartItem;
                }
                else
                {
                    ctx.ChildItems.Add(cartItem);
                }
            }

            _requestCache.RemoveByPattern(CartItemsPatternKey);

            // If ctx.Product is a bundle product and the setting to automatically add bundle products is true, try to add all corresponding BundleItems.

            if (ctx.AutomaticallyAddBundleProducts
                && ctx.Product.ProductType == ProductType.BundledProduct
                && ctx.BundleItem == null
                && ctx.Warnings.Count == 0)
            {
                var bundleItems = await _db.ProductBundleItem
                    .Include(x => x.Product)
                    .Include(x => x.BundleProduct)
                    .ApplyBundledProductsFilter(new[] { ctx.Product.Id }, true)
                    .ToListAsync();

                foreach (var bundleItem in bundleItems)
                {
                    var bundleItemContext = new AddToCartContext
                    {
                        Warnings = new(),
                        Item = ctx.Item,
                        StoreId = ctx.StoreId,
                        Customer = ctx.Customer,
                        CartType = ctx.CartType,
                        BundleItem = bundleItem,
                        ChildItems = ctx.ChildItems,
                        Product = bundleItem.Product,
                        Quantity = bundleItem.Quantity,
                        VariantQuery = ctx.VariantQuery,
                        RawAttributes = ctx.RawAttributes,
                        CustomerEnteredPrice = ctx.CustomerEnteredPrice,
                        AutomaticallyAddRequiredProducts = ctx.AutomaticallyAddRequiredProducts,
                    };

                    // If bundleItem could not be added to the shopping cart, remove child items
                    if (!await AddToCartAsync(bundleItemContext))
                    {
                        ctx.ChildItems.Clear();
                        // TODO: (ms) (core) Add warning for bundle products that are unable to be added to the cart.
                        break;
                    }
                }
            }

            // If context is no bundleItem, add item (parent) and its children (grouped product)
            if ((ctx.Product.ProductType == ProductType.SimpleProduct || ctx.AutomaticallyAddBundleProducts)
                && ctx.BundleItem == null && ctx.Warnings.Count == 0)
            {
                await AddItemToCartAsync(ctx);
            }

            return true;
        }

        public virtual async Task<bool> CopyAsync(AddToCartContext ctx)
        {
            Guard.NotNull(ctx, nameof(ctx));

            if (!await AddToCartAsync(ctx) || ctx.ChildItems == null)
                return false;

            foreach (var childItem in ctx.ChildItems)
            {
                ctx.BundleItem = childItem.BundleItem;
                ctx.Product = childItem.Product;
                ctx.Quantity = childItem.Quantity;
                ctx.RawAttributes = childItem.AttributeSelection.AsJson();
                ctx.CustomerEnteredPrice = new(childItem.CustomerEnteredPrice, ctx.CustomerEnteredPrice.Currency);
                ctx.AutomaticallyAddRequiredProducts = false;
                ctx.AutomaticallyAddBundleProducts = false;

                await AddToCartAsync(ctx);
            }

            _requestCache.RemoveByPattern(CartItemsPatternKey);

            return !ctx.Warnings.Any();
        }

        public virtual async Task<int> DeleteCartItemsAsync(
            IEnumerable<ShoppingCartItem> cartItems,
            bool resetCheckoutData = true,
            bool removeInvalidCheckoutAttributes = false,
            bool deleteChildCartItems = true)
        {
            Guard.NotNull(cartItems, nameof(cartItems));

            var customer = cartItems.Select(x => x.Customer).FirstOrDefault();
            if (resetCheckoutData && customer != null)
            {
                customer.ResetCheckoutData(cartItems.Select(x => x.StoreId).FirstOrDefault());
            }

            _db.ShoppingCartItems.RemoveRange(cartItems);
            await _db.SaveChangesAsync();

            _requestCache.RemoveByPattern(CartItemsPatternKey);

            var cartItemIds = cartItems.Select(x => x.Id).ToList();

            // Delete all child items
            if (deleteChildCartItems && customer != null)
            {
                var childCartItems = await _db.ShoppingCartItems
                    .Where(x => x.CustomerId == customer.Id
                        && x.ParentItemId != null
                        && cartItemIds.Contains(x.ParentItemId.Value)
                        && !cartItemIds.Contains(x.Id))
                    .BatchDeleteAsync();
            }

            var storeId = cartItems.Select(x => x.StoreId).FirstOrDefault();
            var cartType = cartItems.Select(x => x.ShoppingCartType).FirstOrDefault();

            // Validate checkout attributes, removes attributes that require shipping (if cart does not require shipping)
            if (removeInvalidCheckoutAttributes && cartType == ShoppingCartType.ShoppingCart && customer != null)
            {
                var attributeSelection = customer.GenericAttributes.CheckoutAttributes;
                var attributes = await _checkoutAttributeMaterializer.MaterializeCheckoutAttributesAsync(attributeSelection);
                var organizedCartItems = await GetCartItemsAsync(customer, storeId: storeId);
                var attributeIdsToRemove = attributes.GetInvalidShippableAttributesIds();

                attributeSelection.RemoveAttributes(attributeIdsToRemove);
                customer.GenericAttributes.CheckoutAttributes = attributeSelection;
            }

            return await _db.SaveChangesAsync();
        }

        public virtual Task<List<OrganizedShoppingCartItem>> GetCartItemsAsync(
            Customer customer = null,
            ShoppingCartType cartType = ShoppingCartType.ShoppingCart,
            int storeId = 0)
        {
            customer ??= _workContext.CurrentCustomer;

            var cacheKey = CartItemsKey.FormatInvariant(customer.Id, (int)cartType, storeId);
            var result = _requestCache.Get(cacheKey, async () =>
            {
                await _db.LoadCollectionAsync(customer, x => x.ShoppingCartItems, false, q => 
                {
                    return q
                        .Include(x => x.Product)
                        .ThenInclude(x => x.ProductVariantAttributes);
                });
                
                var cartItems = customer.ShoppingCartItems.FilterByCartType(cartType, storeId);                

                // Prefetch all product variant attributes
                var allAttributes = new ProductVariantAttributeSelection(string.Empty);
                var allAttributeMaps = cartItems.SelectMany(x => x.AttributeSelection.AttributesMap);

                foreach (var attribute in allAttributeMaps)
                {
                    if (allAttributes.AttributesMap.Contains(attribute))
                        continue;

                    allAttributes.AddAttribute(attribute.Key, attribute.Value);
                }

                await _productAttributeMaterializer.MaterializeProductVariantAttributesAsync(allAttributes);

                return await OrganizeCartItemsAsync(cartItems);
            });

            return result;
        }

        public virtual async Task<bool> MigrateCartAsync(Customer fromCustomer, Customer toCustomer)
        {
            Guard.NotNull(fromCustomer, nameof(fromCustomer));
            Guard.NotNull(toCustomer, nameof(toCustomer));

            if (fromCustomer.Id == toCustomer.Id)
                return false;

            var cartItems = await OrganizeCartItemsAsync(fromCustomer.ShoppingCartItems);
            if (cartItems.IsNullOrEmpty())
                return false;

            var storeId = 0;
            foreach (var cartItem in cartItems)
            {
                if (storeId == 0)
                {
                    storeId = cartItem.Item.StoreId;
                }

                var ctx = new AddToCartContext
                {
                    Product = cartItem.Item.Product,
                    RawAttributes = cartItem.Item.AttributeSelection.AsJson(),
                    CustomerEnteredPrice = new Money(cartItem.Item.CustomerEnteredPrice, _primaryCurrency),
                    Quantity = cartItem.Item.Quantity,
                    ChildItems = cartItem.ChildItems.Select(x => x.Item).ToList(),
                    Customer = toCustomer,
                    CartType = cartItem.Item.ShoppingCartType,
                    StoreId = cartItem.Item.StoreId,
                };

                if (!await CopyAsync(ctx))
                {
                    return false;
                }
            }

            if (fromCustomer != null && toCustomer != null)
            {
                _eventPublisher.Publish(new MigrateShoppingCartEvent(fromCustomer, toCustomer, storeId));
            }

            var itemsToDelete = cartItems.Select(x => x.Item);

            return await DeleteCartItemsAsync(itemsToDelete) - itemsToDelete.Count() == 0;
        }

        public virtual async Task<IList<string>> UpdateCartItemAsync(Customer customer, int cartItemId, int newQuantity, bool resetCheckoutData)
        {
            Guard.NotNull(customer, nameof(customer));

            var warnings = new List<string>();

            var cartItem = customer.ShoppingCartItems.FirstOrDefault(x => x.Id == cartItemId && x.ParentItemId == null);
            if (cartItem == null)
                return warnings;

            if (resetCheckoutData)
            {
                customer.ResetCheckoutData(cartItem.StoreId);
            }

            if (newQuantity > 0)
            {
                var ctx = new AddToCartContext
                {
                    Customer = customer,
                    CartType = cartItem.ShoppingCartType,
                    Product = cartItem.Product,
                    StoreId = cartItem.StoreId,
                    RawAttributes = cartItem.AttributeSelection.AsJson(),
                    CustomerEnteredPrice = new Money(cartItem.CustomerEnteredPrice, _primaryCurrency),
                    Quantity = newQuantity,
                    AutomaticallyAddRequiredProducts = false,
                };

                var cartItems = await GetCartItemsAsync(customer, cartItem.ShoppingCartType, cartItem.StoreId);

                if (await _cartValidator.ValidateAddToCartItemAsync(ctx, cartItem, cartItems))
                {
                    cartItem.Quantity = newQuantity;
                    cartItem.UpdatedOnUtc = DateTime.UtcNow;
                    _db.TryUpdate(customer);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    warnings.AddRange(ctx.Warnings);
                }
            }
            else
            {
                await DeleteCartItemsAsync(new[] { cartItem }, resetCheckoutData, true);
            }

            _requestCache.RemoveByPattern(CartItemsPatternKey);

            return warnings;
        }
    }
}