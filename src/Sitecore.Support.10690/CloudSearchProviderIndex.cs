using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Azure;
using Sitecore.ContentSearch.Maintenance;
using Sitecore.ContentSearch.Security;
using Sitecore.XA.Foundation.Search.Providers.Azure;

namespace Sitecore.Support.XA.Foundation.Search.Providers.Azure
{

    public class CloudSearchProviderIndex : Sitecore.ContentSearch.Azure.CloudSearchProviderIndex
    {
        public CloudSearchProviderIndex(string name, string connectionStringName, string totalParallelServices, IIndexPropertyStore propertyStore)
            : base(name, connectionStringName, totalParallelServices, propertyStore)
        {
        }

        public CloudSearchProviderIndex(string name, string connectionStringName, string totalParallelServices, IIndexPropertyStore propertyStore, string group)
            : base(name, connectionStringName, totalParallelServices, propertyStore, group)
        {
        }

        public override IProviderSearchContext CreateSearchContext(SearchSecurityOptions options = SearchSecurityOptions.EnableSecurityCheck)
        {
            base.CreateSearchContext(options);
            return new Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchSearchContext(this, options);
        }
    }

}
