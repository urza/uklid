using System.ComponentModel.DataAnnotations;

namespace KdyBylUklid.Data;

public class CleaningRecord
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Datum je povinné")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Čas od je povinný")]
    public TimeOnly TimeFrom { get; set; }

    public TimeOnly? TimeTo { get; set; }

    [Required(ErrorMessage = "Počet uklízeček je povinný")]
    [Range(1, 10, ErrorMessage = "Počet uklízeček musí být mezi 1 a 10")]
    public int CleanerCount { get; set; } = 1;

    public bool IsPaid { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsComplete => TimeTo.HasValue;
    public double? TotalHours => TimeTo.HasValue
        ? (TimeTo.Value.ToTimeSpan() - TimeFrom.ToTimeSpan()).TotalHours * CleanerCount
        : null;
}
