using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Manages portal user accounts: registration, deactivation, reactivation and lookup.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Registers a new user and assigns them the <c>USER</c> role.
    /// </summary>
    /// <remarks>
    /// If the role cannot be assigned, the user is still created and the response is still
    /// <see cref="System.Net.HttpStatusCode.Created"/>.
    /// </remarks>
    /// <param name="createUser">The new user's details and password. The user name must not contain <c>@</c>.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.Created"/> — user created.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — the user name contains <c>@</c>, or Identity rejected the user (for example, password policy).</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the email address or user name is already taken.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> CreateUserAsync(CreateUserDto createUser, CancellationToken ct = default);
    /// <summary>
    /// Deactivates a user account and locks it out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Admins can deactivate any account directly. Other users can only deactivate their own,
    /// and must send an operation token from a verified
    /// <see cref="Constants.OtpPurpose.DeactivateProfile"/> OTP in the <c>X-Operation-Token</c>
    /// header. The token is validated against the database and its hash, must be issued to the
    /// caller, and is marked consumed on success.
    /// </para>
    /// <para>
    /// Deactivation records the justification, the time and who performed it, and sets a
    /// lockout of roughly ten years.
    /// </para>
    /// </remarks>
    /// <param name="userDeactivationRequest">The user name or email of the account, and the reason for deactivating it.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — account deactivated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Forbidden"/> — a non-admin caller sent no operation token.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — the operation token is invalid, expired, for another purpose, or for another user.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no user with that user name or email.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> DeactivateUserProfileAsync(UserDeactivationRequestDto userDeactivationRequest, CancellationToken ct = default);
    /// <summary>
    /// Marks a deactivated user account as active again.
    /// </summary>
    /// <remarks>
    /// The account is found by an exact, case-insensitive match on the email address (when the value
    /// contains <c>@</c>) or the user name. The lockout set at deactivation is cleared.
    /// </remarks>
    /// <param name="reactivateUser">The user name or email of the account.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — account reactivated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no matching user.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the account is already active.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — Identity could not update the user.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> ReactivateUserProfileAsync(ReactivateUserRequestDto reactivateUser, CancellationToken ct = default);
    /// <summary>
    /// Lists every user that has a role, with that role.
    /// </summary>
    /// <remarks>
    /// Deactivated users are always included, so admins can find and reactivate them.
    /// </remarks>
    /// <param name="includeDeactivated">Not used: deactivated users are always returned.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{UserDetailsDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more users found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — there are no users.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<UserDetailsDto>>> GetAllUsers(bool includeDeactivated = false, CancellationToken ct = default);
    /// <summary>
    /// Gets the details of the user making the request.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="UserDetailsDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — details found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — the caller's user ID claim is missing or invalid.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the user or their role was not found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<UserDetailsDto>> GetUserDetailsAsync(CancellationToken ct = default);
}
