using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Usterki.Domain.Entities;

namespace Usterki.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Unit> Units => Set<Unit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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