using Microsoft.AspNetCore.Mvc;

namespace application1.Controllers
{
    public class textController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
