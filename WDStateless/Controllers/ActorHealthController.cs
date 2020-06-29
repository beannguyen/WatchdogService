using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using Microsoft.ServiceFabric.Actors.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WDActor.Interfaces;

namespace WDStateless.Controllers
{
    [Route("api/[controller]")]
    public class ActorHealthController : Controller
    {
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                ContinuationToken continuationToken = null;
                var actorId = new ActorId(id);
                IActorService actorProxy = ActorServiceProxy.Create<IActorService>(new Uri("fabric:/TestWatchdogApp/WDActorService"), actorId);
                PagedResult<ActorInformation> page = await actorProxy.GetActorsAsync(continuationToken, CancellationToken.None);
                var actor = page.Items.Where(x => x.ActorId.Equals(actorId)).FirstOrDefault();
                if (actor != null)
                {
                    return Ok();
                }
                else
                {
                    return StatusCode(404);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex);
            }
        }

        [HttpPost("{id}")]
        public async Task<IActionResult> Post(string id)
        {
            try
            {
                IWDActor actorProxy = ActorProxy.Create<IWDActor>(new ActorId(id), new Uri("fabric:/TestWatchdogApp/WDActorService"));
                await actorProxy.SetCountAsync(1, CancellationToken.None);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex);
            }
        }
    }
}
