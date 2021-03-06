// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using SnippetsApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Graph;
using System.Threading.Tasks;

namespace SnippetsApp.Controllers
{
    [Authorize]
    [AuthorizeForScopes(Scopes = new [] { GraphConstants.UserReadWrite })]
    public class ExtensionsController : BaseController
    {
        private readonly string[] _userScopes =
            new [] { GraphConstants.UserReadWrite };

        public ExtensionsController(
            GraphServiceClient graphClient,
            ITokenAcquisition tokenAcquisition,
            ILogger<HomeController> logger) : base(graphClient, tokenAcquisition, logger)
        {
        }

        // GET /Extensions
        // Displays the open extension added to the signed-in user
        public async Task<IActionResult> Index()
        {
            await EnsureScopes(_userScopes);

            try
            {
                // GET /me/extensions/com.contoso.roamingSettings
                var extension = await _graphClient.Me
                    .Extensions[RoamingSettings.ExtensionName]
                    .Request()
                    .GetAsync();

                var roamingSettings = RoamingSettings.FromOpenExtension(extension);

                var model = new RoamingSettingsDisplayModel(roamingSettings);

                return View(model);
            }
            catch(ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                if (ex.IsMatch(GraphConstants.ResourceNotFound))
                {
                    return View(new RoamingSettingsDisplayModel(null));
                }

                return RedirectToAction("Error", "Home")
                    .WithError("Error getting open extension on user",
                        ex.Error.Message);
            }
        }

        // POST /Extensions/Create
        // Creates a new extension
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string SelectedTheme,
                                                string SelectedColor,
                                                string SelectedLanguage)
        {
            await EnsureScopes(_userScopes);

            try
            {
                var roamingSettings = RoamingSettings.Create(SelectedTheme, SelectedColor, SelectedLanguage);

                var extension = roamingSettings.ToOpenExtension();

                //POST /me/extensions
                /*
                {
                  "@odata.type": "microsoft.graph.openTypeExtension",
                  "extensionName": "com.contoso.roamingSettings",
                  "theme": "...",
                  "color": "...",
                  "language": "..."
                }
                */
                await _graphClient.Me
                    .Extensions
                    .Request()
                    .AddAsync(extension);

                return RedirectToAction("Index")
                    .WithSuccess("Roaming settings created");
            }
            catch(ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("Index")
                    .WithError("Error creating extension",
                        ex.Error.Message);
            }
        }

        // POST /Extensions/Update
        // Updates the extension with new values
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(string SelectedTheme,
                                                string SelectedColor,
                                                string SelectedLanguage)
        {
            await EnsureScopes(_userScopes);

            try
            {
                var roamingSettings = RoamingSettings.Create(SelectedTheme, SelectedColor, SelectedLanguage);

                var extension = roamingSettings.ToOpenExtension();

                // PATCH /me/extensions/com.contoso.roamingSettings
                /*
                {
                  "@odata.type": "microsoft.graph.openTypeExtension",
                  "extensionName": "com.contoso.roamingSettings",
                  "theme": "...",
                  "color": "...",
                  "language": "..."
                }
                */
                await _graphClient.Me
                    .Extensions[extension.ExtensionName]
                    .Request()
                    .UpdateAsync(extension);

                return RedirectToAction("Index")
                    .WithSuccess("Roaming settings updated");
            }
            catch(ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("Index")
                    .WithError("Error updating extension",
                        ex.Error.Message);
            }
        }

        // POST /Extensions/Delete
        // Deletes the extension from the user
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete()
        {
            await EnsureScopes(_userScopes);

            try
            {
                // DELETE /me/extensions/com.contoso.roamingSettings
                await _graphClient.Me
                    .Extensions[RoamingSettings.ExtensionName]
                    .Request()
                    .DeleteAsync();

                return RedirectToAction("Index")
                    .WithSuccess("Roaming settings deleted");
            }
            catch(ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("Index")
                    .WithError("Error deleting extension",
                        ex.Error.Message);
            }
        }
    }
}
