// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Graph;
using SnippetsApp.Models;

namespace SnippetsApp.Controllers
{
    [Authorize]
    public class UsersController : BaseController
    {
        private readonly string[] _userScopes =
            new [] { GraphConstants.UserReadWrite, GraphConstants.UserReadBasicAll };

        private readonly string[] _userAdminScopes =
            new [] { GraphConstants.UserReadWriteAll };

        public UsersController(
            GraphServiceClient graphClient,
            ITokenAcquisition tokenAcquisition,
            ILogger<HomeController> logger) : base(graphClient, tokenAcquisition, logger)
        {
        }

        public IActionResult Index()
        {
            return RedirectToAction("List");
        }

        // GET /Users/Display?userId=""
        // Displays the user without requesting admin level permissions
        // If the user is the signed-in user, they can see everything
        // If the user is a different user, they can only see basic profile
        [AuthorizeForScopes(Scopes = new[]
            { GraphConstants.UserReadWrite,
              GraphConstants.UserReadBasicAll })]
        public async Task<IActionResult> Display(string userId)
        {
            await EnsureScopes(_userScopes);
            return await GetViewForUser(userId);
        }

        // GET /Users/AdminDisplay?userId=""
        // Displays the user, requesting admin level permissions
        // This will allow viewing full profile for all users
        [AuthorizeForScopes(Scopes = new[] { GraphConstants.UserReadWriteAll })]
        public async Task<IActionResult> AdminDisplay(string userId)
        {
            await EnsureScopes(_userAdminScopes);
            return await GetViewForUser(userId);
        }

        // GET /Users/List
        // Lists all users in the org, without requesting admin level permissions
        // Allows viewing of basic profiles, and displays the
        // non-admin UI (no create, delete, etc.)
        [AuthorizeForScopes(Scopes = new[] { GraphConstants.UserReadBasicAll })]
        public async Task<IActionResult> List()
        {
            await EnsureScopes(_userScopes);
            return await GetViewForUserList(false);
        }

        // GET /Users/AdminList
        // Lists all users in the org, requesting admin level permissions
        // Allows viewing of full profiles and uses admin UI, giving
        // access to Create and Delete
        [AuthorizeForScopes(Scopes = new[] { GraphConstants.UserReadWriteAll })]
        public async Task<IActionResult> AdminList()
        {
            await EnsureScopes(_userAdminScopes);
            return await GetViewForUserList(true);
        }

        // GET /Users/Page?pageUrl=""&isAdmin=""
        // Gets the next page of results when the user list is paged
        public async Task<IActionResult> Page(string pageUrl, bool isAdmin)
        {
            return await GetViewForUserList(isAdmin, pageUrl);
        }

        // GET /Users/Create
        // Gets the new user form
        [AuthorizeForScopes(Scopes = new[] { GraphConstants.UserReadWriteAll })]
        public async Task<IActionResult> Create()
        {
            await EnsureScopes(_userAdminScopes);
            try
            {
                // Get the Graph organization to determine the org's
                // default domain name (@contoso.com, etc.)
                // This value will be combined with the entered "user name"
                // to create a UPN for the new user
                var organization = await _graphClient.Organization
                    .Request()
                    // Only need the verified domains
                    .Select(o => new{ o.VerifiedDomains })
                    .GetAsync();

                string domainName = string.Empty;
                foreach (var domain in organization.CurrentPage.First().VerifiedDomains)
                {
                    if (domain.IsDefault ?? true)
                    {
                        domainName = domain.Name;
                        break;
                    }
                }

                return View(model: domainName);
            }
            catch (ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("AdminList")
                    .WithError($"Error getting organization domain", ex.Error.Message);
            }
        }

        // POST /Users/Create
        // Receives the form data and creates the new user
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeForScopes(Scopes = new[] { GraphConstants.UserReadWriteAll })]
        public async Task<IActionResult> Create(string displayName,
                                                string userName,
                                                string domainName,
                                                string password,
                                                string mobilePhone)
        {
            if (string.IsNullOrEmpty(displayName) ||
                string.IsNullOrEmpty(userName) ||
                string.IsNullOrEmpty(domainName) ||
                string.IsNullOrEmpty(password))
            {
                return RedirectToAction("AdminList")
                    .WithError("Invalid data. You must supply a value for Display name, User name, and Password");
            }

            await EnsureScopes(_userAdminScopes);

            try
            {
                // Crate a new user object with supplied values
                var newUser = new User
                {
                    AccountEnabled = true,
                    DisplayName = displayName,
                    UserPrincipalName = $"{userName}{domainName}",
                    MailNickname = userName,
                    MobilePhone = mobilePhone,
                    PasswordProfile = new PasswordProfile
                    {
                        ForceChangePasswordNextSignIn = true,
                        Password = password
                    }
                };

                // Add the user
                await _graphClient.Users
                    .Request()
                    .AddAsync(newUser);

                return RedirectToAction("AdminList")
                    .WithSuccess("User created");
            }
            catch (ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("AdminList")
                    .WithError($"Error getting organization domain", ex.Error.Message);
            }
        }

        // POST /Users/Update
        // Receives form data to update the user's mobile phone number
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeForScopes(Scopes = new[] { GraphConstants.UserReadWrite })]
        public async Task<IActionResult> Update(string userId, string mobilePhone)
        {
            await EnsureScopes(_userAdminScopes);
            return await UpdateUserMobilePhone(userId, mobilePhone);
        }

        // POST /Users/Delete
        // Receives form data to delete a user
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeForScopes(Scopes = new[] { GraphConstants.UserReadWriteAll })]
        public async Task<IActionResult> Delete(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("AdminList")
                    .WithError("User ID cannot be empty.");
            }

            await EnsureScopes(_userAdminScopes);

            try
            {
                await _graphClient.Users[userId]
                    .Request()
                    .DeleteAsync();

                return RedirectToAction("AdminList")
                    .WithSuccess("User deleted");
            }
            catch (ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("AdminList")
                    .WithError($"Error deleteing user with ID {userId}", ex.Error.Message);
            }
        }

        // Builds the UsersDisplayModel and view for displaying a single user
        private async Task<IActionResult> GetViewForUser(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Error", "Home")
                    .WithError("User ID cannot be empty.");
            }

            // Initialize model
            var model = new UserWithPhoto();

            try
            {
                // Use the request builder that matches the ID specified
                var userRequestBuilder = userId.ToLower() == "me" ?
                    _graphClient.Me : _graphClient.Users[userId];

                // Get the requested user
                // Either GET /me or GET /users/userId
                model.User = await userRequestBuilder
                    .Request()
                    // Select just the fields we will use in the app
                    .Select(u => new
                    {
                        u.DisplayName,
                        u.Id,
                        u.Mail,
                        u.MobilePhone,
                        u.UserPrincipalName
                    })
                    .GetAsync();

                // Getting photo, manager, and direct reports
                // is not supported on personal accounts
                if (!User.IsPersonalAccount())
                {
                    try
                    {
                        // Get the full size photo
                        // GET /me/photo/$value or GET /users/userId/photo/$value
                        var fullSizePhoto = await userRequestBuilder
                            .Photo
                            .Content
                            .Request()
                            .GetAsync();

                        model.AddUserPhoto(fullSizePhoto);
                    }
                    catch (ServiceException ex)
                    {
                        // Call can return 404 for a lot of different reasons
                        // (User has no photo, user doesn't have an Exchange Online mailbox, etc.)
                        // So just check for 404, not specific error codes
                        if (!ex.StatusCode.Equals(HttpStatusCode.NotFound))
                        {
                            throw;
                        }
                    }

                    try
                    {
                        // Get the user's manager
                        // This is returned as a DirectoryObject, not a User,
                        // so it must be cast
                        var managerObject = await userRequestBuilder
                            .Manager
                            .Request()
                            .Select("displayName,id")
                            .GetAsync();

                        model.Manager = new List<User> { managerObject as User };
                    }
                    catch (ServiceException ex)
                    {
                        // If not found, continue
                        if (ex.IsMatch(GraphConstants.RequestResourceNotFound))
                        {
                            model.Manager = null;
                        }
                        // If the error is request denied, it means that the token
                        // most likely didn't have an admin scope (User.ReadWriteAll)
                        // Let the user know and give them the option to provide admin consent
                        else if (ex.IsMatch(GraphConstants.RequestDenied) &&
                                !Request.Path.Equals("/Users/AdminDisplay"))
                        {
                            return View("Display", model)
                                .WithInfoActionLink(
                                    "Listing manager and direct reports for this user requires admin consent. " +
                                    "If you are an admin, you can use this link to consent to additional permissions.",
                                    "Provide admin consent",
                                    Url.Action("AdminDisplay", new { userId = userId }));
                        }
                        else throw;
                    }

                    try
                    {
                        // Get the user's manager
                        // This is returned as a list of DirectoryObjects, not Users,
                        // so they must be cast
                        var directReportPage = await userRequestBuilder
                            .DirectReports
                            .Request()
                            .Top(GraphConstants.PageSize)
                            .Select("displayName,id")
                            .GetAsync();

                        // Use an iterator to get all reports
                        model.DirectReports = new List<User>();

                        var pageIterator = PageIterator<DirectoryObject>.CreatePageIterator(
                            _graphClient, directReportPage,
                            (drObject) => {
                                // Executes for each object in the result set
                                User directReport = drObject as User;
                                model.DirectReports.Add(directReport);
                                return true;
                            }
                        );

                        await pageIterator.IterateAsync();
                    }
                    catch (ServiceException ex)
                    {
                        // If not found, continue
                        if (ex.IsMatch(GraphConstants.RequestResourceNotFound))
                        {
                            model.DirectReports = null;
                        }
                        else throw;
                    }
                }

                return View("Display", model);
            }
            catch (ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("Error", "Home")
                    .WithError($"Error getting user with ID {userId}", ex.Error.Message);
            }
        }

        // Builds the UsersDisplayModel and view for listing users
        private async Task<IActionResult> GetViewForUserList(bool isAdmin, string pageRequestUrl = null)
        {
            // Initialize the model
            var model = new UsersListDisplayModel();

            try
            {
                // Is this a page request?
                if (string.IsNullOrEmpty(pageRequestUrl))
                {
                    // Not a page request, so do
                    // GET /users
                    var userPage = await _graphClient.Users
                        .Request()
                        // Get max PageSize number of results
                        .Top(GraphConstants.PageSize)
                        // Sort by display name
                        .OrderBy("displayName")
                        // Only get the fields used by the view
                        .Select(u => new
                        {
                            u.DisplayName,
                            u.Id
                        })
                        .GetAsync();

                    model.UsersList = new List<User>(userPage.CurrentPage);
                    model.NextPageUrl = userPage.NextPageRequest?
                        .GetHttpRequestMessage().RequestUri.ToString();
                }
                else
                {
                    // In this case, a previous GET /users
                    // returned a nextLink to use to get the next
                    // page of results. Instead of using the Graph client request
                    // builder, initialize the request with the nextLink
                    // NOTE: The nextLink contains all of the query parameters needed,
                    // like $top and $orderby
                    var pageRequest = new GraphServiceUsersCollectionRequest(
                        pageRequestUrl, _graphClient, null);

                    var userPage = await pageRequest.GetAsync();

                    model.UsersList = new List<User>(userPage.CurrentPage);
                    model.NextPageUrl = userPage.NextPageRequest?.RequestUrl;
                }

                return View(isAdmin ? "AdminList" : "List", model);
            }
            catch (ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                return RedirectToAction("Error", "Home")
                    .WithError($"Error getting user list", ex.Error.Message);
            }
        }

        // Updates a user's mobile phone number
        private async Task<IActionResult> UpdateUserMobilePhone(string userId, string mobilePhone)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Error", "Home")
                    .WithError("User ID cannot be empty.");
            }

            try
            {
                // Create a new User object and set only
                // the values to update
                var userUpdate = new User
                {
                    MobilePhone = mobilePhone
                };

                // PATCH /users/userId
                var result = await _graphClient.Users[userId]
                    .Request()
                    .UpdateAsync(userUpdate);

                return RedirectToAction("Display", new { userId = userId })
                    .WithSuccess("User updated");
            }
            catch (ServiceException ex)
            {
                InvokeAuthIfNeeded(ex);

                if (ex.IsMatch(GraphConstants.RequestDenied))
                {
                    // If the error is request denied, it means that the token
                    // most likely didn't have an admin scope (User.ReadWriteAll)
                    // Let the user know and give them the option to provide admin consent
                    return RedirectToAction("Display", new { userId = userId })
                        .WithInfoActionLink(
                            "Updating this user requires admin consent. " +
                            "If you are an admin, you can use this link to consent to additional permissions, " +
                            "then retry your request.",
                            "Provide admin consent",
                            Url.Action("AdminDisplay", new { userId = userId }));
                }

                return RedirectToAction("Display", new { userId = userId })
                    .WithError($"Error updating user", ex.Error.Message);
            }
        }
    }
}
