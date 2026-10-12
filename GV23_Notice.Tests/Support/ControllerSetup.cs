using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GV23_Notice.Tests.Support
{
    /// <summary>Gives a controller an HttpContext, a signed-in user, TempData and a fake Url helper.</summary>
    public static class ControllerSetup
    {
        public const string UserName = @"JOBURG\tester";

        public static T Prepare<T>(T controller, Action<HttpContext>? configure = null)
            where T : Controller
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Name, UserName) },
                        authenticationType: "Test"))
            };
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("enotice.test");

            configure?.Invoke(http);

            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());

            var url = new Mock<IUrlHelper>();
            url.Setup(u => u.Action(It.IsAny<UrlActionContext>()))
               .Returns((UrlActionContext c) => $"https://enotice.test/{c.Controller}/{c.Action}");
            controller.Url = url.Object;

            return controller;
        }
    }
}
