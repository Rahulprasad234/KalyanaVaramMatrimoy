using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Data;

/// <summary>
/// SQL Server mapping.
/// - List&lt;string&gt; columns (hobbies, partner locations...) are stored as JSON in nvarchar(max) automatically (EF Core primitive collections).
/// - Raw wizard JSON / custom chips are nvarchar(max).
/// - Unique indexes on nullable columns (Mobile, Email, GoogleSub) get "IS NOT NULL" filters automatically.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OtpRequest> OtpRequests => Set<OtpRequest>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OnboardingStepRecord> OnboardingSteps => Set<OnboardingStepRecord>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Sibling> Siblings => Set<Sibling>();
    public DbSet<PartnerPreference> PartnerPreferences => Set<PartnerPreference>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<KycVerification> KycVerifications => Set<KycVerification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // Default every string to nvarchar(200) unless configured otherwise (instead of nvarchar(max))
        c.Properties<string>().HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.MemberId).HasMaxLength(12);
            e.Property(x => x.ProfileFor).HasMaxLength(10);
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.Gender).HasMaxLength(6);
            e.Property(x => x.Mobile).HasMaxLength(15);
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.GoogleSub).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(10);
            e.HasIndex(x => x.MemberId).IsUnique();
            e.HasIndex(x => x.Mobile).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.GoogleSub).IsUnique();

            e.HasOne(x => x.Profile).WithOne().HasForeignKey<Profile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PartnerPreference).WithOne().HasForeignKey<PartnerPreference>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Siblings).WithOne().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Photos).WithOne().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OtpRequest>(e =>
        {
            e.ToTable("OtpRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(20);
            e.Property(x => x.Mobile).HasMaxLength(15);
            e.Property(x => x.Channel).HasMaxLength(10);
            e.Property(x => x.Destination).HasMaxLength(255);
            e.Property(x => x.ProviderMessageId).HasMaxLength(100);
            e.Property(x => x.Purpose).HasMaxLength(10);
            e.Property(x => x.OtpHash).HasMaxLength(100);
            e.Property(x => x.Ip).HasMaxLength(45);
            e.HasIndex(x => new { x.Mobile, x.Channel, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.Property(x => x.Ip).HasMaxLength(45);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OnboardingStepRecord>(e =>
        {
            e.ToTable("OnboardingSteps");
            e.HasKey(x => new { x.UserId, x.Step });
            e.Property(x => x.Data).HasColumnType("nvarchar(max)");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Profile>(e =>
        {
            e.ToTable("Profiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.About).HasMaxLength(2000);
            e.Property(x => x.CustomHobbies).HasColumnType("nvarchar(max)");
        });

        b.Entity<Sibling>(e =>
        {
            e.ToTable("Siblings");
            e.Property(x => x.Relation).HasMaxLength(7);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<PartnerPreference>(e =>
        {
            e.ToTable("PartnerPreferences");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.Expectations).HasMaxLength(2000);
            e.Property(x => x.CustomCities).HasColumnType("nvarchar(max)");
        });

        b.Entity<Photo>(e =>
        {
            e.ToTable("Photos");
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.StorageKey).HasMaxLength(300);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<KycVerification>(e =>
        {
            e.ToTable("KycVerifications");
            e.Property(x => x.DocumentType).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(15);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
