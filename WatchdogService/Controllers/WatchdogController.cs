using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WatchdogService.Models;

namespace WatchdogService.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class WatchdogController : Controller
    {
        /// <summary>
        /// TelemetryService instance.
        /// </summary>
        private readonly WatchdogService _service = null;

        /// <summary>
        /// WatchdogController constructor.
        /// </summary><param name="service">Operations class instance.</param>
        internal WatchdogController(WatchdogService service)
        {
            this._service = service;
        }

        #region Watchdog Health for Self Monitoring

        [HttpGet]
        //[Route(@"health")]
        public async Task<IActionResult> GetWatchdogHealth()
        {
            // Check that the Watchdog service class exists.
            if (null == this._service)
            {
                ServiceEventSource.Current.Error(nameof(this.GetWatchdogHealth), "WatchdogService instance is null.");
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }

            // Check the HealthCheckOperation class exists.
            if (null == this._service.HealthCheckOperations)
            {
                ServiceEventSource.Current.Error(nameof(this.GetWatchdogHealth), "HealthCheckOperations instance is null.");
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }

            // Check the MetricsOperations class exists.
            if (null == this._service.MetricsOperations)
            {
                ServiceEventSource.Current.Error(nameof(this.GetWatchdogHealth), "MetricsOperations instance is null.");
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }

            // Check that there are items being monitored.
            IList<HealthCheck> items = await this._service.HealthCheckOperations.GetHealthChecksAsync();
            if (0 == items.Count)
            {
                ServiceEventSource.Current.Warning(nameof(this.GetWatchdogHealth), "No HealthCheck have been registered with the watchdog.");
                return StatusCode((int)HttpStatusCode.NoContent);
            }

            // Return the status.
            return Ok("OK");
        }

        #endregion
    }
}