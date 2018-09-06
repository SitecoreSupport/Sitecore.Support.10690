using Sitecore.Abstractions;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Azure;
using Sitecore.ContentSearch.Azure.Query;
using Sitecore.ContentSearch.Diagnostics;
using Sitecore.ContentSearch.Linq.Common;
using Sitecore.ContentSearch.Linq.Indexing;
using Sitecore.ContentSearch.Pipelines.QueryGlobalFilters;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.ContentSearch.Security;
using Sitecore.ContentSearch.Utilities;
using Sitecore.XA.Foundation.Search.Providers.Azure;
using System;
using System.Linq;
namespace Sitecore.Support.XA.Foundation.Search.Providers.Azure
{
    public class CloudSearchSearchContext : Sitecore.ContentSearch.Azure.CloudSearchSearchContext, IProviderSearchContext, IDisposable
    {
        public CloudSearchSearchContext(Sitecore.ContentSearch.Azure.CloudSearchProviderIndex index, SearchSecurityOptions options = SearchSecurityOptions.EnableSecurityCheck)
            : base(index, options)
        {
        }

        IQueryable<TItem> IProviderSearchContext.GetQueryable<TItem>(params IExecutionContext[] executionContexts)
        {
            Sitecore.Support.XA.Foundation.Search.Providers.Azure.LinqToCloudIndex<TItem> linqToCloudIndex = new Sitecore.Support.XA.Foundation.Search.Providers.Azure.LinqToCloudIndex<TItem>(this, executionContexts);
            if (base.Index.Locator.GetInstance<IContentSearchConfigurationSettings>().EnableSearchDebug())
            {
                ((IHasTraceWriter)linqToCloudIndex).TraceWriter = new LoggingTraceWriter(SearchLog.Log);
            }
            IQueryable<TItem> queryable = ((Index<TItem, CloudQuery>)linqToCloudIndex).GetQueryable();
            if (typeof(TItem).IsAssignableFrom(typeof(SearchResultItem)))
            {
                QueryGlobalFiltersArgs queryGlobalFiltersArgs = new QueryGlobalFiltersArgs(queryable, typeof(TItem), executionContexts.ToList());
                base.Index.Locator.GetInstance<ICorePipeline>().Run("contentSearch.getGlobalLinqFilters", queryGlobalFiltersArgs);
                queryable = (IQueryable<TItem>)queryGlobalFiltersArgs.Query;
            }
            return queryable;
        }
    }
}


