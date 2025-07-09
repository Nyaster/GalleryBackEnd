using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Route("api/admin/")]
[Authorize(Policy = "AdminOnly")]
public class AdministrationController : ControllerBase
{
    public static bool IsScraping = false;
    private static readonly object Lock = new object();
    private readonly IServiceManager _serviceManager;

    public AdministrationController(IServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    [HttpGet("images")]
    public async Task<ActionResult> GetNotApprovedImages()
    {
        var appImagesToApproveAsync = await _serviceManager.AppAdministrationService.GetAppImagesToApproveAsync();
        return Ok(appImagesToApproveAsync);
    }

    /*[HttpPost("images/{id:int}")]
    public async Task<IActionResult> ChangeImagesApproveStatus(int id, bool approved)
    {
        return Ok();
    }

    public async Task<IActionResult> ParseNewImages()
    {
        return Ok();
    }*/

    [HttpPost("start-scraping")]
    public async Task<IActionResult> StartScraping()
    {
        lock (Lock) // Ensure thread safety for the flag
        {
            if (AdministrationController.IsScraping)
            {
                return BadRequest("Scraping is already in progress.");
            }

            // Set the flag to indicate scraping has started
            AdministrationController.IsScraping = true;
        }

        try
        {
            // Call the service to start scraping
            await _serviceManager.AppImageParser.CheckUpdates();
            return Ok("Scraping started successfully.");
        }
        finally
        {
            lock (Lock)
            {
                // Reset the flag when scraping is complete
                IsScraping = false;
            }
        }
    }
    [HttpPost("downloadImages")]
    public async Task<IActionResult> DownloadImages()
    {
        lock (Lock) // Ensure thread safety for the flag
        {
            if (AdministrationController.IsScraping)
            {
                return BadRequest("Scraping is already in progress.");
            }

            // Set the flag to indicate scraping has started
            AdministrationController.IsScraping = true;
        }

        try
        {
            // Call the service to start scraping
            await _serviceManager.AppImageParser.DownloadAllImages();
            return Ok("Scraping started successfully.");
        }
        finally
        {
            lock (Lock)
            {
                // Reset the flag when scraping is complete
                IsScraping = false;
            }
        }
    }
}