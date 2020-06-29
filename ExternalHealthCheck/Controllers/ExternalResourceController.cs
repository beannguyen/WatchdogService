using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExternalHealthCheck.Helper;
using ExternalHealthCheck.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace ExternalHealthCheck.Controllers
{
    [Route("api/[controller]")]
    public class ExternalResourceController : Controller
    {
        private readonly IReliableStateManager _stateManager;

        public ExternalResourceController(IReliableStateManager stateManager)
        {
            _stateManager = stateManager;
        }

        // GET api/values
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                List<ExternalResource> listResources = await StateManagerHelper.GetExternalResourcesAsync(_stateManager);
                return Ok(listResources);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex);
            }
        }
    }
}
