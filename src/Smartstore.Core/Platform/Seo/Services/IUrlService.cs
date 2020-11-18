﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Smartstore.Domain;

namespace Smartstore.Core.Seo
{
    /// <summary>
    /// Seo slugs service interface
    /// </summary>
    public partial interface IUrlService
    {
        /// <summary>
        /// Gets the active slug for an entity.
        /// </summary>
        /// <param name="entityId">Entity identifier</param>
        /// <param name="entityName">Entity name</param>
        /// <param name="languageId">Language identifier</param>
        /// <returns>Found slug or empty string</returns>
        Task<string> GetActiveSlugAsync(int entityId, string entityName, int languageId);

        /// <summary>
        /// Prefetches a collection of url record properties for a range of entities in one go
        /// and caches them for the duration of the current request.
        /// </summary>
        /// <param name="entityName">Entity name</param>
        /// <param name="entityIds">
        /// The entity ids to prefetch url records for. Can be null,
        /// in which case all records for the requested entity name are loaded.
        /// </param>
        /// <param name="isRange">Whether <paramref name="entityIds"/> represents a range of ids (perf).</param>
        /// <param name="isSorted">Whether <paramref name="entityIds"/> is already sorted (perf).</param>
        /// <param name="tracked">Whether to put prefetched entities to EF change tracker.</param>
        /// <returns>Url record collection</returns>
        /// <remarks>
        /// Be careful not to load large amounts of data at once (e.g. for "Product" scope with large range).
        /// </remarks>
        Task PrefetchUrlRecordsAsync(string entityName, int[] languageIds, int[] entityIds, bool isRange = false, bool isSorted = false, bool tracked = false);

        /// <summary>
        /// Prefetches a collection of url record properties for a range of entities in one go.
        /// </summary>
        /// <param name="entityName">Entity name</param>
        /// <param name="entityIds">
        /// The entity ids to prefetch url records for. Can be null,
        /// in which case all records for the requested entity name are loaded.
        /// </param>
        /// <param name="isRange">Whether <paramref name="entityIds"/> represents a range of ids (perf).</param>
        /// <param name="isSorted">Whether <paramref name="entityIds"/> is already sorted (perf).</param>
        /// <param name="tracked">Whether to put prefetched entities to EF change tracker.</param>
        /// <returns>Url record collection</returns>
        /// <remarks>
        /// Be careful not to load large amounts of data at once (e.g. for "Product" scope with large range).
        /// </remarks>
        Task<UrlRecordCollection> GetUrlRecordCollection(string entityName, int[] languageIds, int[] entityIds, bool isRange = false, bool isSorted = false, bool tracked = false);

        /// <summary>
        /// Applies a slug. The caller is responsible for database commit.
        /// </summary>
        /// <typeparam name="T">Type of slug supporting entity</typeparam>
        /// <param name="entity">Entity instance</param>
        /// <param name="slug">Slug to apply</param>
        /// <param name="languageId">Language ID</param>
		/// <returns>
		/// A <see cref="UrlRecord"/> instance when a new record had to be inserted, <c>null</c> otherwise.
		/// </returns>
        Task<UrlRecord> ApplySlugAsync<T>(T entity, string slug, int languageId, bool save = false) where T : BaseEntity, ISlugSupported;

        /// <summary>
        /// Validates (sanitizes) the slug. Also appends a unique number if it already exists in the database.
        /// </summary>
        /// <typeparam name="T">Type of slug supporting entity</typeparam>
        /// <param name="entity">Entity instance</param>
        /// <param name="slug">Slug to validate</param>
        /// <param name="ensureNotEmpty">Ensure that slug is not empty</param>
        /// <returns>Valid slug</returns>
        Task<string> ValidateSlugAsync<T>(T entity,
            string slug,
            bool ensureNotEmpty,
            int? languageId = null,
            Func<string, UrlRecord> extraSlugLookup = null)
            where T : BaseEntity, ISlugSupported;

        /// <summary>
        /// Gets the number of existing slugs per entity.
        /// </summary>
        /// <param name="urlRecordIds">URL record identifiers</param>
        /// <returns>Dictionary of slugs per entity count</returns>
        Task<Dictionary<int, int>> CountSlugsPerEntityAsync(params int[] urlRecordIds);
    }
}