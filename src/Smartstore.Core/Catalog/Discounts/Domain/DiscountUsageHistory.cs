﻿using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Newtonsoft.Json;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Domain;

namespace Smartstore.Core.Catalog.Discounts
{
    internal class DiscountUsageHistoryMap : IEntityTypeConfiguration<DiscountUsageHistory>
    {
        public void Configure(EntityTypeBuilder<DiscountUsageHistory> builder)
        {
            builder.HasOne(x => x.Discount)
                .WithMany()
                .HasForeignKey(x => x.DiscountId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Order)
                .WithMany(x => x.DiscountUsageHistory)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);
        }
    }

    /// <summary>
    /// Represents a usage history item for discounts.
    /// </summary>
    public partial class DiscountUsageHistory : BaseEntity
    {
        private readonly ILazyLoader _lazyLoader;

        public DiscountUsageHistory()
        {
        }

        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private member.", Justification = "Required for EF lazy loading")]
        private DiscountUsageHistory(ILazyLoader lazyLoader)
        {
            _lazyLoader = lazyLoader;
        }

        /// <summary>
        /// Gets or sets the discount identifier.
        /// </summary>
        public int DiscountId { get; set; }

        private Discount _discount;
        /// <summary>
        /// Gets or sets the discount.
        /// </summary>
        [JsonIgnore]
        public Discount Discount
        {
            get => _discount ?? _lazyLoader?.Load(this, ref _discount);
            set => _discount = value;
        }

        /// <summary>
        /// Gets or sets the order identifier.
        /// </summary>
        public int OrderId { get; set; }

        private Order _order;
        /// <summary>
        /// Gets or sets the order.
        /// </summary>
        [JsonIgnore]
        public Order Order
        {
            get => _order ?? _lazyLoader?.Load(this, ref _order);
            set => _order = value;
        }

        /// <summary>
        /// Gets or sets the date of instance creation.
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }
    }
}
