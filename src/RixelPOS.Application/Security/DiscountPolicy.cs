using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Settings;

namespace RixelPOS.Application.Security;

public static class DiscountPolicy
{
    /// <summary>
    /// A cashier needs manager approval when a discount exceeds the configured
    /// percentage of the base amount. Managers and admins can authorize themselves.
    /// </summary>
    public static bool RequiresApproval(BusinessSettings settings, User user, Discount discount, long baseCents)
    {
        if (user.Role != UserRole.Cashier || discount.IsNone || baseCents <= 0)
        {
            return false;
        }

        var cents = discount.ComputeCents(baseCents);
        var percent = cents * 100m / baseCents;
        return percent > settings.DiscountApprovalPercent;
    }

    public static bool VoidRequiresApproval(BusinessSettings settings, User user) =>
        settings.RequireManagerForVoids && user.Role == UserRole.Cashier;

    public static bool RefundRequiresApproval(BusinessSettings settings, User user) =>
        settings.RequireManagerForRefunds && user.Role == UserRole.Cashier;
}
