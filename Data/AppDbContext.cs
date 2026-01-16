using Microsoft.EntityFrameworkCore;

namespace KdyBylUklid.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<CleaningRecord> CleaningRecords => Set<CleaningRecord>();
}
