using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehicleServiceManagement.Services.Dashboard;

namespace VehicleServiceManagement.Controllers;

[Authorize(Roles = "Admin,Technician")]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await _dashboard.GetSummaryAsync(cancellationToken));
}
