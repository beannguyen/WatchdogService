using ExternalHealthCheck.Models;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ExternalHealthCheck.Helper
{
    public class StateManagerHelper
    {
        public static string ExternalResourceDict = "externalResourceDict";

        public static async Task<IReliableDictionary<string, ExternalResource>> GetExternalResourceDictAsync(IReliableStateManager _stateManager)
        {
            return await _stateManager.GetOrAddAsync<IReliableDictionary<string, ExternalResource>>(ExternalResourceDict).ConfigureAwait(false);
        }

        public static async Task<List<ExternalResource>> GetExternalResourcesAsync(IReliableStateManager _stateManager)
        {
            var dict = await StateManagerHelper.GetExternalResourceDictAsync(_stateManager);

            List<ExternalResource> listResources = new List<ExternalResource>();

            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                Microsoft.ServiceFabric.Data.IAsyncEnumerable<KeyValuePair<string, ExternalResource>> list = await dict.CreateEnumerableAsync(tx, EnumerationMode.Ordered)
                                        .ConfigureAwait(false);
                var asyncEnumerator = list.GetAsyncEnumerator();
                while (await asyncEnumerator.MoveNextAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    listResources.Add(asyncEnumerator.Current.Value);
                }
            }
            return listResources;
        }
    }
}
