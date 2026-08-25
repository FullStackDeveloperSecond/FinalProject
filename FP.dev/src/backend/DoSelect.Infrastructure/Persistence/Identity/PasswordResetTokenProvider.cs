using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Persistence.Identity;

/// <summary>
/// Options for <see cref="PasswordResetTokenProvider{TUser}"/>. A distinct type (rather than
/// reusing <see cref="DataProtectionTokenProviderOptions"/>) is required because
/// <see cref="DataProtectorTokenProvider{TUser}"/> resolves its options via the unnamed
/// <c>IOptions&lt;DataProtectionTokenProviderOptions&gt;</c> — named options configured against
/// the shared type (e.g. via <c>services.Configure&lt;DataProtectionTokenProviderOptions&gt;("PasswordReset", ...)</c>)
/// are never observed by the provider instance Identity constructs, so the token lifespan silently
/// falls back to the framework default. A dedicated options type sidesteps the named-options gotcha.
/// </summary>
public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>
/// Token provider for password reset tokens, configured independently of the default provider
/// used for email confirmation so the two purposes can have different <see cref="DataProtectionTokenProviderOptions.TokenLifespan"/> values.
/// </summary>
public sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<PasswordResetTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, ToBaseOptions(options), logger)
    where TUser : class
{
    private static IOptions<DataProtectionTokenProviderOptions> ToBaseOptions(
        IOptions<PasswordResetTokenProviderOptions> options) =>
        Microsoft.Extensions.Options.Options.Create<DataProtectionTokenProviderOptions>(options.Value);
}
