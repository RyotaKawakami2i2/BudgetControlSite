using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Sessions;

namespace TaskYojitsu.Web.Security;

/// <summary>
/// サインイン。有効な（招待を受諾した）利用者だけがログインでき、認証の方式をセッションに記録する（基本設計書 7.1）。
/// セッションは常にブラウザを閉じると消える Cookie にする（「ログインしたままにする」は設けない）。
/// </summary>
public sealed class AppSignInManager(
    UserManager<User> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<User> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<User>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<User> confirmation)
    : SignInManager<User>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <summary>次のサインインで記録する認証の方式（画面が、サインインの前に設定する）。</summary>
    public AuthMethod PendingMethod { get; set; } = AuthMethod.Password;

    public override async Task<bool> CanSignInAsync(User user) =>
        user.Status == UserStatus.Active && await base.CanSignInAsync(user);

    public override Task SignInWithClaimsAsync(User user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        var properties = authenticationProperties ?? new AuthenticationProperties();
        properties.IsPersistent = false;
        var claims = additionalClaims
            .Where(c => c.Type != DbTicketStore.AuthMethodClaim)
            .Append(new Claim(DbTicketStore.AuthMethodClaim, PendingMethod.ToCode()))
            .ToList();
        return base.SignInWithClaimsAsync(user, properties, claims);
    }

    /// <summary>「この端末を記憶する」は使わない（多要素認証を毎回求める）。</summary>
    public override Task RememberTwoFactorClientAsync(User user) => Task.CompletedTask;

    public override Task<bool> IsTwoFactorClientRememberedAsync(User user) => Task.FromResult(false);
}
