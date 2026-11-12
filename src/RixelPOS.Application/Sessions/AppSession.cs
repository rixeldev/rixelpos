using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Security;

namespace RixelPOS.Application.Sessions;

public sealed class AppSession
{
    public User? CurrentUser { get; private set; }
    public CashSession? CashSession { get; set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public event EventHandler? Changed;

    public void SignIn(User user)
    {
        CurrentUser = user;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUser = null;
        CashSession = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Can(Permission permission) => CurrentUser is not null && RolePermissions.Can(CurrentUser.Role, permission);

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
