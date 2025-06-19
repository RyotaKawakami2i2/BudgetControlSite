using Microsoft.EntityFrameworkCore;

namespace TaskManagementApp.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<Job> Jobs { get; set; }
        public DbSet<TeamUser> TeamUsers { get; set; }
        public DbSet<TimeEntry> TimeEntries { get; set; }
        public DbSet<StatusMaster> StatusMasters { get; set; }
        public DbSet<PriorityMaster> PriorityMasters { get; set; }
        // 今後、Team, Task, TeamUser, ScheduledTime, ActualTime, Category, Project, Notification, AuditLog なども追加

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<StatusMaster>().HasData(
                new StatusMaster { Id = 1, Name = "未着手", Description = "まだ作業を開始していない" },
                new StatusMaster { Id = 2, Name = "進行中", Description = "作業中" },
                new StatusMaster { Id = 3, Name = "完了", Description = "作業完了" }
            );
            modelBuilder.Entity<PriorityMaster>().HasData(
                new PriorityMaster { Id = 1, Name = "低", Description = "優先度低" },
                new PriorityMaster { Id = 2, Name = "中", Description = "優先度中" },
                new PriorityMaster { Id = 3, Name = "高", Description = "優先度高" }
            );
        }
    }
} 