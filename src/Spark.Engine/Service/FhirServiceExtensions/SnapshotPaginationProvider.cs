using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Spark.Core;
using Spark.Engine.Core;
using Spark.Engine.Extensions;
using Spark.Engine.Store.Interfaces;
using Spark.Service;
using Task = System.Threading.Tasks.Task;

namespace Spark.Engine.Service.FhirServiceExtensions
{
    public class SnapshotPaginationProvider : ISnapshotPaginationProvider, ISnapshotPagination
    {
        private IFhirStore fhirStore;
        private readonly ITransfer transfer;
        private readonly ILocalhost localhost;
        private readonly ISnapshotPaginationCalculator _snapshotPaginationCalculator;
        private Snapshot snapshot;
     
        public SnapshotPaginationProvider(IFhirStore fhirStore, ITransfer transfer, ILocalhost localhost, ISnapshotPaginationCalculator snapshotPaginationCalculator)
        {
            this.fhirStore = fhirStore;
            this.transfer = transfer;
            this.localhost = localhost;
            _snapshotPaginationCalculator = snapshotPaginationCalculator;
        }

        public ISnapshotPagination StartPagination(Snapshot snapshot)
        {
            this.snapshot = snapshot;
            return this;
        }

        public Bundle GetPage(int? index = null, Action<Entry> transformElement = null)
        {
            return Task.Run(() => GetPageAsync(index, transformElement)).GetAwaiter().GetResult();
        }

        public async Task<Bundle> GetPageAsync(int? index = null, Action<Entry> transformElement = null)
        {
            if (snapshot == null)
                throw Error.NotFound("There is no paged snapshot");

            if (!snapshot.InRange(index ?? 0))
            {
                throw Error.NotFound(
                    "The specified index lies outside the range of available results ({0}) in snapshot {1}",
                    snapshot.Keys.Count(), snapshot.Id);
            }

            return await CreateBundleAsync(index);
        }

        private async Task<Bundle> CreateBundleAsync(int? start = null)
        {
            Bundle bundle = new Bundle();
            bundle.Type = snapshot.Type;
            bundle.Total = snapshot.Count;
            bundle.Id = Guid.NewGuid().ToString();

            List<IKey> keys = _snapshotPaginationCalculator.GetKeysForPage(snapshot, start).ToList();
            var entries = (await fhirStore.GetAsync(keys).ConfigureAwait(false)).ToList();
            if (snapshot.SortBy != null)
            {
                entries = entries.Select(e => new {Entry = e, Index = keys.IndexOf(e.Key)})
                    .OrderBy(e => e.Index)
                    .Select(e => e.Entry).ToList();
            }
            IList<Entry> included = await GetIncludesRecursiveForAsync(entries, snapshot.Includes).ConfigureAwait(false);
            entries.Append(included);

            transfer.Externalize(entries);
            bundle.Append(entries);
            BuildLinks(bundle, start);

            return bundle;
        }


        private async Task<IList<Entry>> GetIncludesRecursiveForAsync(IList<Entry> entries, IEnumerable<string> includes)
        {
            IList<Entry> included = new List<Entry>();

            var latest = await GetIncludesForAsync(entries, includes).ConfigureAwait(false);
            int previouscount;
            do
            {
                previouscount = included.Count;
                included.AppendDistinct(latest);
                latest = await GetIncludesForAsync(latest, includes).ConfigureAwait(false);
            }
            while (included.Count > previouscount);
            return included;
        }

        private async Task<IList<Entry>> GetIncludesForAsync(IList<Entry> entries, IEnumerable<string> includes)
        {
            if (includes == null) return new List<Entry>();

            IEnumerable<string> paths = includes.SelectMany(i => IncludeToPath(i));
            IList<IKey> identifiers = entries.GetResources().GetReferences(paths).Distinct().Select(k => (IKey)Key.ParseOperationPath(k)).ToList();

            IList<Entry> result = (await fhirStore.GetAsync(identifiers).ConfigureAwait(false)).ToList();

            return result;
        }

        private void BuildLinks(Bundle bundle, int? start = null)
        {
            bundle.SelfLink = start == null
                ? localhost.Absolute(new Uri(snapshot.FeedSelfLink, UriKind.RelativeOrAbsolute))
                : BuildSnapshotPageLink(0);
            bundle.FirstLink = BuildSnapshotPageLink(0);
            bundle.LastLink = BuildSnapshotPageLink(_snapshotPaginationCalculator.GetIndexForLastPage(snapshot));

            int? previousPageIndex = _snapshotPaginationCalculator.GetIndexForPreviousPage(snapshot, start);
            if (previousPageIndex != null)
            {
                bundle.PreviousLink = BuildSnapshotPageLink(previousPageIndex);
            }

            int? nextPageIndex = _snapshotPaginationCalculator.GetIndexForNextPage(snapshot, start);
            if (nextPageIndex != null)
            {
                bundle.NextLink = BuildSnapshotPageLink(nextPageIndex);
            }
        }

        private Uri BuildSnapshotPageLink(int? snapshotIndex)
        {
            if (snapshotIndex == null)
                return null;

            Uri baseurl;
            if (string.IsNullOrEmpty(snapshot.Id) == false)
            {
                //baseUrl for statefull pagination
                baseurl = new Uri(localhost.DefaultBase + "/" + FhirRestOp.SNAPSHOT)
                    .AddParam(FhirParameter.SNAPSHOT_ID, snapshot.Id);
            }
            else
            {
                //baseUrl for stateless pagination
                baseurl = new Uri(snapshot.FeedSelfLink);
            }

            return baseurl
                .AddParam(FhirParameter.SNAPSHOT_INDEX, snapshotIndex.ToString());
        }

        private IEnumerable<string> IncludeToPath(string include)
        {
            string[] _include = include.Split(':');
            string resource = _include.FirstOrDefault();
            string paramname = _include.Skip(1).FirstOrDefault();
            var param = ModelInfo.SearchParameters.FirstOrDefault(p => p.Resource == resource && p.Name == paramname);
            if (param != null)
            {
                return param.Path;
            }
            else
            {
                return Enumerable.Empty<string>();
            }
        }
    }
}