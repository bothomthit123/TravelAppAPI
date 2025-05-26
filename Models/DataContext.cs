using Microsoft.EntityFrameworkCore;
using SmartTravelAPI.Models;

public class DataContext : DbContext
{
    public DataContext(DbContextOptions<DataContext> options) : base(options) { }

    public DbSet<Account> Accounts { get; set; }
    public DbSet<Place> Place { get; set; } 
    public DbSet<Favorite> Favorite { get; set; }
    public DbSet<SearchHistory> SearchHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>().HasKey(a => a.AccountId);
        modelBuilder.Entity<Place>().HasKey(p => p.PlaceId);
        modelBuilder.Entity<Favorite>().HasKey(f => f.FavoriteId);
        modelBuilder.Entity<SearchHistory>().HasKey(s => s.SearchId);

        modelBuilder.Entity<Favorite>();
            
        modelBuilder.Entity<SearchHistory>()
            .HasOne(s => s.Account)
            .WithMany(a => a.SearchHistories)
            .HasForeignKey(s => s.AccountId);
    }
}
