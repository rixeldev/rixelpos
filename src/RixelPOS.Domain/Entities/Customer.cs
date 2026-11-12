using RixelPOS.Domain.Common;

namespace RixelPOS.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? TaxId { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public long CreditLimitCents { get; set; }
    public long BalanceCents { get; set; }
    public long StoreCreditCents { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Money Balance => Money.FromCents(BalanceCents);
    public Money StoreCredit => Money.FromCents(StoreCreditCents);
    public Money CreditLimit => Money.FromCents(CreditLimitCents);
    public bool CanBuyOnCredit(long amountCents) =>
        CreditLimitCents > 0 && BalanceCents + amountCents <= CreditLimitCents;
}
