using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IAuthenticatorService
{
    Task<AuthenticatorSetupResponse> SetupAsync(int userId, AuthenticatorSetupDto request,
        CancellationToken cancellationToken = default);

    Task<AuthenticatorBackupCodesResponse> ConfirmAsync(int userId, AuthenticatorConfirmDto request,
        CancellationToken cancellationToken = default);

    Task<AuthenticatorBackupCodesResponse> RegenerateBackupCodesAsync(int userId, RegenerateBackupCodesDto request,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(int userId, AuthenticatorRemoveDto request, CancellationToken cancellationToken = default);
    Task RecoverAsync(RecoverAccountDto request, CancellationToken cancellationToken = default);
}