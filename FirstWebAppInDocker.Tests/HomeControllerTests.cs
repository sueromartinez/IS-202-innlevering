using Microsoft.AspNetCore.Mvc;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Models;
using Xunit;

namespace FirstWebAppInDocker.Tests
{
    public class HomeControllerTests
    {
        // GET: skjemasiden skal vises som en vanlig view
        [Fact]
        public void RegisterResource_Get_ReturnsView()
        {
            var controller = new HomeController();

            var result = controller.RegisterResource();

            Assert.IsType<ViewResult>(result);
        }

        // POST med gyldig data: skal redirecte til oversiktssiden
        [Fact]
        public void RegisterResource_Post_ValidModel_RedirectsToResourceOverview()
        {
            var controller = new HomeController();
            var model = new PositionModel
            {
                ResourceType = "Traktor",
                Description = "Stor traktor med frontlaster",
                Latitude = 58.15,
                Longitude = 8.0
            };

            var result = controller.RegisterResource(model);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("ResourceOverview", redirect.ActionName);
        }

        // POST med ugyldig data: skal vise skjemaet på nytt
        [Fact]
        public void RegisterResource_Post_InvalidModel_ReturnsViewWithModel()
        {
            var controller = new HomeController();
            controller.ModelState.AddModelError("ResourceType", "Påkrevd");
            var model = new PositionModel();

            var result = controller.RegisterResource(model);

            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal(model, view.Model);
        }

        // Oversiktssiden skal få en liste med ressurser
        [Fact]
        public void ResourceOverview_ReturnsViewWithList()
        {
            var controller = new HomeController();

            var result = controller.ResourceOverview();

            var view = Assert.IsType<ViewResult>(result);
            Assert.IsAssignableFrom<IEnumerable<PositionModel>>(view.Model);
        }
    }
}