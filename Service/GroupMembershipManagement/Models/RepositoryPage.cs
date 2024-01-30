// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Models
{
    public class RepositoryPage<T>
    {
        /// <summary>
        /// Gets or sets the page of items.
        /// </summary>
        public IEnumerable<T> Items { get; set; }
        /// <summary>
        /// Gets or sets the current page number.
        /// </summary>
        public int PageNumber { get; set; }
        /// <summary>
        /// Gets or sets the number of items in a page.
        /// </summary>
        public int PageSize { get; set; }
        /// <summary>
        /// Gets or sets the total number of items in the collection.
        /// </summary>
        public int TotalCount { get; set; }
        /// <summary>
        /// Gets or sets the total number of pages in the collection.
        /// </summary>
        public int TotalPages { get; set; }
    }
}
