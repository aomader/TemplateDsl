using Benchmarks.Layouts;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Benchmarks.Pages;

public sealed class RazorDashboardModel : PageModel
{
    public Dashboard Data { get; private set; } = null!;

    public void OnGet() => Data = Dashboard.Sample;
}
