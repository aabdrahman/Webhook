using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<User> _userManager;
    private readonly RepositoryContext _repositoryContext;

    public AuthenticationService(UserManager<User> userManager, RepositoryContext repositoryContext)
    {
        _userManager = userManager;
        _repositoryContext = repositoryContext;

        _logger = Log.ForContext(_className, nameof(AuthenticationService));
    }

    private Serilog.ILogger _logger;
    private const string _methodNmae = "MethodName";
    private const string _className = "ClassName";

    private User? _loggedInUser;
}
