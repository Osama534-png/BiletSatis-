using System.Diagnostics;
using BiletSatis.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BiletSatis.Web.Models;

namespace BiletSatis.Web.Controllers;

public class HomeController : Controller
{
    private readonly BiletSatisDbContext _db;

    public HomeController(BiletSatisDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var etkinlikler = await _db.Etkinlikler.OrderBy(e => e.Tarih).ToListAsync();
        return View(etkinlikler);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
