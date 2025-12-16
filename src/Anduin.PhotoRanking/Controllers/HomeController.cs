using Anduin.PhotoRanking.Models.HomeViewModels;
using Anduin.PhotoRanking.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Anduin.PhotoRanking.Controllers;

[LimitPerMin]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return this.SimpleView(new IndexViewModel());
    }
}
