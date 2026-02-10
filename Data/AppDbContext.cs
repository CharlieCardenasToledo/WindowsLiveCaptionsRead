using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using WindowsLiveCaptionsReader.Models;

namespace WindowsLiveCaptionsReader.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Session> Sessions { get; set; }
        public DbSet<TranscriptionEntry> Entries { get; set; }
        public DbSet<DetectedQuestion> Questions { get; set; }
        public DbSet<VocabularyItem> Vocabulary { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindowsLiveCaptionsReader");
            
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var dbPath = Path.Combine(folder, "sessions.db");
            options.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure relationships and standard behaviors if needed
            // Configure relationships and standard behaviors
            modelBuilder.Entity<Session>()
                .OwnsOne(s => s.Metadata, builder => 
                {
                    builder.Property(m => m.TopVocabulary)
                           .HasConversion(
                               v => string.Join(";", v),
                               v => new List<string>(v.Split(';', StringSplitOptions.RemoveEmptyEntries)));
                });
            
            // VocabularyItem configuration
            modelBuilder.Entity<VocabularyItem>()
                .Ignore(v => v.SessionIds); // Handled via conversion below
                
            modelBuilder.Entity<VocabularyItem>()
                .Property(v => v.SessionIds)
                .HasConversion(
                    v => string.Join(";", v),
                    v => new List<int>(Array.ConvertAll(v.Split(';', StringSplitOptions.RemoveEmptyEntries), int.Parse)));

        }
    }
}
