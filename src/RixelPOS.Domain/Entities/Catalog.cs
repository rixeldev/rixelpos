namespace RixelPOS.Domain.Entities;

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#00C2A8";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TaxRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int RateBasisPoints { get; set; }
    public bool IsInclusive { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public decimal RatePercent => RateBasisPoints / 100m;
}
