using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<User> _userManager;
    private readonly RepositoryContext _repositoryContext;
    private readonly JwtSettingsConfiguration _jwtSettingsConfiguration;
    private readonly SignInManager<User> _signInManager;

    public AuthenticationService(UserManager<User> userManager, RepositoryContext repositoryContext, IOptionsMonitor<JwtSettingsConfiguration> jwtSettingsOptionsMonitor, SignInManager<User> signInManager)
    {
        _userManager = userManager;
        _repositoryContext = repositoryContext;
        _jwtSettingsConfiguration = jwtSettingsOptionsMonitor.CurrentValue;
        _signInManager = signInManager;

        _logger = Log.ForContext(_className, nameof(AuthenticationService));
    }

    private Serilog.ILogger _logger;
    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    private User? _loggedInUser;

    public async Task<GenericResponse<TokenDto>> LoginUserAsync(LoginUserDto loginUserDetails, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(LoginUserAsync));

        try
        {
            _logger.Information("Login user request - {0}", loginUserDetails);

            User? userToAuthenticate = loginUserDetails.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ?
                await _userManager.FindByEmailAsync(loginUserDetails.UserNameOrEmailAddress) :
                await _userManager.FindByNameAsync(loginUserDetails.UserNameOrEmailAddress);

            if(userToAuthenticate is null)
            {
                _logger.Warning(loginUserDetails.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ? "User with email does not exists - {0}" :
                                "User with username does not exists - {0}", loginUserDetails.UserNameOrEmailAddress
                    );

                return GenericResponse<TokenDto>.Failure(null, loginUserDetails.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ? "Invalid Credentials" : "Invalid Credentials",
                    HttpStatusCode.NotFound);
            }

            bool isPasswordValid = await _userManager.CheckPasswordAsync(userToAuthenticate, loginUserDetails.Password);

            if (!isPasswordValid)
            {
                _logger.Warning("User details does not matchthe provided passwrd - {0}", loginUserDetails.UserNameOrEmailAddress);
                return GenericResponse<TokenDto>.Failure(null, "Invalid Credentials.", HttpStatusCode.NotFound);
            }

            _logger.Information("User details successfully validated. Begin user system signin operation");
            SignInResult signinResult = await _signInManager.CheckPasswordSignInAsync(userToAuthenticate, loginUserDetails.Password, true);

            if (!signinResult.Succeeded)
            {
                _logger.Warning("User signin failed - {0}", loginUserDetails.UserNameOrEmailAddress);
                return GenericResponse<TokenDto>.Failure(null, "Invalid Credentials.", HttpStatusCode.BadRequest);
            }

            if(signinResult.IsNotAllowed || signinResult.IsLockedOut)
            {
                _logger.Warning("User could not be signed in successfully. Is locked out - {0}, Is Not Alowed - {1}", signinResult.IsLockedOut, signinResult.IsNotAllowed);
                return GenericResponse<TokenDto>.Failure(null, "User profiled locked out. Kindly contact admin or reset your password.", HttpStatusCode.BadRequest);
            }

            _logger.Information("User signed in successfully. Begin token generation for user.");
            _loggedInUser = userToAuthenticate;
            DateTimeOffset operationTimestamp = DateTimeOffset.UtcNow;

            _loggedInUser.RefreshToken = GenerateRefreshToken();
            _loggedInUser.TokenExpirationTime = operationTimestamp.AddSeconds(_jwtSettingsConfiguration.RefreshTokenExpirationAfterInSeconds);
            _loggedInUser.LastLoginDate = operationTimestamp;
            _loggedInUser.LastAuthenticatedAt = operationTimestamp;

            string token = await GenerateToken();

            var updateUserResult = await _userManager.UpdateAsync(_loggedInUser);

            if (!updateUserResult.Succeeded)
            {
                _logger.Warning("User details could not be saved successfully after token geenration. rrors - {0}", updateUserResult.Errors);
                return GenericResponse<TokenDto>.Failure(null, "An error occurred eprforming operation.", HttpStatusCode.InternalServerError);
            }

            var tokenDetails = new TokenDto(accessToken: token, refreshToken: _loggedInUser.RefreshToken);

            _logger.Information("User with details - {0} aigned in successfully and token generated.", loginUserDetails.UserNameOrEmailAddress);

            return GenericResponse<TokenDto>.Success(tokenDetails, "User signed in successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred performing user login request.");
            return GenericResponse<TokenDto>.Failure(null, "An error occurred.", HttpStatusCode.InternalServerError);
        }
    }



    //-------------------------------------------------
    // Utility operation class sccoped methods.
    //-------------------------------------------------

    private async Task<List<Claim>> GetUserClaims()
    {
        var claims = new List<Claim>();
        var roles = await _userManager.GetRolesAsync(_loggedInUser!);

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        claims.Add(new Claim(ClaimTypes.Email, _loggedInUser?.NormalizedEmail!));
        claims.Add(new Claim(ClaimTypes.Name, _loggedInUser?.NormalizedUserName!));
        claims.Add(new Claim(ClaimTypes.NameIdentifier, _loggedInUser?.Id.ToString()!));
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        claims.Add(new Claim(JwtRegisteredClaimNames.Sub, _loggedInUser?.Id.ToString()!));

        return claims;
    }

    private SigningCredentials GetSigninCredentials()
    {
        string secretKey = Environment.GetEnvironmentVariable("webhook_secret_key") ?? throw new ArgumentNullException("Application secret key is not yet defined.");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        return new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

    }

    private async Task<string> GenerateToken()
    {
        var userClaims = await GetUserClaims();
        var tokenCredentials = GetSigninCredentials();
        var tokenOptions = GetTokenOptions(tokenCredentials, userClaims);

        var tokenHandler = new JwtSecurityTokenHandler();

        return tokenHandler.WriteToken(tokenOptions);
    }

    private JwtSecurityToken GetTokenOptions(SigningCredentials tokenCredentials, List<Claim> userClaims)
    {
        var tokenOptions = new JwtSecurityToken
        (
            issuer: _jwtSettingsConfiguration.ValidIssuer,
            audience: "",
            claims: userClaims,
            expires: DateTime.UtcNow.AddSeconds(_jwtSettingsConfiguration.TokenExpirationAfterInSeconds),
            signingCredentials: tokenCredentials
        );

        return tokenOptions;
    }

    private string GenerateRefreshToken()
    {
        var rndNumBytes = new byte[32];

        using (var rndNumGen = RandomNumberGenerator.Create())
        {
            rndNumGen.GetBytes(rndNumBytes);
        }

        return Convert.ToBase64String(rndNumBytes);
    }
}
