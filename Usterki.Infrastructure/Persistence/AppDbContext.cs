using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Usterki.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Usterki.Infrastructure.Identity;

namespace Usterki.Infrastructure.Persistence;

public class AppDbContext : IdentityUserContext<ApplicationUser, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.LastName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(x => x.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();
        });

        modelBuilder.Entity<OrganizationMembership>(entity =>
        {
            entity.ToTable("OrganizationMemberships");

            entity.HasKey(x => new
            {
                x.OrganizationId,
                x.UserId
            });

            entity.Property(x => x.Role)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.JoinedAtUtc)
                .IsRequired();

            entity.HasOne(x => x.Organization)
                .WithMany(x => x.Memberships)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);


            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();
        });

        modelBuilder.Entity<Building>(entity =>
        {
            entity.ToTable("Buildings");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.City)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.PostalCode)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(x => x.Street)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.BuildingNumber)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasOne(x => x.Organization)
                .WithMany(x => x.Buildings)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Unit>(entity =>
        {
            entity.ToTable("Units");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Number)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(x => new { x.BuildingId, x.Number })
                .IsUnique();

            entity.HasOne(x => x.Building)
                .WithMany(x => x.Units)
                .HasForeignKey(x => x.BuildingId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}