using Microsoft.EntityFrameworkCore;
using UrbanRoadworks.Models;

namespace UrbanRoadworks.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RoadworkAsset>()
                .HasOne<RoadworkSite>()
                .WithMany()
                .HasForeignKey(a => a.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Canal>()
                .Property(c => c.Geometry)
                .HasColumnType("geometry(LineString,3857)");
        }

        // A DbSet for each project table
        public DbSet<RoadworkSite> RoadworkSites { get; set; }
        public DbSet<RoadworkAsset> RoadworkAssets { get; set; }
        public DbSet<Canal> Canals { get; set; }

    }
}
