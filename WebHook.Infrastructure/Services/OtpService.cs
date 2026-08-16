using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net;
using System.Text.Json;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.OtpOperation;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class OtpService : IOtpService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly UserManager<User> _userManager;
    private readonly IApplicationHasher _applicationHasher;
    private readonly IOptionsMonitor<TokenValidationConfiguration> _tokenValidationOptionsMonitor;
    private readonly IDataProtector _dataProtector;

    public OtpService(RepositoryContext repositoryContext, UserManager<User> userManager, 
        IApplicationHasher applicationHasher, IOptionsMonitor<TokenValidationConfiguration> tokenValidationOptionsMonitor, IDataProtectionProvider dataProtectionProvider)
    {
        _repositoryContext = repositoryContext;
        _userManager = userManager;
        _applicationHasher = applicationHasher;
        _tokenValidationOptionsMonitor = tokenValidationOptionsMonitor;
        _dataProtector = dataProtectionProvider.CreateProtector("Webhook.Otp.OtpVerificationSigning");

        _logger = Log.ForContext(_className, nameof(OtpService));

    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";
    private ILogger _logger;

    public async Task<GenericResponse<string>> RevokeUserOtpAsync(Guid userId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RevokeUserOtpAsync));

        try
        {
            _logger.Information("Revoke all ununsed otps for user - {0}", userId);

            int totalUpdatedRecords = await _repositoryContext.OtpVerifications.Where(x => !x.IsConsumed && !x.ConsumedAt.HasValue && x.UserId!.Value == userId)
                                                        .ExecuteUpdateAsync(setter => setter.SetProperty(ot => ot.RevokedAt, DateTimeOffset.UtcNow), cancellationToken: ct);

            _logger.Information("All user unverified tokens revoked successfully. Total revoked tokens: {0}", totalUpdatedRecords);

            return totalUpdatedRecords > 0 ?
                GenericResponse<string>.Success("Operation Successful.", $"{totalUpdatedRecords} OTPs revoked successfully.", HttpStatusCode.OK) :
                GenericResponse<string>.Success("Operation Successful.", "No unconsumed otps revoked for user.", HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while revoking user otps.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred revoking otps.", HttpStatusCode.InternalServerError);
        }
    }

    public async Task<GenericResponse<OtpVerificationDto>> ValidateOtpAsync(OtpVerificationRequestDto otpVerificationRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ValidateOtpAsync));

        try
        {
            _logger.Information("Validating OTP request - {0}", otpVerificationRequest);

            User? linkedUser = await _userManager.Users.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.NormalizedEmail == otpVerificationRequest.EmailAddress.ToUpper(), ct);

            if(linkedUser is null)
            {
                _logger.Warning("User with provided email does not exist - {0}", otpVerificationRequest.EmailAddress);
                return GenericResponse<OtpVerificationDto>.Failure(null, "OTP Verification Failed. Invalid Credentials.", HttpStatusCode.BadRequest);
            }

            OtpVerification? linkedUserOtp = await _repositoryContext.OtpVerifications.OrderByDescending(x => x.ConsumedAt).FirstOrDefaultAsync(
                                                                                                x => !x.RevokedAt.HasValue && !x.IsConsumed && x.UserId!.Value == linkedUser.Id && x.ExpiresAt > DateTimeOffset.UtcNow && !x.ValidatedAt.HasValue,
                                                                                                cancellationToken: ct);

            if (linkedUserOtp is null)
            {
                _logger.Warning("Linked OTP to user could not be fetched.");
                return GenericResponse<OtpVerificationDto>.Failure(null, "OTP Verification Failed. OTP Expired.", HttpStatusCode.BadRequest);
            }

            bool isOtpValid = await _applicationHasher.ValidateHashedSecret(linkedUserOtp.OtpHash, otpVerificationRequest.Otp);

            if(!isOtpValid)
            {
                _logger.Warning("Provided OTP: {0} does not match the hashed record for linked otp.", otpVerificationRequest.Otp);
                return GenericResponse<OtpVerificationDto>.Failure(null, "OTP Verification Failed. Invalid Credentials.", HttpStatusCode.BadRequest);
            }

            DateTimeOffset operationTimestamp = DateTimeOffset.UtcNow;
            Guid operationJti = Guid.CreateVersion7(DateTimeOffset.UtcNow);
            linkedUserOtp.ValidatedAt = operationTimestamp;

            var operationVerificationSignCredentials = new OtpVerificationSigning()
            {
                Jti = operationJti.ToString("N"),
                IssuedFor = linkedUser.NormalizedEmail!,
                IssuedAt = operationTimestamp,
                ExpiresAt = operationTimestamp.AddSeconds(_tokenValidationOptionsMonitor.CurrentValue.OtpOperationTokenExpiresAFterInSceonds),
            };

            string signedCredentials = _dataProtector.Protect(JsonSerializer.Serialize(operationVerificationSignCredentials));

            if (string.IsNullOrEmpty(signedCredentials))
            {
                _logger.Warning("Data protector could not generated a signed token for operation.");
                return GenericResponse<OtpVerificationDto>.Failure(null, "OTP Verification Failed. Invalid Credentials.", HttpStatusCode.BadRequest);
            }

            string hashedToken = await _applicationHasher.HashSecret(signedCredentials);

            if (string.IsNullOrEmpty(hashedToken))
            {
                _logger.Warning("Generated operation token could not be hashed successfully.");
                return GenericResponse<OtpVerificationDto>.Failure(null, "OTP Verification Failed. Invalid Credentials.", HttpStatusCode.BadRequest);
            }

            var otpOperationItem = new OtpOperationToken()
            {
                UserId = linkedUser.Id,
                Jti = operationJti,
                Purpose = linkedUserOtp.Purpose,
                ExpiresAt = operationVerificationSignCredentials.ExpiresAt,
                TokenHash = hashedToken,
                OtpVerificationId = linkedUserOtp.Id
            };

            linkedUserOtp.OperationTokens.Add
            (
                otpOperationItem
            );

            await _repositoryContext.SaveChangesAsync(ct);
            _logger.Information("OTP Validation successful. Token issued for operation. User id - {0}", linkedUser.Id);
            return GenericResponse<OtpVerificationDto>.Success(
                                                        new OtpVerificationDto() { ExpiresAt = otpOperationItem.ExpiresAt, SignedToken = signedCredentials },
                                                        "OTP Veriifcation Successful. Token issued for operation.",
                                                        HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while validating OTP.");
            return GenericResponse<OtpVerificationDto>.Failure(null, "An error occurred while validating your OTP. Kindly retry.", HttpStatusCode.InternalServerError);
        }

    }
}
