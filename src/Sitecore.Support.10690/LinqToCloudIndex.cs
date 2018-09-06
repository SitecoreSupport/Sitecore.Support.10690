using Newtonsoft.Json.Linq;
using Sitecore.Abstractions;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Azure;
using Sitecore.ContentSearch.Azure.FieldMaps;
using Sitecore.ContentSearch.Azure.Models;
using Sitecore.ContentSearch.Azure.Query;
using Sitecore.ContentSearch.Azure.Schema;
using Sitecore.ContentSearch.Azure.Utils;
using Sitecore.ContentSearch.Diagnostics;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.Linq.Common;
using Sitecore.ContentSearch.Linq.Methods;
using Sitecore.ContentSearch.Linq.Nodes;
using Sitecore.ContentSearch.Linq.Parsing;
using Sitecore.ContentSearch.Pipelines.GetFacets;
using Sitecore.ContentSearch.Pipelines.ProcessFacets;
using Sitecore.ContentSearch.Utilities;
using Sitecore.XA.Foundation.Search.Providers.Azure;
using Sitecore.XA.Foundation.Search.Providers.Azure.Geospatial;
using Sitecore.XA.Foundation.Search.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Sitecore.Support.XA.Foundation.Search.Providers.Azure
{
    public class LinqToCloudIndex<TItem> : Sitecore.ContentSearch.Azure.Query.LinqToCloudIndex<TItem>
    {
        private readonly Sitecore.XA.Foundation.Search.Providers.Azure.CloudQueryOptimizer _queryOptimizer;

        protected readonly Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchSearchContext Context;

        protected readonly ICorePipeline Pipeline;

        protected override QueryMapper<CloudQuery> QueryMapper
        {
            get;
        }

        protected override IQueryOptimizer QueryOptimizer => _queryOptimizer;

        public LinqToCloudIndex(Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchSearchContext context, IExecutionContext executionContext)
            : base((Sitecore.ContentSearch.Azure.CloudSearchSearchContext)context, executionContext)
        {
            ProviderIndexConfiguration configuration = context.Index.Configuration;
            CloudIndexParameters parameters = new CloudIndexParameters(configuration.IndexFieldStorageValueFormatter, configuration.VirtualFields, context.Index.FieldNameTranslator, typeof(TItem), false, new IExecutionContext[1]
            {
            executionContext
            }, context.Index.Schema);
            QueryMapper = new Sitecore.XA.Foundation.Search.Providers.Azure.CloudQueryMapper(parameters);
            _queryOptimizer = new Sitecore.XA.Foundation.Search.Providers.Azure.CloudQueryOptimizer();
            Pipeline = context.Index.Locator.GetInstance<ICorePipeline>();
            Context = context;
        }

        public LinqToCloudIndex(Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchSearchContext context, IExecutionContext[] executionContexts)
            : base((Sitecore.ContentSearch.Azure.CloudSearchSearchContext)context, executionContexts)
        {
            ProviderIndexConfiguration configuration = context.Index.Configuration;
            CloudIndexParameters parameters = new CloudIndexParameters(configuration.IndexFieldStorageValueFormatter, configuration.VirtualFields, context.Index.FieldNameTranslator, typeof(TItem), false, executionContexts, context.Index.Schema);
            QueryMapper = new Sitecore.XA.Foundation.Search.Providers.Azure.CloudQueryMapper(parameters);
            _queryOptimizer = new Sitecore.XA.Foundation.Search.Providers.Azure.CloudQueryOptimizer();
            Context = context;
            Pipeline = context.Index.Locator.GetInstance<ICorePipeline>();
            Context = context;
        }

        public override IQueryable<TItem> GetQueryable()
        {
            Sitecore.XA.Foundation.Search.Spatial.ExpressionParser expressionParser = new Sitecore.XA.Foundation.Search.Spatial.ExpressionParser(typeof(TItem), ItemType, FieldNameTranslator);
            IQueryable<TItem> queryable = new Sitecore.XA.Foundation.Search.Spatial.GenericQueryable<TItem, CloudQuery>(this, QueryMapper, QueryOptimizer, FieldNameTranslator, expressionParser);
            foreach (IPredefinedQueryAttribute item in Enumerable.ToList<object>(Enumerable.SelectMany<Type, object>(GetTypeInheritance(typeof(TItem)), (Func<Type, IEnumerable<object>>)((Type t) => t.GetCustomAttributes(typeof(IPredefinedQueryAttribute), true)))))
            {
                queryable = item.ApplyFilter<TItem>(queryable, ValueFormatter);
            }
            return queryable;
        }

        public override TResult Execute<TResult>(CloudQuery query)
        {
            SearchLog.Log.Debug($"Executing Query [{this.Context.Index.Name}]: {query.Expression}", null);
            SelectMethod selectMethod = this.GetSelectMethod(query);
            int num;
            int num2;
            Dictionary<string, object> dictionary;
            if (typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(SearchResults<>))
            {
                Type type = typeof(TResult).GetGenericArguments()[0];
                List<Dictionary<string, object>> list = this.Execute(query, out num, out num2, out dictionary);
                Type type2 = typeof(CloudSearchResults<>).MakeGenericType(type);
                MethodInfo methodInfo = base.GetType().GetMethod("ApplyScalarMethods", BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(typeof(TResult), type);
                object obj = Activator.CreateInstance(type2, this.Context, list, selectMethod, num, dictionary, base.Parameters, query.VirtualFieldProcessors);
                return (TResult)methodInfo.Invoke(this, new object[3]
                {
                query,
                obj,
                num2
                });
            }
            List<Dictionary<string, object>> searchResults = this.Execute(query, out num, out num2, out dictionary);
            CloudSearchResults<TItem> processedResults = new CloudSearchResults<TItem>((Sitecore.ContentSearch.Azure.CloudSearchSearchContext)this.Context, searchResults, selectMethod, num, dictionary, (IIndexParameters)base.Parameters, (IEnumerable<IFieldQueryTranslator>)query.VirtualFieldProcessors);
            return ApplyScalarMethods<TResult, TItem>(query, processedResults, num2);
        }

        public override IEnumerable<TElement> FindElements<TElement>(CloudQuery query)
        {
            SearchLog.Log.Debug("Executing query: " + query.Expression, null);
            if (EnumerableLinq.ShouldExecuteEnumerableLinqQuery(query))
            {
                return EnumerableLinq.ExecuteEnumerableLinqQuery<IEnumerable<TElement>>((IQuery)query);
            }
            int totalDoc;
            int num;
            Dictionary<string, object> facetResult;
            List<Dictionary<string, object>> searchResults = this.Execute(query, out totalDoc, out num, out facetResult);
            SelectMethod selectMethod = this.GetSelectMethod(query);
            return new CloudSearchResults<TElement>((Sitecore.ContentSearch.Azure.CloudSearchSearchContext)this.Context, searchResults, selectMethod, totalDoc, facetResult, (IIndexParameters)base.Parameters, (IEnumerable<IFieldQueryTranslator>)query.VirtualFieldProcessors).GetSearchResults();
        }

        protected virtual List<Dictionary<string, object>> Execute(CloudQuery query, out int countDoc, out int totalDoc, out Dictionary<string, object> facetResult)
        {
            countDoc = 0;
            totalDoc = 0;
            facetResult = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(query.Expression) || query.Methods.Count > 0)
            {
                Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchProviderIndex cloudSearchProviderIndex = Context.Index as Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchProviderIndex;
                if (cloudSearchProviderIndex == null)
                {
                    return new List<Dictionary<string, object>>();
                }
                if (query.Expression.Contains("This_Is_Equal_ConstNode_Return_Nothing"))
                {
                    return new List<Dictionary<string, object>>();
                }
                string text = OptimizeQueryExpression(query, cloudSearchProviderIndex) + "&$count=true";
                SearchLog.Log.Debug($"Final Query [{Context.Index.Name}]: {text}", null);
                try
                {
                    string text2 = cloudSearchProviderIndex.SearchService.Search(text);
                    if (string.IsNullOrEmpty(text2))
                    {
                        return new List<Dictionary<string, object>>();
                    }
                    List<Dictionary<string, object>> list = Enumerable.ToList<Dictionary<string, object>>(Enumerable.Select<JToken, Dictionary<string, object>>((IEnumerable<JToken>)JObject.Parse(text2).SelectToken("value"), (Func<JToken, Dictionary<string, object>>)((JToken x) => JsonHelper.Deserialize(x.ToString()) as Dictionary<string, object>)));
                    if (list.Count != 0)
                    {
                        totalDoc = JObject.Parse(text2)["@odata.count"].ToObject<int>();
                        countDoc = totalDoc;
                        List<SkipMethod> source = Enumerable.ToList<SkipMethod>(Enumerable.Select<QueryMethod, SkipMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)query.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.Skip)), (Func<QueryMethod, SkipMethod>)((QueryMethod m) => (SkipMethod)m)));
                        if (Enumerable.Any<SkipMethod>((IEnumerable<SkipMethod>)source))
                        {
                            int num = Enumerable.Sum<SkipMethod>((IEnumerable<SkipMethod>)source, (Func<SkipMethod, int>)((SkipMethod skipMethod) => skipMethod.Count));
                            countDoc -= num;
                        }
                        List<TakeMethod> source2 = Enumerable.ToList<TakeMethod>(Enumerable.Select<QueryMethod, TakeMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)query.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.Take)), (Func<QueryMethod, TakeMethod>)((QueryMethod m) => (TakeMethod)m)));
                        if (Enumerable.Any<TakeMethod>((IEnumerable<TakeMethod>)source2))
                        {
                            countDoc = Enumerable.Sum<TakeMethod>((IEnumerable<TakeMethod>)source2, (Func<TakeMethod, int>)((TakeMethod takeMethod) => takeMethod.Count));
                            if (list.Count < countDoc)
                            {
                                countDoc = list.Count;
                            }
                        }
                        if (JObject.Parse(text2).GetValue("@search.facets") != null)
                        {
                            facetResult = JObject.Parse(text2).GetValue("@search.facets").ToObject<Dictionary<string, object>>();
                        }
                        return list;
                    }
                }
                catch (Exception ex)
                {
                    SearchLog.Log.Error($"Azure Search Error [Index={cloudSearchProviderIndex.Name}] ERROR:{ex.Message} Search expression:{query.Expression}", null);
                    throw;
                }
            }
            return new List<Dictionary<string, object>>();
        }

        protected virtual SelectMethod GetSelectMethod(CloudQuery compositeQuery)
        {
            List<SelectMethod> list = Enumerable.ToList<SelectMethod>(Enumerable.Select<QueryMethod, SelectMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)compositeQuery.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.Select)), (Func<QueryMethod, SelectMethod>)((QueryMethod m) => (SelectMethod)m)));
            if (Enumerable.Count<SelectMethod>((IEnumerable<SelectMethod>)list) != 1)
            {
                return null;
            }
            return list[0];
        }

        protected virtual TResult ApplyScalarMethods<TResult, TDocument>(CloudQuery query, CloudSearchResults<TDocument> processedResults, int? totalCount)
        {
            QueryMethod queryMethod = Enumerable.First<QueryMethod>((IEnumerable<QueryMethod>)query.Methods);
            object value;
            switch (queryMethod.MethodType)
            {
                case QueryMethodType.All:
                    value = true;
                    break;
                case QueryMethodType.Any:
                    value = processedResults.Any();
                    break;
                case QueryMethodType.Count:
                    value = processedResults.Count();
                    break;
                case QueryMethodType.ElementAt:
                    value = (((ElementAtMethod)queryMethod).AllowDefaultValue ? processedResults.ElementAtOrDefault(((ElementAtMethod)queryMethod).Index) : processedResults.ElementAt(((ElementAtMethod)queryMethod).Index));
                    break;
                case QueryMethodType.GetResults:
                    {
                        IEnumerable<SearchHit<TDocument>> searchHits = processedResults.GetSearchHits();
                        FacetResults facetResults = this.FormatFacetResults(processedResults.GetFacets(), query.FacetQueries);
                        int num = totalCount ?? ((int)processedResults.Count());
                        value = ReflectionUtility.CreateInstance(typeof(TResult), searchHits, num, facetResults);
                        break;
                    }
                case QueryMethodType.First:
                    value = (((FirstMethod)queryMethod).AllowDefaultValue ? processedResults.FirstOrDefault() : processedResults.First());
                    break;
                case QueryMethodType.GetFacets:
                    value = this.FormatFacetResults(processedResults.GetFacets(), query.FacetQueries);
                    break;
                case QueryMethodType.Last:
                    value = (((LastMethod)queryMethod).AllowDefaultValue ? processedResults.LastOrDefault() : processedResults.Last());
                    break;
                case QueryMethodType.Single:
                    value = (((SingleMethod)queryMethod).AllowDefaultValue ? processedResults.SingleOrDefault() : processedResults.Single());
                    break;
                default:
                    throw new InvalidOperationException("Invalid query method");
            }
            return (TResult)System.Convert.ChangeType(value, typeof(TResult));
        }

        protected virtual FacetResults FormatFacetResults(Dictionary<string, ICollection<KeyValuePair<string, int>>> facetResults, List<FacetQuery> facetQueries)
        {
            if (facetResults == null)
            {
                return null;
            }
            CloudFieldNameTranslator cloudFieldNameTranslator = Context.Index.FieldNameTranslator as CloudFieldNameTranslator;
            IDictionary<string, ICollection<KeyValuePair<string, int>>> dictionary = ProcessFacetsPipeline.Run(Pipeline, new ProcessFacetsArgs(facetResults, facetQueries, facetQueries, Context.Index.Configuration.VirtualFields, cloudFieldNameTranslator));
            FacetResults facetResults2 = new FacetResults();
            if (cloudFieldNameTranslator == null)
            {
                return facetResults2;
            }
            foreach (FacetQuery facetQuery in facetQueries)
            {
                if (Enumerable.Count<string>(facetQuery.FieldNames) > 1)
                {
                    throw new NotSupportedException("Pivot faceting is not supported by Azure Search provider.");
                }
                string key = Enumerable.Single<string>(facetQuery.FieldNames);
                if (dictionary.ContainsKey(key))
                {
                    ICollection<KeyValuePair<string, int>> source = dictionary[key];
                    if (facetQuery.MinimumResultCount > 0)
                    {
                        source = Enumerable.ToList<KeyValuePair<string, int>>(Enumerable.Where<KeyValuePair<string, int>>((IEnumerable<KeyValuePair<string, int>>)source, (Func<KeyValuePair<string, int>, bool>)((KeyValuePair<string, int> cv) => cv.Value >= facetQuery.MinimumResultCount)));
                    }
                    IEnumerable<FacetValue> values = Enumerable.Select<KeyValuePair<string, int>, FacetValue>((IEnumerable<KeyValuePair<string, int>>)source, (Func<KeyValuePair<string, int>, FacetValue>)((KeyValuePair<string, int> v) => new FacetValue(v.Key, v.Value)));
                    CloudSearchFieldConfiguration cloudFieldConfigurationByCloudFieldName = ((ICloudFieldMap)((CloudIndexConfiguration)Context.Index.Configuration).FieldMap).GetCloudFieldConfigurationByCloudFieldName(facetQuery.CategoryName);
                    string name = (cloudFieldConfigurationByCloudFieldName == null) ? facetQuery.CategoryName : cloudFieldConfigurationByCloudFieldName.FieldName;
                    facetResults2.Categories.Add(new FacetCategory(name, values));
                }
            }
            return facetResults2;
        }

        protected virtual string OptimizeQueryExpression(CloudQuery query, Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchProviderIndex index)
        {
            string text = query.Expression;
            List<SkipMethod> source = Enumerable.ToList<SkipMethod>(Enumerable.Select<QueryMethod, SkipMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)query.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.Skip)), (Func<QueryMethod, SkipMethod>)((QueryMethod m) => (SkipMethod)m)));
            if (Enumerable.Any<SkipMethod>((IEnumerable<SkipMethod>)source))
            {
                int num = Enumerable.Sum<SkipMethod>((IEnumerable<SkipMethod>)source, (Func<SkipMethod, int>)((SkipMethod skipMethod) => skipMethod.Count));
                text += $"&$skip={num}";
            }
            List<TakeMethod> source2 = Enumerable.ToList<TakeMethod>(Enumerable.Select<QueryMethod, TakeMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)query.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.Take)), (Func<QueryMethod, TakeMethod>)((QueryMethod m) => (TakeMethod)m)));
            if (Enumerable.Any<TakeMethod>((IEnumerable<TakeMethod>)source2))
            {
                int num2 = Enumerable.Sum<TakeMethod>((IEnumerable<TakeMethod>)source2, (Func<TakeMethod, int>)((TakeMethod takeMethod) => takeMethod.Count));
                text += $"&$top={num2}";
            }
            string facetExpression = GetFacetExpression(query, index);
            text = CloudQueryBuilder.Merge(text, facetExpression, "and", CloudQueryBuilder.ShouldWrap.Both);
            if (CloudQueryBuilder.IsSearchExpression(text) && !text.Contains("&queryType="))
            {
                text += "&queryType=full";
            }
            string orderByExpression = GetOrderByExpression(query, index);
            return $"{text}{orderByExpression}";
        }

        protected virtual string GetFacetExpression(CloudQuery query, Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchProviderIndex index)
        {
            string text = string.Empty;
            List<GetResultsMethod> source = Enumerable.ToList<GetResultsMethod>(Enumerable.Select<QueryMethod, GetResultsMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)query.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.GetResults)), (Func<QueryMethod, GetResultsMethod>)((QueryMethod m) => (GetResultsMethod)m)));
            List<GetFacetsMethod> source2 = Enumerable.ToList<GetFacetsMethod>(Enumerable.Select<QueryMethod, GetFacetsMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)query.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.GetFacets)), (Func<QueryMethod, GetFacetsMethod>)((QueryMethod m) => (GetFacetsMethod)m)));
            if (query.FacetQueries.Count > 0 && (Enumerable.Any<GetFacetsMethod>((IEnumerable<GetFacetsMethod>)source2) || Enumerable.Any<GetResultsMethod>((IEnumerable<GetResultsMethod>)source)))
            {
                HashSet<FacetQuery> hashSet = EnumerableExtensions.ToHashSet<FacetQuery>(GetFacetsPipeline.Run(Pipeline, new GetFacetsArgs(null, query.FacetQueries, Context.Index.Configuration.VirtualFields, Context.Index.FieldNameTranslator)).FacetQueries);
                List<FacetQuery> list = new List<FacetQuery>();
                foreach (FacetQuery item in hashSet)
                {
                    if (!Enumerable.Any<FacetQuery>((IEnumerable<FacetQuery>)list, (Func<FacetQuery, bool>)((FacetQuery x) => Enumerable.SequenceEqual<string>(x.FieldNames, item.FieldNames))))
                    {
                        list.Add(item);
                    }
                }
                {
                    foreach (FacetQuery item2 in list)
                    {
                        if (Enumerable.Any<string>(item2.FieldNames))
                        {
                            foreach (string fieldName in item2.FieldNames)
                            {
                                string indexFieldName = FieldNameTranslator.GetIndexFieldName(fieldName);
                                IndexedField fieldByCloudName = (Context.Index.Schema as ICloudSearchIndexSchema).GetFieldByCloudName(indexFieldName);
                                if (fieldByCloudName != null)
                                {
                                    string text2 = $"&facet={indexFieldName}";
                                    if (index.MaxTermsCountInFacet != 0)
                                    {
                                        text2 += $",sort:count,count:{index.MaxTermsCountInFacet}";
                                    }
                                    if (item2.FilterValues != null)
                                    {
                                        string text3 = string.Empty;
                                        foreach (object filterValue in item2.FilterValues)
                                        {
                                            text3 = ((!(filterValue is string)) ? CloudQueryBuilder.Merge(text3, CloudQueryBuilder.Filter.Operations.Equal(indexFieldName, filterValue, fieldByCloudName.Type), "or", CloudQueryBuilder.ShouldWrap.None) : CloudQueryBuilder.Merge(text3, CloudQueryBuilder.Search.Operations.Equal(indexFieldName, filterValue, 1f), "or", CloudQueryBuilder.ShouldWrap.None));
                                        }
                                        text = CloudQueryBuilder.Merge(text, text3, "and", CloudQueryBuilder.ShouldWrap.Right);
                                    }
                                    text = CloudQueryBuilder.Merge(text, text2, "and", CloudQueryBuilder.ShouldWrap.Right);
                                }
                            }
                        }
                    }
                    return text;
                }
            }
            return text;
        }

        protected virtual string GetOrderByExpression(CloudQuery query, Sitecore.Support.XA.Foundation.Search.Providers.Azure.CloudSearchProviderIndex index)
        {
            List<OrderByMethod> source = Enumerable.ToList<OrderByMethod>(Enumerable.Select<QueryMethod, OrderByMethod>(Enumerable.Where<QueryMethod>((IEnumerable<QueryMethod>)query.Methods, (Func<QueryMethod, bool>)((QueryMethod m) => m.MethodType == QueryMethodType.OrderBy)), (Func<QueryMethod, OrderByMethod>)((QueryMethod m) => (OrderByMethod)m)));
            if (!Enumerable.Any<OrderByMethod>((IEnumerable<OrderByMethod>)source))
            {
                return string.Empty;
            }
            IEnumerable<OrderByMethod> enumerable = Enumerable.Select<IGrouping<string, OrderByMethod>, OrderByMethod>(Enumerable.GroupBy<OrderByMethod, string>((IEnumerable<OrderByMethod>)source, (Func<OrderByMethod, string>)((OrderByMethod x) => x.Field)), (Func<IGrouping<string, OrderByMethod>, OrderByMethod>)((IGrouping<string, OrderByMethod> x) => Enumerable.First<OrderByMethod>((IEnumerable<OrderByMethod>)x)));
            StringBuilder stringBuilder = new StringBuilder();
            foreach (OrderByMethod item in enumerable)
            {
                string field = item.Field;
                string indexFieldName = Context.Index.FieldNameTranslator.GetIndexFieldName(field, typeof(TItem));
                if (index.SearchService.Schema.AllFieldNames.Contains(indexFieldName))
                {
                    StringBuilder stringBuilder2 = stringBuilder;
                    stringBuilder2.Append(stringBuilder2.ToString().Contains("$orderby") ? "," : "&$orderby=");
                    if (item is OrderByDistanceMethod)
                    {
                        OrderByDistanceMethod orderByDistanceMethod = item as OrderByDistanceMethod;
                        stringBuilder.Append($"geo.distance({item.Field}, geography'Point({orderByDistanceMethod.Coordinates.Latitude} {orderByDistanceMethod.Coordinates.Longitude})')");
                    }
                    else
                    {
                        stringBuilder.Append(indexFieldName);
                    }
                    stringBuilder.Append((item.SortDirection == SortDirection.Descending) ? " desc" : string.Empty);
                }
            }
            return stringBuilder.ToString();
        }

        private IEnumerable<Type> GetTypeInheritance(Type type)
        {
            yield return type;
            Type baseType = type.BaseType;
            while (baseType != (Type)null)
            {
                yield return baseType;
                baseType = baseType.BaseType;
            }
        }
    }


}
