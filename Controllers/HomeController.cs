using MessageProxyApi.Data;
using MessageProxyApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace MessageProxyApi.Controllers
{
    [Authorize(Policy = "AuthorizedLogin")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ProxyDbContext _dbContext;
        private const int PageSize = 100;

        public HomeController(ILogger<HomeController> logger, ProxyDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public IActionResult Index(int? page = 1, DateOnly? startDate = null, DateOnly? endDate = null)
        {
            var currentPage = Math.Max(page.GetValueOrDefault(1), 1);

            var query = _dbContext.CProxyMessages.AsQueryable();

            if (startDate.HasValue)
            {
                var startDateTime = startDate.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(m => m.Received >= startDateTime);
            }

            if (endDate.HasValue)
            {
                var endDateTime = endDate.Value.ToDateTime(TimeOnly.MaxValue);
                query = query.Where(m => m.Received <= endDateTime);
            }

            var totalCount = query.Count();

            var messageLogs = query
                .OrderByDescending(m => m.Received)
                .Skip((currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            var model = new MessageLogViewModel
            {
                Page = currentPage,
                PageSize = PageSize,
                TotalCount = totalCount,
                StartDate = startDate,
                EndDate = endDate,
                Messages = messageLogs
            };

            return View(model);
        }

        public IActionResult Details(int id)
        {
            var message = _dbContext.CProxyMessages
                .FirstOrDefault(m => m.MessageId == id);

            if (message is null)
            {
                return NotFound();
            }

            return View(message);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var exceptionHandlerFeature = HttpContext.Features.Get<IExceptionHandlerFeature>();
            var exception = exceptionHandlerFeature?.Error;

            if (exception is not null)
            {
                _logger.LogError(exception, "Unhandled exception occurred while processing the request.");
            }

            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                Message = exception?.Message ?? "An unexpected error occurred."
            };

            return View(model);
        }
    }
}
